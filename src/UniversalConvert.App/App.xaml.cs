using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ModernWpf;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    public partial class App : Application
    {
        private CoreHost _host;
        private SettingsManager _settingsManager;
        private EventWaitHandle _exitSignal;

        /// <summary>退出时清空会话级预览渲染缓存（崩溃/强杀时会漏掉，由下次启动清理兜底）。</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            PreviewRenderCache.Clear();
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 应用主题：默认跟随系统（ThemeManager 默认 null=跟随系统），稍后按设置覆盖
            ThemeManager.Current.ApplicationTheme = null;

            // 尽早挂 UI 线程崩溃捕获，保证后续初始化异常也能被报告
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var parsed = CommandLineContract.Parse(e.Args);

            // 帮助、版本与语法错误不进入完整启动流程
            if (parsed.IsHelp)
            {
                ShowCommandLineHelp();
                Shutdown(ExitCodes.Success);
                return;
            }
            if (parsed.IsVersion)
            {
                ShowVersion();
                Shutdown(ExitCodes.Success);
                return;
            }
            if (parsed.UnknownOptions.Count > 0 || parsed.MissingOptionValues.Count > 0)
            {
                ShowCommandLineError(BuildParseError(parsed));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            // 看护进程拉起的「卡死报告」模式：只显示报告窗口，不走完整启动流程
            if (parsed.IsReportMode)
            {
                RunReportMode(parsed);
                return;
            }

            CleanupStaleInstallerPackages();

            // 预览渲染缓存是会话级的：启动时清空，兼作上次崩溃/强杀残留的兜底
            PreviewRenderCache.Clear();

            var config = new ConfigStore().Load();
            config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // 主题色（读设置，默认 Fluent 蓝）
            ApplyAccentColor(GetConfigValue(config, "themeAccent"));

            // 明暗主题（默认跟随系统）
            ApplyAppTheme(GetConfigValue(config, "appTheme"));

            // 日志：归档上一次运行日志，再配置本次日志（级别读设置，默认 Info）
            ArchivePreviousLog();
            Log.Configure(AppLogPath, Log.ParseLevel(GetConfigValue(config, "logLevel")));
            Log.Info("=== UniversalConvert 启动，版本 " + (AppVersion.Current?.ToString() ?? "?") + " ===");

            // 应用暂存的扩展更新（重启后生效的在线更新），须在插件加载前执行
            var updatesApplied = ExtensionCenter.ApplyPendingUpdates();
            Log.Info($"启动：暂存扩展更新应用结果 = {updatesApplied}");
            // 删除标记为待卸载的扩展（重启后生效的卸载）
            var uninstallsApplied = ExtensionCenter.ApplyPendingUninstalls();
            Log.Info($"启动：暂存扩展卸载应用结果 = {uninstallsApplied}");

            // 旧版按显示名安装的扩展目录迁移为稳定 Id 目录；须在插件加载前执行
            var migrated = ExtensionCenter.MigrateLegacyInstalls();
            Log.Info($"启动：扩展目录迁移结果 = {migrated}");

            _host = new CoreHost(config, config.ResolvePluginsDirectory(), msg => Log.Debug(msg), IsDarkTheme);
            _settingsManager = new SettingsManager(config, _host.Plugins);

            // 崩溃报告（转储开关/等级读设置，默认开启 + Normal 等级）
            CrashReporter.Install(_host, IsDumpEnabled(config),
                CrashReporter.ParseDumpType(GetConfigValue(config, "crashDumpLevel")));

            ApplyLanguage(_settingsManager.Get("language"));

            StartHeartbeatAndWatchdog();

            if (parsed.IsConvertMode)
            {
                RunConversion(parsed);
            }
            else if (parsed.IsCustomizeMode)
            {
                RunCustomize(parsed);
            }
            else if (parsed.IsOpenMode)
            {
                RunOpenMode(parsed);
            }
            else
            {
                ShowMainWindow(parsed.ExtraFiles);
            }
        }

        private void RunConversion(CommandLineContract.ParsedArguments parsed)
        {
            var files = parsed.ExtraFiles ?? new string[0];
            if (files.Length == 0)
            {
                ShowCommandLineError(Strings.Get("CliNoInput"));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            var missing = files.Where(f => !File.Exists(f)).ToList();
            if (missing.Count > 0)
            {
                ShowInputNotFound(missing);
                Shutdown(ExitCodes.InputNotFound);
                return;
            }

            if (string.IsNullOrEmpty(parsed.OutputExtension))
            {
                ShowCommandLineError(Strings.Get("CliMissingTargetFormat"));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            if (files.Length > 1 && !string.IsNullOrEmpty(parsed.OutputPath))
            {
                ShowCommandLineError(Strings.Get("CliOutputOnlySingle"));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            if (!string.IsNullOrEmpty(parsed.OutputPath) && !string.IsNullOrEmpty(parsed.OutputDir))
            {
                ShowCommandLineError(Strings.Get("CliOutputConflict"));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            if (!string.IsNullOrEmpty(parsed.OutputDir))
            {
                try
                {
                    Directory.CreateDirectory(parsed.OutputDir);
                }
                catch (Exception ex)
                {
                    ShowCommandLineError(string.Format(
                        Strings.Get("CliOutputDirCreateFailed"),
                        parsed.OutputDir,
                        ex.Message));
                    Shutdown(ExitCodes.UsageError);
                    return;
                }
            }

            if (files.Length == 1)
            {
                var inputPath = files[0];
                var inputExt = Path.GetExtension(inputPath);
                var selection = FormatSelector.Resolve(
                    _host,
                    inputExt,
                    parsed.OutputExtension,
                    parsed.PluginId,
                    _settingsManager.Get(FormatSelector.GetChoiceKey(inputExt, parsed.OutputExtension)));

                if (selection.PreferredNotFound || selection.IsEmpty)
                {
                    ShowResolutionError(inputPath, inputExt, parsed);
                    Shutdown(ExitCodes.UnsupportedFormat);
                    return;
                }

                var entry = FormatResolver.Resolve(
                    _host, _settingsManager, inputExt, parsed.OutputExtension, parsed.PluginId);
                if (entry == null)
                {
                    ShowResolutionError(inputPath, inputExt, parsed);
                    Shutdown(ExitCodes.UnsupportedFormat);
                    return;
                }

                if (!entry.IsAvailable)
                {
                    ShowCommandLineError(string.Format(Strings.Get("CliPluginUnavailable"), entry.PluginName));
                    Shutdown(ExitCodes.ToolUnavailable);
                    return;
                }

                if (!ValidatePreset(entry, parsed.PresetName))
                {
                    Shutdown(ExitCodes.UsageError);
                    return;
                }

                var outputPath = parsed.OutputPath;
                if (string.IsNullOrEmpty(outputPath) && !string.IsNullOrEmpty(parsed.OutputDir))
                {
                    outputPath = Path.Combine(
                        parsed.OutputDir,
                        Path.GetFileNameWithoutExtension(inputPath) + "." + parsed.OutputExtension);
                }

                if (!string.IsNullOrEmpty(outputPath))
                {
                    var outputExtension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
                    if (!string.Equals(outputExtension, parsed.OutputExtension, StringComparison.Ordinal))
                    {
                        ShowCommandLineError(string.Format(
                            Strings.Get("CliOutputExtensionMismatch"),
                            string.IsNullOrEmpty(outputExtension) ? "（无）" : "." + outputExtension,
                            "." + parsed.OutputExtension));
                        Shutdown(ExitCodes.UsageError);
                        return;
                    }

                    try
                    {
                        outputPath = Path.GetFullPath(outputPath);
                        var outputDirectory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowCommandLineError(string.Format(
                            Strings.Get("CliOutputDirCreateFailed"),
                            outputPath,
                            ex.Message));
                        Shutdown(ExitCodes.UsageError);
                        return;
                    }
                }

                var options = PresetMerger.Merge(entry.Options, entry.Presets, parsed.PresetName, null);
                var request = new ConversionRequest
                {
                    PluginId = entry.PluginId,
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    InputExtension = inputExt,
                    OutputExtension = "." + parsed.OutputExtension,
                    Options = options
                };

                var window = new ConvertWindow(_host.Engine, request);
                MainWindow = window;
                window.Show();
                return;
            }

            var problems = new List<string>();
            var problemCode = int.MaxValue;
            foreach (var file in files)
            {
                var inputExt = Path.GetExtension(file);
                var remembered = _settingsManager.Get(FormatSelector.GetChoiceKey(inputExt, parsed.OutputExtension));
                var selection = FormatSelector.Resolve(
                    _host, inputExt, parsed.OutputExtension, parsed.PluginId, remembered);

                if (selection.PreferredNotFound)
                {
                    problems.Add(string.Format(
                        Strings.Get("CliPluginNotFound"),
                        parsed.PluginId,
                        selection.DescribeCandidates()));
                    problemCode = Math.Min(problemCode, ExitCodes.UnsupportedFormat);
                    continue;
                }
                if (selection.IsEmpty)
                {
                    problems.Add(string.Format(
                        Strings.Get("CliUnsupportedFormat"), file, parsed.OutputExtension));
                    problemCode = Math.Min(problemCode, ExitCodes.UnsupportedFormat);
                    continue;
                }
                if (selection.Selected == null)
                {
                    // 多个候选：批量窗口会按既有流程询问用户
                    continue;
                }
                if (!selection.Selected.IsAvailable)
                {
                    problems.Add(string.Format(
                        Strings.Get("CliPluginUnavailable"), selection.Selected.PluginName));
                    problemCode = Math.Min(problemCode, ExitCodes.ToolUnavailable);
                    continue;
                }
                if (!string.IsNullOrEmpty(parsed.PresetName)
                    && !PresetExists(selection.Selected, parsed.PresetName))
                {
                    problems.Add(string.Format(
                        Strings.Get("CliPresetNotFound"),
                        parsed.PresetName,
                        DescribePresets(selection.Selected)));
                    problemCode = Math.Min(problemCode, ExitCodes.UsageError);
                }
            }

            if (problems.Count > 0)
            {
                ShowCommandLineErrors(problems);
                Shutdown(problemCode == int.MaxValue ? ExitCodes.UnsupportedFormat : problemCode);
                return;
            }

            var workerThreads = ResolveWorkerThreads(_settingsManager.Get("workerThreads"));
            var batchWindow = new BatchConvertWindow(
                _host, _settingsManager, files, parsed.OutputExtension, workerThreads,
                perFileOptions: null, outputDir: parsed.OutputDir, presetName: parsed.PresetName,
                preferredPluginId: parsed.PluginId);
            MainWindow = batchWindow;
            batchWindow.Show();
        }

        private void RunCustomize(CommandLineContract.ParsedArguments parsed)
        {
            var inputPath = parsed.InputPath;
            if (string.IsNullOrEmpty(inputPath))
            {
                ShowCommandLineError(Strings.Get("CliCustomizeNoInput"));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            if (!File.Exists(inputPath))
            {
                ShowInputNotFound(new[] { inputPath });
                Shutdown(ExitCodes.InputNotFound);
                return;
            }

            if (string.IsNullOrEmpty(parsed.OutputExtension))
            {
                ShowCommandLineError(Strings.Get("CliMissingTargetFormat"));
                Shutdown(ExitCodes.UsageError);
                return;
            }

            var inputExt = Path.GetExtension(inputPath);
            var selection = FormatSelector.Resolve(
                _host,
                inputExt,
                parsed.OutputExtension,
                parsed.PluginId,
                _settingsManager.Get(FormatSelector.GetChoiceKey(inputExt, parsed.OutputExtension)));

            if (selection.PreferredNotFound || selection.IsEmpty)
            {
                ShowResolutionError(inputPath, inputExt, parsed);
                Shutdown(ExitCodes.UnsupportedFormat);
                return;
            }

            var entry = FormatResolver.Resolve(
                _host, _settingsManager, inputExt, parsed.OutputExtension, parsed.PluginId);
            if (entry == null)
            {
                ShowResolutionError(inputPath, inputExt, parsed);
                Shutdown(ExitCodes.UnsupportedFormat);
                return;
            }

            if (!entry.IsAvailable)
            {
                ShowCommandLineError(string.Format(Strings.Get("CliPluginUnavailable"), entry.PluginName));
                Shutdown(ExitCodes.ToolUnavailable);
                return;
            }

            var window = new CustomizeWindow(_host.Engine, inputPath, entry);
            MainWindow = window;
            window.Show();
        }

        private void RunOpenMode(CommandLineContract.ParsedArguments parsed)
        {
            var files = parsed.ExtraFiles ?? new string[0];
            var missing = files.Where(f => !File.Exists(f)).ToList();
            if (missing.Count > 0)
            {
                ShowInputNotFound(missing);
                Shutdown(ExitCodes.InputNotFound);
                return;
            }
            ShowMainWindow(files);
        }

        private void ShowMainWindow(string[] files)
        {
            var main = new MainWindow(_host, _settingsManager, files ?? new string[0]);
            MainWindow = main;
            main.Show();

            // 空闲时预热媒体管线：把「第一次点预览」的冷启动成本提前到应用启动后
            SchedulePreviewWarmup();

            // 扩展更新/卸载在重启时未能应用（文件仍被占用，如资源管理器锁着右键菜单加载的插件 DLL）：
            // 明确提示，避免用户以为没有更新
            if (ExtensionCenter.HasPendingUpdates() || ExtensionCenter.HasPendingUninstalls())
            {
                MessageBox.Show(Strings.PendingChangesNotApplied,
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowCommandLineHelp()
        {
            MessageBox.Show(
                Strings.Get("CliUsage"),
                "UniversalConvert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ShowVersion()
        {
            MessageBox.Show(
                string.Format(Strings.Get("CliVersionFormat"), AppVersion.Current?.ToString() ?? string.Empty),
                "UniversalConvert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static string BuildParseError(CommandLineContract.ParsedArguments parsed)
        {
            var parts = new List<string>();
            if (parsed.MissingOptionValues.Count > 0)
            {
                parts.Add(string.Format(
                    Strings.Get("CliMissingOptionValue"),
                    string.Join("、", parsed.MissingOptionValues)));
            }
            if (parsed.UnknownOptions.Count > 0)
            {
                parts.Add(string.Format(
                    Strings.Get("CliUnknownOption"),
                    string.Join("、", parsed.UnknownOptions)));
            }
            return string.Join(Environment.NewLine, parts);
        }

        private void ShowResolutionError(
            string inputPath,
            string inputExt,
            CommandLineContract.ParsedArguments parsed)
        {
            var remembered = _settingsManager.Get(FormatSelector.GetChoiceKey(inputExt, parsed.OutputExtension));
            var selection = FormatSelector.Resolve(
                _host, inputExt, parsed.OutputExtension, parsed.PluginId, remembered);

            string message;
            if (selection.PreferredNotFound)
            {
                message = string.Format(
                    Strings.Get("CliPluginNotFound"),
                    parsed.PluginId,
                    selection.DescribeCandidates());
            }
            else if (selection.IsEmpty)
            {
                message = string.Format(
                    Strings.Get("CliUnsupportedFormat"),
                    inputPath,
                    parsed.OutputExtension);
            }
            else if (selection.IsAmbiguous)
            {
                message = string.Format(
                    Strings.Get("CliPluginAmbiguous"),
                    selection.DescribeCandidates());
            }
            else
            {
                message = string.Format(
                    Strings.Get("CliUnsupportedFormat"),
                    inputPath,
                    parsed.OutputExtension);
            }
            ShowCommandLineError(message);
        }

        private static void ShowInputNotFound(IEnumerable<string> files)
        {
            ShowCommandLineError(string.Join(
                Environment.NewLine,
                files.Select(f => string.Format(Strings.Get("CliInputNotFound"), f))));
        }

        private static void ShowCommandLineErrors(IEnumerable<string> messages)
        {
            ShowCommandLineError(string.Join(Environment.NewLine, messages));
        }

        private static void ShowCommandLineError(string message)
        {
            MessageBox.Show(
                message,
                "UniversalConvert",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static bool ValidatePreset(ConversionEntry entry, string presetName)
        {
            if (string.IsNullOrEmpty(presetName) || PresetExists(entry, presetName)) return true;
            ShowCommandLineError(string.Format(
                Strings.Get("CliPresetNotFound"),
                presetName,
                DescribePresets(entry)));
            return false;
        }

        private static bool PresetExists(ConversionEntry entry, string presetName)
        {
            if (entry == null || entry.Presets == null || string.IsNullOrEmpty(presetName)) return false;
            return entry.Presets.Any(p => p != null && p.Name == presetName);
        }

        private static string DescribePresets(ConversionEntry entry)
        {
            if (entry == null || entry.Presets == null || entry.Presets.Count == 0) return "（无）";
            return string.Join("、", entry.Presets
                .Where(p => p != null && !string.IsNullOrEmpty(p.Name))
                .Select(p => p.Name)
                .ToArray());
        }

        private static class ExitCodes
        {
            public const int Success = 0;
            public const int UsageError = 2;
            public const int UnsupportedFormat = 3;
            public const int InputNotFound = 4;
            public const int ToolUnavailable = 5;
        }

        private static void ApplyLanguage(string language)
        {
            CultureInfo culture = null;

            if (language == "zh") culture = new CultureInfo("zh-CN");
            else if (language == "en") culture = new CultureInfo("en");

            if (culture != null)
            {
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        private static void CleanupStaleInstallerPackages()
        {
            // 清理自动更新遗留在 %TEMP% 的旧安装包（升级装完后安装包不再被主动删除）。
            // 删除失败（如正被某个正在运行的安装器占用）时静默忽略。
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "UniversalConvert-Setup-*.exe"))
                {
                    try { File.Delete(f); }
                    catch { /* 忽略 */ }
                }
            }
            catch { /* 忽略 */ }
        }

        private void RunReportMode(CommandLineContract.ParsedArguments parsed)
        {
            // 报告窗口也遵循用户选择的语言（否则英文设置下仍显示系统语言）
            var config = new ConfigStore().Load();
            ApplyLanguage(GetConfigValue(config, "language"));

            // 报告模式不读主题设置、跟随系统；但须应用主题动态资源（AppBackgroundBrush 等），
            // 否则系统深色下窗口背景仍是默认浅色、文字变白，白字白底看不清
            ApplyAppTheme(null);

            var logsDir = string.IsNullOrEmpty(parsed.ReportDir)
                ? Path.Combine(ConfigStore.ConfigDirectory, "logs")
                : parsed.ReportDir;

            var logText = ReadFileSafe(Path.Combine(logsDir, "app.log"));
            var summary = parsed.ReportKind == "hang" ? Strings.HangReportSummary
                : parsed.ReportKind == "crash" ? Strings.CrashUnexpectedSummary
                : Strings.CrashReportTitle;
            var reportText = parsed.ReportKind == "hang" || parsed.ReportKind == "crash"
                ? BuildHangReportText(logsDir)
                : string.Empty;

            // 看护卡死/终结报告：进程即将结束，不允许「继续运行」
            var window = new CrashReportWindow(summary, reportText, logText, logsDir, allowContinue: false);
            MainWindow = window;
            window.Show();
        }

        private static string BuildHangReportText(string logsDir)
        {
            return Strings.HangReportText + Environment.NewLine +
                   Strings.LogDirectory + ": " + logsDir + Environment.NewLine +
                   Strings.TimeLabel + ": " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string ReadFileSafe(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void StartHeartbeatAndWatchdog()
        {
            try
            {
                var heartbeatPath = Heartbeat.Start();

                var watchdogExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UniversalConvert.Watchdog.exe");
                if (!File.Exists(watchdogExe))
                {
                    Log.Warn("看护进程不存在，跳过启动: " + watchdogExe);
                    return;
                }

                var appPath = System.Reflection.Assembly.GetEntryAssembly().Location;
                var logsDir = Path.Combine(ConfigStore.ConfigDirectory, "logs");
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

                // 正常退出信号：进程退出前 Set，看护进程收到后立即退出（不再靠轮询发现，最多延迟 3 秒）
                try
                {
                    _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "UniversalConvert.Exit." + pid);
                    AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                    {
                        try { if (_exitSignal != null) _exitSignal.Set(); } catch { }
                    };
                    Log.Info("已创建看护退出信号: UniversalConvert.Exit." + pid);
                }
                catch (Exception ex)
                {
                    _exitSignal = null;
                    Log.Warn("创建看护退出信号失败，退回轮询检测: " + ex.Message);
                }

                var args = string.Format(
                    "--pid {0} --heartbeat \"{1}\" --app \"{2}\" --logs \"{3}\"",
                    pid,
                    heartbeatPath,
                    appPath,
                    logsDir);

                var psi = new System.Diagnostics.ProcessStartInfo(watchdogExe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi);
                Log.Info("已启动看护进程");
            }
            catch (Exception ex)
            {
                Log.Error("启动看护进程失败: " + ex.Message);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            CrashReporter.HandleException(e.Exception);
            e.Handled = true; // 已弹窗报告，阻止默认终止
        }

        /// <summary>归档上一次运行的日志：把 app.log 压成 app-时间戳.zip 后删除原文件。</summary>
        private static void ArchivePreviousLog()
        {
            try
            {
                if (!File.Exists(AppLogPath)) return;
                var dir = Path.GetDirectoryName(AppLogPath) ?? string.Empty;
                var zipPath = Path.Combine(dir, "app-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(AppLogPath, "app.log", CompressionLevel.Optimal);
                }
                File.Delete(AppLogPath);
            }
            catch
            {
                // 归档失败不影响启动
            }
        }

        private static string AppLogPath => Path.Combine(ConfigStore.ConfigDirectory, "logs", "app.log");

        private static string GetConfigValue(AppConfig config, string key)
        {
            string value;
            return config.Settings != null && config.Settings.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>解析转换线程数设置：auto → 逻辑核心数 × 75%（低于 4 核用 1）。</summary>
        private static int ResolveWorkerThreads(string setting)
        {
            if (string.Equals(setting, "auto", StringComparison.OrdinalIgnoreCase))
            {
                int cores = Environment.ProcessorCount;
                if (cores < 4) return 1;
                return Math.Max(1, (int)Math.Round(cores * 0.75, MidpointRounding.AwayFromZero));
            }
            int parsed;
            return int.TryParse(setting, out parsed) ? Math.Max(1, parsed) : 2;
        }

        private static bool IsDumpEnabled(AppConfig config)
        {
            var value = GetConfigValue(config, "crashDumpEnabled");
            bool enabled;
            return string.IsNullOrEmpty(value) ? true : (!bool.TryParse(value, out enabled) || enabled);
        }

        /// <summary>应用主题色（十六进制字符串，如 "#0078D4"）；无效则忽略。</summary>
        public static void ApplyAccentColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                ThemeManager.Current.AccentColor = color;
                // 同步更新自定义元素（卡片边框等）绑定的强调色
                Application.Current.Resources["AccentBorderBrush"] = new SolidColorBrush(color);
            }
            catch
            {
                // 忽略无效颜色
            }
        }

        /// <summary>应用明暗主题：auto=跟随系统（null），light=浅色，dark=深色。</summary>
        public static void ApplyAppTheme(string mode)
        {
            bool dark;
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
            {
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                dark = true;
            }
            else if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
            {
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                dark = false;
            }
            else
            {
                ThemeManager.Current.ApplicationTheme = null; // 跟随系统
                // 跟随系统时按当前系统主题判断自定义资源
                dark = ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark;
            }

            ApplyThemeBrushes(dark);
        }

        /// <summary>当前是否为深色主题（跟随系统时反映系统实际深浅）。供扩展上下文（IPluginContext）注入使用。</summary>
        public static bool IsDarkTheme()
        {
            try { return ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark; }
            catch { return false; }
        }

        /// <summary>切换自定义主题资源（硬编码浅色背景在深色下需换成深色，否则白字白底）。</summary>
        private static void ApplyThemeBrushes(bool dark)
        {
            try
            {
                var app = dark ? "#1F1F1F" : "#F3F3F3";
                var card = dark ? "#2D2D2D" : "#FFFFFF";
                var border = dark ? "#3F3F3F" : "#DDDDDD";
                var secondary = dark ? "#AAAAAA" : "#666666";
                var tertiary = dark ? "#888888" : "#999999";
                Application.Current.Resources["AppBackgroundBrush"] =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(app));
                Application.Current.Resources["CardBackgroundBrush"] =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(card));
                Application.Current.Resources["CardBorderBrush"] =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
                Application.Current.Resources["SecondaryTextBrush"] =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(secondary));
                Application.Current.Resources["TertiaryTextBrush"] =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(tertiary));
            }
            catch
            {
                // 忽略资源切换失败
            }
        }

        /// <summary>应用空闲时预热媒体管线（内置音视频预览依赖的 Windows Media Foundation）。</summary>
        private static void SchedulePreviewWarmup()
        {
            try
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    new Action(WarmupMediaFoundation),
                    DispatcherPriority.ApplicationIdle);
            }
            catch
            {
                // 忽略预热调度失败
            }
        }

        /// <summary>预热 MediaPlayer：首次 new + Open 会加载 mfplat.dll 与核心 MF 栈，
        /// 把「第一次点预览」的冷启动成本提前到应用启动后的空闲期（预览窗口秒开）。</summary>
        private static void WarmupMediaFoundation()
        {
            try
            {
                var p = new MediaPlayer();
                try { p.Open(new Uri("file:///__uc_warmup__.wav", UriKind.Absolute)); } catch { }
                p.Close();
            }
            catch
            {
                // 预热失败不影响正常使用（首次预览时仍会自行初始化）
            }
        }
    }
}
