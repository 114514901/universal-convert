using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core.Models;

namespace UniversalConvert.Cli
{
    /// <summary>
    /// 命令行输出。负责普通文本、进度行与行分隔 JSON 事件。
    /// 所有输出均通过同一把锁串行化，允许并行转换时安全调用。
    /// </summary>
    internal sealed class CliOutput
    {
        private readonly object _sync = new object();
        private readonly bool _json;
        private readonly bool _quiet;
        private readonly bool _verbose;
        private readonly bool _terminal;
        private readonly CliProgressMode _progressMode;
        private readonly Dictionary<string, double> _lastPercent =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private bool _wroteProgress;

        public CliOutput(bool json, bool quiet, bool verbose, CliProgressMode progressMode)
        {
            _json = json;
            _quiet = quiet;
            _verbose = verbose;
            _progressMode = progressMode;
            try
            {
                _terminal = !Console.IsOutputRedirected;
            }
            catch
            {
                _terminal = false;
            }
        }

        public bool Json
        {
            get { return _json; }
        }

        public bool Quiet
        {
            get { return _quiet; }
        }

        public bool Verbose
        {
            get { return _verbose; }
        }

        public void Info(string message)
        {
            if (_json || _quiet || string.IsNullOrEmpty(message)) return;
            lock (_sync)
            {
                if (_wroteProgress)
                {
                    Console.Out.WriteLine();
                    _wroteProgress = false;
                }
                Console.Out.WriteLine(message);
            }
        }

        public void Detail(string message)
        {
            if (!_verbose || _json || string.IsNullOrEmpty(message)) return;
            lock (_sync)
            {
                Console.Error.WriteLine(message);
            }
        }

        public void Warn(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_json)
            {
                WriteJson("warning", delegate(JObject obj)
                {
                    obj["message"] = message;
                });
                return;
            }
            lock (_sync)
            {
                Console.Error.WriteLine("警告：" + message);
            }
        }

