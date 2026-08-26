using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

namespace UniversalConvert.App
{
    public partial class App : Application
    {
        private CoreHost _host;
        private SettingsManager _settingsManager;
        private EventWaitHandle _exitSignal;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 应用主题：浅色（主题色稍后从设置读取）
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;

            // 尽早挂 UI 线程崩溃捕获，保证后续初始化异常也能被报告
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 看护进程拉起的「卡死报告」模式：只显示报告窗口，不走完整启动流程
            var parsed = CommandLineContract.Parse(e.Args);
            if (parsed.IsReportMode)
            {
                RunReportMode(parsed);
                return;
            }

            CleanupStaleInstallerPackages();

            var config = new ConfigStore().Load();
            config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // 主题色（读设置，默认 Fluent 蓝）
            ApplyAccentColor(GetConfigValue(config, "themeAccent"));

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

            _host = new CoreHost(config, config.ResolvePluginsDirectory(), msg => Log.Debug(msg));
            _settingsManager = new SettingsManager(config, _host.Plugins);

            // 崩溃报告（转储开关/等级读设置，默认开启 + Normal 等级）
            CrashReporter.Install(_host, IsDumpEnabled(config),
                CrashReporter.ParseDumpType(GetConfigValue(config, "crashDumpLevel")));

            ApplyLanguage(_settingsManager.Get("language"));

            StartHeartbeatAndWatchdog();

            // convert 模式：输入可以是 --convert 后的单个文件（InputPath）或多文件批量（ExtraFiles）
            var hasConvertInput = !string.IsNullOrEmpty(parsed.InputPath)
                || (parsed.ExtraFiles != null && parsed.ExtraFiles.Length > 0);
            if (parsed.IsConvertMode && hasConvertInput)
            {
                RunHeadlessConversion(parsed);
            }
            else if (parsed.IsCustomizeMode && !string.IsNullOrEmpty(parsed.InputPath))
            {
                RunCustomize(parsed);
            }
            else
            {
                var main = new MainWindow(_host, _settingsManager, parsed.ExtraFiles);
                MainWindow = main;
                main.Show();

                // 扩展更新/卸载在重启时未能应用（文件仍被占用，如资源管理器锁着右键菜单加载的插件 DLL）：
                // 明确提示，避免用户以为没更新
                if (ExtensionCenter.HasPendingUpdates() || ExtensionCenter.HasPendingUninstalls())
                {
                    MessageBox.Show(Strings.PendingChangesNotApplied,
                        "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void RunHeadlessConversion(CommandLineContract.ParsedArguments parsed)
        {
            // convert 模式的输入文件：--convert 后的全部裸参数（支持右键多选批量）
            var files = parsed.ExtraFiles != null && parsed.ExtraFiles.Length > 0
                ? parsed.ExtraFiles
                : (string.IsNullOrEmpty(parsed.InputPath) ? new string[0] : new[] { parsed.InputPath });

            if (files.Length == 0)
            {
                MessageBox.Show(Strings.InputFileMissing, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            if (files.Length > 1)
            {
                // 多文件：打开批量转换窗口（含进度/暂停/取消），输出到输入目录
                var workerThreads = ResolveWorkerThreads(_settingsManager.Get("workerThreads"));
                var batchWindow = new BatchConvertWindow(
                    _host, _settingsManager, files, "." + parsed.OutputExtension, workerThreads,
                    perFileOptions: null, outputDir: null, presetName: parsed.PresetName);
                MainWindow = batchWindow;
                batchWindow.Show();
                return;
            }

            var inputPath = files[0];
            if (!File.Exists(inputPath))
            {
                MessageBox.Show(Strings.InputFileMissing + inputPath, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var inputExt = Path.GetExtension(inputPath);
            var entry = FormatResolver.Resolve(_host, _settingsManager, inputExt, parsed.OutputExtension);
            if (entry == null)
            {
                MessageBox.Show(
                    string.Format(Strings.UnsupportedConversion, inputExt, parsed.OutputExtension),
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            var options = PresetMerger.Merge(entry.Options, entry.Presets, parsed.PresetName, null);

            var request = new ConversionRequest
            {
                PluginId = entry.PluginId,
                InputPath = inputPath,
                OutputPath = parsed.OutputPath,
                InputExtension = inputExt,
                OutputExtension = "." + parsed.OutputExtension,
                Options = options
            };

            var window = new ConvertWindow(_host.Engine, request);
            MainWindow = window;
            window.Show();
        }

        private void RunCustomize(CommandLineContract.ParsedArguments parsed)
        {
            var inputPath = parsed.InputPath;
            if (!File.Exists(inputPath))
            {
                MessageBox.Show(Strings.InputFileMissing + inputPath, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var inputExt = Path.GetExtension(inputPath);
            var entry = FormatResolver.Resolve(_host, _settingsManager, inputExt, parsed.OutputExtension);
            if (entry == null)
            {
                MessageBox.Show(
                    string.Format(Strings.UnsupportedConversion, inputExt, parsed.OutputExtension),
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            var window = new CustomizeWindow(_host.Engine, inputPath, entry);
            MainWindow = window;
            window.Show();
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

            var logsDir = string.IsNullOrEmpty(parsed.ReportDir)
                ? Path.Combine(ConfigStore.ConfigDirectory, "logs")
                : parsed.ReportDir;

            var logText = ReadFileSafe(Path.Combine(logsDir, "app.log"));
            var summary = parsed.ReportKind == "hang" ? Strings.HangReportSummary : Strings.CrashReportTitle;
            var reportText = parsed.ReportKind == "hang"
                ? BuildHangReportText(logsDir)
                : string.Empty;

            var window = new CrashReportWindow(summary, reportText, logText, logsDir);
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
    }
}
