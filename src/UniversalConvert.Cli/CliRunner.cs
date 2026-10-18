using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Cli
{
    /// <summary>
    /// 命令行执行器。负责加载插件、解析转换目标、执行转换、输出进度与结果。
    /// </summary>
    internal sealed class CliRunner
    {
        private readonly CliOptions _options;
        private readonly CliOutput _output;
        private readonly List<string> _temporaryFiles = new List<string>();
        private readonly List<string> _placeholders = new List<string>();
        private readonly object _fileStateSync = new object();

        private CoreHost _host;
        private AppConfig _config;
        private CancellationTokenSource _cts;
        private volatile bool _stopRequested;
        private int _succeeded;
        private int _failed;
        private int _skipped;
        private long _outputBytes;
        private DateTime _startedAt;

        public CliRunner(CliOptions options)
        {
            _options = options;
            _output = new CliOutput(options.Json, options.Quiet, options.Verbose, options.Progress);
        }

        public int Run()
        {
            if (_options.Command == CliCommand.Help)
            {
                Console.Out.WriteLine(CliText.Help);
                return CliExitCode.Success;
            }
            if (_options.Command == CliCommand.Version)
            {
                Console.Out.WriteLine(string.Format(CliText.VersionFormat, GetVersion()));
                Console.Out.WriteLine(string.Format(
                    CliText.CoreVersionFormat,
                    CliCompatibility.GetCoreVersion() ?? CliText.UnknownVersion));
                return CliExitCode.Success;
            }
            if (_options.Errors.Count > 0)
            {
                if (_options.Json)
                {
                    foreach (var error in _options.Errors)
                    {
                        _output.ErrorEvent(null, CliExitCode.UsageError, error, null);
                    }
                }
                else
                {
                    foreach (var error in _options.Errors)
                    {
                        Console.Error.WriteLine(error);
                    }
                    Console.Error.WriteLine(CliText.UsageHint);
                }
                return CliExitCode.UsageError;
            }

            LoadHost();

            switch (_options.Command)
            {
                case CliCommand.Convert:
                    return RunConvert();
                case CliCommand.Formats:
                    return RunFormats();
                case CliCommand.Plugins:
                    return RunPlugins();
                default:
                    Console.Error.WriteLine(CliText.UnknownCommandHint);
                    return CliExitCode.UsageError;
            }
        }

        private void LoadHost()
        {
            Log.Configure(
                Path.Combine(ConfigStore.ConfigDirectory, "logs", "cli.log"),
                _options.Verbose ? LogLevel.Debug : LogLevel.Info);

            _config = new ConfigStore().Load();
            _config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var pluginsDirectory = _config.ResolvePluginsDirectory();
            _output.Detail(string.Format(CliText.VerbosePluginDirectory, pluginsDirectory));

            _host = new CoreHost(
                _config,
                pluginsDirectory,
                delegate(string message) { _output.Detail(message); },
                delegate { return false; });

            _output.Detail(string.Format(
                CliText.VerboseLoaded,
                _host.Plugins.Count,
                _host.Registry.Entries.Count));

            if (!_output.Quiet)
            {
                foreach (var error in _host.LoadErrors)
                {
                    Console.Error.WriteLine("警告：" + string.Format(
                        CliText.LoadErrors, error.File + "：" + error.Message));
                }
            }
        }

        private int RunConvert()
        {
            var problems = new List<CliProblem>();
            var files = ExpandInputFiles(problems);
            if (problems.Count > 0)
            {
                return ReportProblems(problems);
            }
            if (files.Count == 0)
            {
                _output.Error(CliText.NoInput);
                return CliExitCode.UsageError;
            }

            var targetExtension = NormalizeExtension(_options.To);
            if (string.IsNullOrEmpty(targetExtension))
            {
                _output.Error(CliText.MissingTargetFormat);
                return CliExitCode.UsageError;
            }

            if (!string.IsNullOrEmpty(_options.Output) && files.Count != 1)
            {
                _output.Error(CliText.OutputOnlySingle);
                return CliExitCode.UsageError;
            }

            var missing = files.Where(f => !File.Exists(f)).ToList();
            if (missing.Count > 0)
            {
                foreach (var file in missing)
                {
                    _output.Error(string.Format(CliText.InputNotFound, file));
                }
                return CliExitCode.InputNotFound;
            }

            var jobs = new List<ConversionJob>();
            var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < files.Count; i++)
            {
                var job = BuildJob(files[i], targetExtension, i + 1, reservedTargets, problems);
                if (job != null) jobs.Add(job);
            }

            if (problems.Count > 0)
            {
                return ReportProblems(problems);
            }

            if (_options.DryRun)
            {
                var skipped = 0;
                foreach (var job in jobs)
                {
                    var targetExists = File.Exists(job.DesiredPath);
                    var sameAsInput = string.Equals(
                        job.DesiredPath, job.InputPath, StringComparison.OrdinalIgnoreCase);

                    if (_options.Skip && (targetExists || sameAsInput))
                    {
                        skipped++;
                        _output.Plan(
                            job.InputPath, job.DesiredPath, job.Entry.PluginName, job.PresetName,
                            false, true, false, sameAsInput ? CliText.SkipReasonSameAsInput : CliText.SkipReasonExists);
                        continue;
                    }

                    var planPath = _options.Force
                        ? job.DesiredPath
                        : FindAvailablePath(job.DesiredPath, job.InputPath);
                    var renamed = !string.Equals(planPath, job.DesiredPath, StringComparison.OrdinalIgnoreCase);
                    _output.Plan(
                        job.InputPath, planPath, job.Entry.PluginName, job.PresetName,
                        _options.Force && targetExists, false, renamed, null);
                }
                _output.PlanSummary(jobs.Count, skipped);
                return CliExitCode.Success;
            }

            if (!ReserveOutputs(jobs, problems))
            {
                CleanupLeftovers();
                return ReportProblems(problems);
            }

            return Execute(jobs, targetExtension);
        }

        private List<string> ExpandInputFiles(IList<CliProblem> problems)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in _options.Files)
            {
                AddInputFile(raw, problems, result, seen);
            }

            foreach (var listPath in _options.FileLists)
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(listPath, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    problems.Add(new CliProblem(
                        CliExitCode.UsageError,
                        string.Format(CliText.FileListReadFailed, listPath, ex.Message)));
                    continue;
                }

                foreach (var line in lines)
                {
                    AddInputFile(line, problems, result, seen);
                }
            }

            return result;
        }

        private static void AddInputFile(
            string raw,
            IList<CliProblem> problems,
            IList<string> files,
            ISet<string> seen)
        {
            var value = NormalizeInputPath(raw);
            if (string.IsNullOrEmpty(value)) return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(value);
            }
            catch (Exception ex)
            {
                problems.Add(new CliProblem(
                    CliExitCode.InputNotFound,
                    string.Format(CliText.InvalidPath, value, ex.Message)));
                return;
            }

            if (seen.Add(fullPath))
            {
                files.Add(fullPath);
            }
        }

        private ConversionJob BuildJob(
            string inputPath,
            string targetExtension,
            int index,
            ISet<string> reservedTargets,
            IList<CliProblem> problems)
        {
            var inputExtension = Path.GetExtension(inputPath);
            var remembered = GetRememberedPlugin(inputExtension, targetExtension);
            var selection = FormatSelector.Resolve(
                _host,
                inputExtension,
                targetExtension,
                _options.PluginId,
                remembered);

            if (selection.PreferredNotFound)
            {
                problems.Add(new CliProblem(
                    CliExitCode.UnsupportedFormat,
                    string.Format(CliText.PluginNotFound, _options.PluginId, selection.DescribeCandidates())));
                return null;
            }
            if (selection.IsEmpty)
            {
                problems.Add(new CliProblem(
                    CliExitCode.UnsupportedFormat,
                    string.Format(CliText.UnsupportedFormat, targetExtension, inputPath)));
                return null;
            }
            if (selection.IsAmbiguous)
            {
                problems.Add(new CliProblem(
                    CliExitCode.UnsupportedFormat,
                    string.Format(CliText.PluginAmbiguous, selection.DescribeCandidates())));
                return null;
            }

            var entry = selection.Selected;
            if (!entry.IsAvailable)
            {
                problems.Add(new CliProblem(
                    CliExitCode.ToolUnavailable,
                    string.Format(CliText.PluginUnavailable, entry.PluginName)));
                return null;
            }

            string presetName;
            string presetError;
            if (!TryResolvePreset(entry, _options.Preset, out presetName, out presetError))
            {
                problems.Add(new CliProblem(CliExitCode.UsageError, presetError));
                return null;
            }

            IDictionary<string, string> overrides;
            string overrideError;
            if (!TryBuildOverrides(entry, out overrides, out overrideError))
            {
                problems.Add(new CliProblem(CliExitCode.UsageError, overrideError));
                return null;
            }

            string desiredPath;
            string targetError;
            if (!TryComputeTarget(inputPath, targetExtension, out desiredPath, out targetError))
            {
                problems.Add(new CliProblem(CliExitCode.UsageError, targetError));
                return null;
            }

            if (string.Equals(desiredPath, inputPath, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new CliProblem(
                    CliExitCode.UsageError,
                    string.Format(CliText.OutputSameAsInput, inputPath)));
                return null;
            }

            if (_options.Force && !reservedTargets.Add(desiredPath))
            {
                problems.Add(new CliProblem(
                    CliExitCode.UsageError,
                    string.Format(CliText.DuplicateOutput, desiredPath)));
                return null;
            }

            var options = PresetMerger.Merge(entry.Options, entry.Presets, presetName, overrides);
            return new ConversionJob
            {
                InputPath = inputPath,
                InputExtension = inputExtension,
                DesiredPath = desiredPath,
                Entry = entry,
                PresetName = presetName,
                Options = options,
                Index = index
            };
        }

        private bool ReserveOutputs(IList<ConversionJob> jobs, IList<CliProblem> problems)
        {
            foreach (var job in jobs)
            {
                var directory = Path.GetDirectoryName(job.DesiredPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    try
                    {
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        problems.Add(new CliProblem(
                            CliExitCode.UsageError,
                            string.Format(CliText.OutputDirCreateFailed, directory, ex.Message)));
                        return false;
                    }
                }

                if (_options.Skip)
                {
                    if (string.Equals(job.DesiredPath, job.InputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        job.Skipped = true;
                        job.SkipReason = CliText.SkipReasonSameAsInput;
                        continue;
                    }
                    if (File.Exists(job.DesiredPath))
                    {
                        job.Skipped = true;
                        job.SkipReason = CliText.SkipReasonExists;
                        continue;
                    }

                    try
                    {
                        if (!OutputPathHelper.TryReserveExactPath(job.DesiredPath))
                        {
                            job.Skipped = true;
                            job.SkipReason = CliText.SkipReasonExists;
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        problems.Add(new CliProblem(
                            CliExitCode.UsageError,
                            string.Format(CliText.OutputReserveFailed, job.DesiredPath, ex.Message)));
                        return false;
                    }

                    job.FinalPath = job.DesiredPath;
                    job.PlaceholderReserved = true;
                    lock (_fileStateSync)
                    {
                        _placeholders.Add(job.FinalPath);
                    }
                    continue;
                }

                if (_options.Force)
                {
                    job.FinalPath = job.DesiredPath;
                    continue;
                }

                bool reserved;
                job.FinalPath = OutputPathHelper.ReserveUniqueOutputPath(
                    job.DesiredPath, job.InputPath, out reserved);
                job.PlaceholderReserved = reserved;

                if (!reserved && File.Exists(job.FinalPath))
                {
                    problems.Add(new CliProblem(
                        CliExitCode.UsageError,
                        string.Format(CliText.OutputExists, job.FinalPath)));
                    return false;
                }

                if (reserved)
                {
                    lock (_fileStateSync)
                    {
                        _placeholders.Add(job.FinalPath);
                    }
                }
            }
            return true;
        }

        private int Execute(IList<ConversionJob> jobs, string targetExtension)
        {
            _startedAt = DateTime.UtcNow;
            _cts = new CancellationTokenSource();
            _succeeded = 0;
            _failed = 0;
            _skipped = 0;
            _outputBytes = 0;
            _stopRequested = false;

            ConsoleCancelEventHandler handler = delegate(object sender, ConsoleCancelEventArgs args)
            {
                args.Cancel = true;
                try { _cts.Cancel(); } catch { }
                _output.Detail("已收到取消请求，正在终止正在执行的转换。");
            };
            Console.CancelKeyPress += handler;

            var cancelled = false;
            try
            {
                ExecuteAsync(jobs, targetExtension).GetAwaiter().GetResult();
            }
            finally
            {
                cancelled = _cts.IsCancellationRequested;
                Console.CancelKeyPress -= handler;
                CleanupLeftovers();
                _cts.Dispose();
            }

            var seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
            _output.Summary(jobs.Count, _succeeded, _failed, _skipped, seconds, _outputBytes);

            if (cancelled)
            {
                _output.Cancelled();
                return CliExitCode.Cancelled;
            }
            if (_failed == 0)
            {
                return CliExitCode.Success;
            }
            if (_succeeded == 0)
            {
                return CliExitCode.ConversionFailed;
            }
            return CliExitCode.PartialFailure;
        }

        private async Task ExecuteAsync(IList<ConversionJob> jobs, string targetExtension)
        {
            if (_options.Jobs <= 1)
            {
                foreach (var job in jobs)
                {
                    if (job.Skipped)
                    {
                        _skipped++;
                        _output.FileSkipped(job.InputPath, job.SkipReason);
                        continue;
                    }
                    if (!CanStart())
                    {
                        _skipped++;
                        _output.FileSkipped(job.InputPath);
                        continue;
                    }
                    await ProcessJobAsync(job, jobs.Count, targetExtension).ConfigureAwait(false);
                }
                return;
            }

            var semaphore = new SemaphoreSlim(_options.Jobs);
            var tasks = new List<Task>();
            foreach (var job in jobs)
            {
                var current = job;
                tasks.Add(Task.Run(async delegate
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (current.Skipped)
                        {
                            Interlocked.Increment(ref _skipped);
                            _output.FileSkipped(current.InputPath, current.SkipReason);
                            return;
                        }
                        if (!CanStart())
                        {
                            Interlocked.Increment(ref _skipped);
                            _output.FileSkipped(current.InputPath);
                            return;
                        }
                        await ProcessJobAsync(current, jobs.Count, targetExtension).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ProcessJobAsync(ConversionJob job, int total, string targetExtension)
        {
            _output.FileStart(job.InputPath, job.FinalPath, job.Entry.PluginName, job.Index, total);

            string tempPath;
            try
            {
                tempPath = CreateTempPath(job.FinalPath);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failed);
                _output.FileFailure(
                    job.InputPath,
                    string.Format(CliText.PreparationFailed, ex.Message),
                    null,
                    CliExitCode.ConversionFailed);
                CleanupJob(job, null);
                return;
            }

            lock (_fileStateSync)
            {
                _temporaryFiles.Add(tempPath);
            }

            var request = new ConversionRequest
            {
                PluginId = job.Entry.PluginId,
                InputPath = job.InputPath,
                OutputPath = tempPath,
                InputExtension = job.InputExtension,
                OutputExtension = "." + targetExtension,
                Options = job.Options
            };

            _output.Detail(string.Format(
                CliText.VerboseResolve,
                job.InputPath,
                job.FinalPath,
                job.Entry.PluginName,
                job.Entry.PluginId,
                string.IsNullOrEmpty(job.PresetName) ? CliText.NoPreset : job.PresetName,
                DescribeOptions(job.Options)));

            var progress = new InlineProgress(delegate(ConversionProgress value)
            {
                _output.Progress(job.InputPath, job.Index, total, value);
            });

            ConversionResult result;
            try
            {
                result = await _host.Engine.ConvertAsync(request, progress, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _skipped);
                _output.FileSkipped(job.InputPath);
                CleanupJob(job, tempPath);
                return;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failed);
                _output.FileFailure(
                    job.InputPath,
                    string.Format(CliText.ExecutionException, ex.Message),
                    ex.ToString(),
                    CliExitCode.ConversionFailed);
                CleanupJob(job, tempPath);
                if (_options.StopOnError) _stopRequested = true;
                return;
            }

            if (result.Success)
            {
                try
                {
                    FinalizeOutput(tempPath, result.OutputPath, job.FinalPath);
                    RemoveTemp(tempPath);
                    RemovePlaceholder(job.FinalPath);
                    var outputBytes = GetFileLength(job.FinalPath);
                    Interlocked.Add(ref _outputBytes, outputBytes);
                    Interlocked.Increment(ref _succeeded);
                    _output.FileSuccess(job.InputPath, job.FinalPath, result.Duration.TotalSeconds, outputBytes);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failed);
                    _output.FileFailure(
                        job.InputPath,
                        string.Format(CliText.FinalizeFailed, ex.Message),
                        ex.ToString(),
                        CliExitCode.ConversionFailed);
                    CleanupJob(job, tempPath);
                    if (_options.StopOnError) _stopRequested = true;
                }
                return;
            }

            if (_cts.IsCancellationRequested)
            {
                Interlocked.Increment(ref _skipped);
                _output.FileSkipped(job.InputPath);
                CleanupJob(job, tempPath);
                return;
            }

            var raw = string.IsNullOrEmpty(result.FullError) ? result.ErrorMessage : result.FullError;
            var analysis = ErrorParser.Parse(raw, result.ExitCode, "." + targetExtension);
            var message = CliText.DescribeErrorKind(analysis.Kind);
            Interlocked.Increment(ref _failed);
            _output.FileFailure(job.InputPath, message, Truncate(raw, 4000), CliExitCode.ConversionFailed);
            CleanupJob(job, tempPath);
            if (_options.StopOnError) _stopRequested = true;
        }

        private bool CanStart()
        {
            return !_cts.IsCancellationRequested && !_stopRequested;
        }

        private void CleanupJob(ConversionJob job, string tempPath)
        {
            if (!string.IsNullOrEmpty(tempPath))
            {
                TryDelete(tempPath);
                RemoveTemp(tempPath);
            }
            if (job.PlaceholderReserved)
            {
                TryDelete(job.FinalPath);
                RemovePlaceholder(job.FinalPath);
            }
        }

        private void CleanupLeftovers()
        {
            string[] temporary;
            string[] placeholders;
            lock (_fileStateSync)
            {
                temporary = _temporaryFiles.ToArray();
                placeholders = _placeholders.ToArray();
                _temporaryFiles.Clear();
                _placeholders.Clear();
            }

            foreach (var file in temporary)
            {
                TryDelete(file);
            }
            foreach (var file in placeholders)
            {
                TryDelete(file);
            }
        }

        private void RemoveTemp(string path)
        {
            lock (_fileStateSync)
            {
                _temporaryFiles.Remove(path);
            }
        }

        private void RemovePlaceholder(string path)
        {
            lock (_fileStateSync)
            {
                _placeholders.Remove(path);
            }
        }

        private static void FinalizeOutput(string tempPath, string producedPath, string finalPath)
        {
            var source = string.IsNullOrEmpty(producedPath) ? tempPath : producedPath;
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("转换未产生输出文件。", source);
            }
            if (string.Equals(source, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(finalPath))
            {
                try
                {
                    File.Replace(source, finalPath, null);
                }
                catch
                {
                    File.Delete(finalPath);
                    File.Move(source, finalPath);
                }
            }
            else
            {
                File.Move(source, finalPath);
            }

            if (!string.Equals(source, tempPath, StringComparison.OrdinalIgnoreCase) && File.Exists(tempPath))
            {
                TryDelete(tempPath);
            }
        }

        private static string CreateTempPath(string finalPath)
        {
            var directory = Path.GetDirectoryName(finalPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new IOException("无法确定输出目录。");
            }

            var extension = Path.GetExtension(finalPath);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var name = ".uc-" + Guid.NewGuid().ToString("N").Substring(0, 12) + extension;
                var candidate = Path.Combine(directory, name);
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException("无法生成临时文件路径。");
        }

        private bool TryComputeTarget(
            string inputPath,
            string targetExtension,
            out string desiredPath,
            out string error)
        {
            desiredPath = null;
            error = null;

            try
            {
                if (!string.IsNullOrEmpty(_options.Output))
                {
                    var explicitPath = Path.GetFullPath(_options.Output);
                    var explicitExtension = NormalizeExtension(Path.GetExtension(explicitPath));
                    if (string.IsNullOrEmpty(explicitExtension))
                    {
                        error = string.Format(CliText.OutputNoExtension, explicitPath);
                        return false;
                    }
                    if (!string.Equals(explicitExtension, targetExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        error = string.Format(
                            CliText.OutputExtensionMismatch,
                            "." + explicitExtension,
                            "." + targetExtension);
                        return false;
                    }
                    if (Directory.Exists(explicitPath))
                    {
                        error = string.Format(CliText.OutputIsDirectory, explicitPath);
                        return false;
                    }
                    desiredPath = explicitPath;
                    return true;
                }

                var directory = !string.IsNullOrEmpty(_options.OutputDir)
                    ? Path.GetFullPath(_options.OutputDir)
                    : Path.GetDirectoryName(inputPath);

                if (string.IsNullOrEmpty(directory))
                {
                    error = string.Format(CliText.InvalidPath, inputPath, "无法确定输入目录。");
                    return false;
                }

                desiredPath = Path.Combine(
                    directory,
                    Path.GetFileNameWithoutExtension(inputPath) + "." + targetExtension);
                if (Directory.Exists(desiredPath))
                {
                    error = string.Format(CliText.OutputIsDirectory, desiredPath);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = string.Format(CliText.InvalidPath, _options.Output ?? _options.OutputDir ?? inputPath, ex.Message);
                return false;
            }
        }

        private bool TryResolvePreset(
            ConversionEntry entry,
            string requested,
            out string presetName,
            out string error)
        {
            presetName = null;
            error = null;
            if (string.IsNullOrEmpty(requested)) return true;

            var presets = entry.Presets ?? new List<ConversionPreset>();
            var exact = presets.FirstOrDefault(p => p.Name == requested);
            if (exact != null)
            {
                presetName = exact.Name;
                return true;
            }

            var caseInsensitive = presets.FirstOrDefault(
                p => string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitive != null)
            {
                presetName = caseInsensitive.Name;
                return true;
            }

            error = string.Format(CliText.PresetNotFound, requested, DescribePresets(entry));
            return false;
        }

        private bool TryBuildOverrides(
            ConversionEntry entry,
            out IDictionary<string, string> overrides,
            out string error)
        {
            overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = null;
            if (_options.Overrides.Count == 0) return true;

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry.Options != null)
            {
                foreach (var option in entry.Options)
                {
                    if (option != null && !string.IsNullOrEmpty(option.Key)) allowed.Add(option.Key);
                }
            }

            foreach (var item in _options.Overrides)
            {
                if (!allowed.Contains(item.Key))
                {
                    error = string.Format(CliText.OptionUnsupported, item.Key, DescribeOptions(entry));
                    return false;
                }
                overrides[item.Key] = item.Value;
            }
            return true;
        }

        private string GetRememberedPlugin(string inputExtension, string targetExtension)
        {
            if (_config == null || _config.Settings == null) return null;
            var key = FormatSelector.GetChoiceKey(inputExtension, targetExtension);
            string value;
            return _config.Settings.TryGetValue(key, out value) ? value : null;
        }

        private int RunFormats()
        {
            var filter = NormalizeExtension(_options.InputExtension);
            var pluginFilter = (_options.PluginId ?? string.Empty).Trim();
            var registryEntries = _host.Registry.Entries.ToList();

            if (!string.IsNullOrEmpty(pluginFilter))
            {
                var filtered = registryEntries
                    .Where(e => string.Equals(e.PluginId, pluginFilter, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.PluginName, pluginFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (filtered.Count == 0)
                {
                    _output.Error(string.Format(
                        CliText.FormatsPluginNotFound,
                        pluginFilter,
                        DescribePluginCandidates(registryEntries)));
                    return CliExitCode.UsageError;
                }
                registryEntries = filtered;
            }

            // 无 --input 时按插件汇总，避免每个输入扩展名重复展开全部目标格式
            if (string.IsNullOrEmpty(filter))
            {
                return WriteFormatSummary(registryEntries);
            }

            var entries = registryEntries
                .Where(e => string.Equals(e.InputExtension, filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return WriteFormatDetails(entries);
        }

        private int WriteFormatSummary(IList<ConversionEntry> entries)
        {
            var groups = entries
                .GroupBy(e => e.PluginId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                if (_options.Json) WriteJsonArray(new JArray());
                else _output.Info(CliText.NoFormats);
                return CliExitCode.Success;
            }

            if (_options.Json)
            {
                var array = new JArray();
                foreach (var group in groups)
                {
                    var item = new JObject();
                    item["plugin"] = group.First().PluginName;
                    item["pluginId"] = group.Key;
                    item["capabilities"] = group.Count();
                    item["inputCount"] = group
                        .Select(e => e.InputExtension)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    item["outputCount"] = group
                        .Select(e => e.OutputExtension)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    item["available"] = group.All(e => e.IsAvailable);
                    array.Add(item);
                }
                WriteJsonArray(array);
                return CliExitCode.Success;
            }

            foreach (var group in groups)
            {
                Console.Out.WriteLine(string.Format(
                    CultureInfo.CurrentCulture,
                    CliText.FormatsSummaryLine,
                    group.First().PluginName,
                    group.Key,
                    group.Count(),
                    group.Select(e => e.InputExtension).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Select(e => e.OutputExtension).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
            }
            Console.Out.WriteLine(string.Format(
                CultureInfo.CurrentCulture,
                CliText.FormatsSummaryHint,
                groups.Count,
                entries.Count));
            return CliExitCode.Success;
        }

        private int WriteFormatDetails(IList<ConversionEntry> entries)
        {
            var groups = entries
                .GroupBy(e => e.InputExtension, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                if (_options.Json) WriteJsonArray(new JArray());
                else _output.Info(CliText.NoFormats);
                return CliExitCode.Success;
            }

            if (_options.Json)
            {
                var array = new JArray();
                foreach (var group in groups)
                {
                    var item = new JObject();
                    item["input"] = "." + group.Key;
                    item["displayName"] = group.First().InputDisplayName;
                    var outputs = new JArray();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in group)
                    {
                        var key = entry.OutputExtension + "|" + entry.PluginId;
                        if (!seen.Add(key)) continue;
                        var output = new JObject();
                        output["extension"] = "." + entry.OutputExtension;
                        output["displayName"] = entry.OutputDisplayName;
                        output["plugin"] = entry.PluginName;
                        output["pluginId"] = entry.PluginId;
                        output["available"] = entry.IsAvailable;
                        outputs.Add(output);
                    }
                    item["outputs"] = outputs;
                    array.Add(item);
                }
                WriteJsonArray(array);
                return CliExitCode.Success;
            }

            const int maxDetailLines = 200;
            var totalLines = 0;
            foreach (var group in groups)
            {
                totalLines++;
                totalLines += CountDistinctOutputs(group);
            }

            var written = 0;
            var truncated = totalLines > maxDetailLines;
            foreach (var group in groups)
            {
                if (written >= maxDetailLines) break;
                Console.Out.WriteLine("." + group.Key + "：");
                written++;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in group)
                {
                    if (written >= maxDetailLines) break;
                    var key = entry.OutputExtension + "|" + entry.PluginId;
                    if (!seen.Add(key)) continue;
                    var line = "  ." + entry.OutputExtension + "  " + entry.PluginName + "（" + entry.PluginId + "）";
                    if (!entry.IsAvailable) line += "（工具不可用）";
                    Console.Out.WriteLine(line);
                    written++;
                }
            }

            if (truncated)
            {
                Console.Out.WriteLine(string.Format(
                    CultureInfo.CurrentCulture,
                    CliText.FormatsTruncated,
                    totalLines,
                    maxDetailLines));
            }
            return CliExitCode.Success;
        }

        private static int CountDistinctOutputs(IEnumerable<ConversionEntry> entries)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                seen.Add(entry.OutputExtension + "|" + entry.PluginId);
            }
            return seen.Count;
        }

        private static string DescribePluginCandidates(IEnumerable<ConversionEntry> entries)
        {
            var ids = entries
                .Select(e => e.PluginId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return ids.Length == 0 ? "（无）" : string.Join("、", ids);
        }

        private int RunPlugins()
        {
            if (_options.Json)
            {
                var array = new JArray();
                foreach (var plugin in _host.Plugins)
                {
                    var item = new JObject();
                    item["id"] = plugin.Id;
                    item["name"] = plugin.Name;
                    item["version"] = plugin.Version;
                    item["available"] = SafeIsAvailable(plugin);
                    item["untested"] = SafeIsUntested(plugin);
                    item["capabilities"] = SafeCapabilityCount(plugin);
                    array.Add(item);
                }
                WriteJsonArray(array);
                return CliExitCode.Success;
            }

            if (_host.Plugins.Count == 0)
            {
                _output.Info(CliText.NoPlugins);
                return CliExitCode.Success;
            }

            foreach (var plugin in _host.Plugins)
            {
                var marker = SafeIsAvailable(plugin) ? CliText.PluginAvailable : CliText.PluginUnavailableMark;
                if (SafeIsUntested(plugin)) marker += CliText.PluginUntested;
                Console.Out.WriteLine(string.Format(
                    CultureInfo.CurrentCulture,
                    CliText.PluginLine,
                    plugin.Name,
                    plugin.Id,
                    plugin.Version,
                    marker));
                Console.Out.WriteLine("  " + string.Format(CliText.CapabilityCount, SafeCapabilityCount(plugin)));
            }
            return CliExitCode.Success;
        }

        private void WriteJsonArray(JArray array)
        {
            var json = JsonConvert.SerializeObject(array, Formatting.Indented, new JsonSerializerSettings
            {
                // 保留中文原文，避免路径与消息被序列化器转为 Unicode 转义
                StringEscapeHandling = StringEscapeHandling.Default
            });
            Console.Out.WriteLine(json);
        }

        private int ReportProblems(IList<CliProblem> problems)
        {
            var code = int.MaxValue;
            foreach (var problem in problems)
            {
                _output.ErrorEvent(null, problem.Code, problem.Message, null);
                if (problem.Code < code) code = problem.Code;
            }
            return code == int.MaxValue ? CliExitCode.UsageError : code;
        }

        private static bool SafeIsAvailable(IConverterPlugin plugin)
        {
            try { return plugin.IsToolAvailable(); }
            catch { return false; }
        }

        private static bool SafeIsUntested(IConverterPlugin plugin)
        {
            try { return plugin.IsUntested; }
            catch { return false; }
        }

        private static int SafeCapabilityCount(IConverterPlugin plugin)
        {
            try
            {
                var capabilities = plugin.GetCapabilities();
                return capabilities == null ? 0 : capabilities.Count;
            }
            catch
            {
                return 0;
            }
        }

        private static string DescribePresets(ConversionEntry entry)
        {
            if (entry.Presets == null || entry.Presets.Count == 0) return "（无）";
            return string.Join("、", entry.Presets
                .Where(p => p != null && !string.IsNullOrEmpty(p.Name))
                .Select(p => p.Name)
                .ToArray());
        }

        private static string DescribeOptions(ConversionEntry entry)
        {
            if (entry.Options == null || entry.Options.Count == 0) return "（无）";
            return string.Join("、", entry.Options
                .Where(o => o != null && !string.IsNullOrEmpty(o.Key))
                .Select(o => o.Key)
                .ToArray());
        }

        private static string DescribeOptions(IDictionary<string, string> options)
        {
            if (options == null || options.Count == 0) return "（无）";
            return string.Join("，", options
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .Select(kv => kv.Key + "=" + kv.Value)
                .ToArray());
        }

        private static string FindAvailablePath(string desiredPath, string inputPath)
        {
            if (!File.Exists(desiredPath)
                && !string.Equals(desiredPath, inputPath, StringComparison.OrdinalIgnoreCase))
            {
                return desiredPath;
            }

            var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(desiredPath);
            var extension = Path.GetExtension(desiredPath);
            for (var i = 1; i < 1000; i++)
            {
                var candidate = Path.Combine(directory, name + " (" + i.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                if (!File.Exists(candidate)
                    && !string.Equals(candidate, inputPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            return desiredPath;
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return null;
            var value = extension.Trim().TrimStart('.');
            return value.Length == 0 ? null : value.ToLowerInvariant();
        }

        private static string NormalizeInputPath(string value)
        {
            if (value == null) return null;
            var trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }
            return trimmed;
        }

        private static long GetFileLength(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path)
                    ? new FileInfo(path).Length
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string Truncate(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= length ? value : value.Substring(0, length);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        private static string GetVersion()
        {
            var assembly = typeof(CliRunner).Assembly;
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var version = attribute != null ? attribute.InformationalVersion : null;
            if (!string.IsNullOrEmpty(version))
            {
                var plus = version.IndexOf('+');
                if (plus > 0) version = version.Substring(0, plus);
                return version;
            }
            return assembly.GetName().Version.ToString();
        }

        private sealed class ConversionJob
        {
            public string InputPath { get; set; }
            public string InputExtension { get; set; }
            public string DesiredPath { get; set; }
            public string FinalPath { get; set; }
            public ConversionEntry Entry { get; set; }
            public string PresetName { get; set; }
            public IDictionary<string, string> Options { get; set; }
            public int Index { get; set; }
            public bool PlaceholderReserved { get; set; }
            public bool Skipped { get; set; }
            public string SkipReason { get; set; }
        }

        private sealed class CliProblem
        {
            public int Code { get; private set; }
            public string Message { get; private set; }

            public CliProblem(int code, string message)
            {
                Code = code;
                Message = message;
            }
        }

        private sealed class InlineProgress : IProgress<ConversionProgress>
        {
            private readonly Action<ConversionProgress> _report;

            public InlineProgress(Action<ConversionProgress> report)
            {
                _report = report;
            }

            public void Report(ConversionProgress value)
            {
                try
                {
                    if (_report != null) _report(value);
                }
                catch
                {
                    // 进度输出失败不影响转换
                }
            }
        }
    }
}