        public void Error(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_json)
            {
                WriteJson("error", delegate(JObject obj)
                {
                    obj["message"] = message;
                });
                return;
            }
            lock (_sync)
            {
                Console.Error.WriteLine(message);
            }
        }

        public void ErrorEvent(string file, int code, string message, string detail)
        {
            if (_json)
            {
                WriteJson("error", delegate(JObject obj)
                {
                    obj["file"] = file;
                    obj["code"] = code;
                    obj["message"] = message;
                    if (!string.IsNullOrEmpty(detail)) obj["detail"] = detail;
                });
                return;
            }
            lock (_sync)
            {
                Console.Error.WriteLine(message);
            }
        }

        public void Plan(string input, string output, string plugin, string preset, bool overwrite)
        {
            if (_json)
            {
                WriteJson("plan", delegate(JObject obj)
                {
                    obj["file"] = input;
                    obj["output"] = output;
                    obj["plugin"] = plugin;
                    obj["preset"] = preset;
                    obj["overwrite"] = overwrite;
                });
                return;
            }
            if (_quiet) return;

            var line = string.Format(
                CultureInfo.CurrentCulture,
                CliText.PlanLine,
                input,
                output,
                plugin,
                string.IsNullOrEmpty(preset) ? CliText.NoPreset : preset);
            if (overwrite) line += CliText.PlanOverwrite;
            Info(line);
        }

        public void FileStart(string file, string output, string plugin, int index, int total)
        {
            if (_json)
            {
                WriteJson("start", delegate(JObject obj)
                {
                    obj["file"] = file;
                    obj["output"] = output;
                    obj["plugin"] = plugin;
                    obj["index"] = index;
                    obj["total"] = total;
                });
                return;
            }
            if (_quiet) return;
            Info(string.Format(
                CultureInfo.CurrentCulture,
                "开始转换（{0}/{1}）：{2} → {3}",
                index,
                total,
                file,
                output));
        }

        public void Progress(string file, int index, int total, ConversionProgress progress)
        {
            if (progress == null) return;

            var percent = progress.Percentage;
            var message = string.IsNullOrEmpty(progress.Message) ? StageText(progress.Stage) : progress.Message;
            message = message.Replace("\r", " ").Replace("\n", " ");
            if (message.Length > 200) message = message.Substring(0, 200) + "…";

            if (_json)
            {
                if (!ShouldReportJsonProgress(file, percent, progress.Stage)) return;
                WriteJson("progress", delegate(JObject obj)
                {
                    obj["file"] = file;
                    obj["index"] = index;
                    obj["total"] = total;
                    if (percent >= 0) obj["percent"] = Math.Round(percent, 1);
                    obj["stage"] = progress.Stage.ToString();
                    obj["message"] = message;
                });
                return;
            }

            if (_quiet || _progressMode == CliProgressMode.Never) return;

            if (!_terminal && _progressMode != CliProgressMode.Always)
            {
                return;
            }

            if (percent >= 0 && !ShouldReportTextProgress(file, percent))
            {
                return;
            }

            var label = total > 1
                ? string.Format(CultureInfo.CurrentCulture, "[{0}/{1}] {2}", index, total, Path.GetFileName(file))
                : Path.GetFileName(file);

            var line = percent >= 0
                ? string.Format(CultureInfo.CurrentCulture, CliText.ProgressLine, label, percent, message)
                : label + "  " + message;

            lock (_sync)
            {
                if (_terminal)
                {
                    var width = 0;
                    try { width = Console.WindowWidth; } catch { }
                    if (width > 1 && line.Length > width - 1) line = line.Substring(0, width - 1);
                    Console.Out.Write("\r" + line.PadRight(Math.Max(0, width - 1)));
                    _wroteProgress = true;
                }
                else
                {
                    Console.Out.WriteLine(line);
                }
            }
        }

        public void FileSuccess(string file, string output, double seconds)
        {
            if (_json)
            {
                WriteJson("success", delegate(JObject obj)
                {
                    obj["file"] = file;
                    obj["output"] = output;
                    obj["durationMs"] = (long)Math.Round(seconds * 1000.0);
                });
                return;
            }
            EndProgressLine();
            if (_quiet) return;
            Info(string.Format(CultureInfo.CurrentCulture, CliText.SuccessLine, file, output, seconds));
        }

        public void FileFailure(string file, string message, string detail, int code)
        {
            if (_json)
            {
                WriteJson("failure", delegate(JObject obj)
                {
                    obj["file"] = file;
                    obj["code"] = code;
                    obj["message"] = message;
                    if (!string.IsNullOrEmpty(detail)) obj["detail"] = detail;
                });
                return;
            }
            EndProgressLine();
            lock (_sync)
            {
                Console.Error.WriteLine(string.Format(CultureInfo.CurrentCulture, CliText.FailureLine, file, message));
            }
            if (_verbose && !string.IsNullOrEmpty(detail))
            {
                Detail(detail);
            }
        }

        public void FileSkipped(string file)
        {
            if (_json)
            {
                WriteJson("skipped", delegate(JObject obj)
                {
                    obj["file"] = file;
                });
                return;
            }
            if (_quiet) return;
            Info(string.Format(CultureInfo.CurrentCulture, CliText.SkippedLine, file));
        }

        public void Summary(int total, int succeeded, int failed, int skipped, double seconds)
        {
            if (_json)
            {
                WriteJson("summary", delegate(JObject obj)
                {
                    obj["total"] = total;
                    obj["succeeded"] = succeeded;
                    obj["failed"] = failed;
                    obj["skipped"] = skipped;
                    obj["durationMs"] = (long)Math.Round(seconds * 1000.0);
                });
                return;
            }
            EndProgressLine();
            if (_quiet) return;
            Info(string.Format(
                CultureInfo.CurrentCulture,
                CliText.Summary,
                total,
                succeeded,
                failed,
                skipped,
                seconds));
        }

        public void PlanSummary(int count)
        {
            if (_json)
            {
                WriteJson("summary", delegate(JObject obj)
                {
                    obj["dryRun"] = true;
                    obj["planned"] = count;
                });
                return;
            }
            if (_quiet) return;
            Info(string.Format(CultureInfo.CurrentCulture, CliText.DryRunSummary, count));
        }

        public void Cancelled()
        {
            if (_json)
            {
                WriteJson("cancelled", delegate(JObject obj) { });
                return;
            }
            EndProgressLine();
            if (!_quiet)
            {
                lock (_sync)
                {
                    Console.Error.WriteLine(CliText.Cancelled);
                }
            }
        }

        private void EndProgressLine()
        {
            if (_json || _quiet || !_terminal || _progressMode == CliProgressMode.Never) return;
            lock (_sync)
            {
                if (!_wroteProgress) return;
                Console.Out.WriteLine();
                _wroteProgress = false;
            }
        }

        private bool ShouldReportTextProgress(string file, double percent)
        {
            lock (_sync)
            {
                double last;
                if (!_lastPercent.TryGetValue(file, out last))
                {
                    _lastPercent[file] = percent;
                    return true;
                }
                if (percent < last || percent - last >= 2.0 || percent >= 100.0)
                {
                    _lastPercent[file] = percent;
                    return true;
                }
                return false;
            }
        }

        private bool ShouldReportJsonProgress(string file, double percent, ConversionStage stage)
        {
            if (percent < 0) return true;
            lock (_sync)
            {
                double last;
                if (!_lastPercent.TryGetValue(file, out last))
                {
                    _lastPercent[file] = percent;
                    return true;
                }
                if (stage == ConversionStage.Completed || percent >= 100.0 || percent - last >= 2.0)
                {
                    _lastPercent[file] = percent;
                    return true;
                }
                return false;
            }
        }

        private static string StageText(ConversionStage stage)
        {
            switch (stage)
            {
                case ConversionStage.Starting:
                    return CliText.ProgressStageStarting;
                case ConversionStage.Running:
                    return CliText.ProgressStageRunning;
                case ConversionStage.Finalizing:
                    return CliText.ProgressStageFinalizing;
                case ConversionStage.Completed:
                    return CliText.ProgressStageCompleted;
                default:
                    return string.Empty;
            }
        }

        private void WriteJson(string eventName, Action<JObject> fill)
        {
            var obj = new JObject();
            obj["event"] = eventName;
            obj["timestamp"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (fill != null) fill(obj);

            var json = JsonConvert.SerializeObject(obj, Formatting.None, new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
            });

            lock (_sync)
            {
                Console.Out.WriteLine(json);
            }
        }
    }
}
