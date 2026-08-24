using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32.SafeHandles;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.App
{
    /// <summary>
    /// 本地崩溃报告器：捕获未处理异常，搜集软件/系统/插件信息与最近日志，
    /// 写入 crash 日志（保留 5 份）并可选生成内存转储（只留 1 份），最后弹本地对话框展示。
    /// 纯本地，不上传任何信息。
    /// </summary>
    public static class CrashReporter
    {
        private const int KeepCrashLogs = 5;
        private const uint MiniDumpNormal = 0x00000000;
        private const uint MiniDumpWithDataSegments = 0x00000001;
        private const uint MiniDumpWithFullMemory = 0x00000002;
        private const uint MiniDumpWithIndirectlyReferencedMemory = 0x00000040;

        private static readonly object Sync = new object();
        private static CoreHost _host;
        private static bool _dumpEnabled = true;
        private static uint _dumpType = MiniDumpNormal;
        private static DateTime _startTime = DateTime.Now;

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeFileHandle hFile,
            uint dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RTL_OSVERSIONINFOW
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
        }

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW versionInfo);

        /// <summary>获取真实 OS 版本（Environment.OSVersion 在未声明 manifest 时返回假的 6.2）。</summary>
        private static string GetOSVersion()
        {
            try
            {
                var info = new RTL_OSVERSIONINFOW();
                info.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOW));
                if (RtlGetVersion(ref info) == 0)
                {
                    // Win10/Win11 的 Major.Minor 都是 10.0，靠 Build 区分：>=22000 为 Windows 11
                    var name = info.dwBuildNumber >= 22000 ? "Windows 11" : "Windows 10";
                    return $"{name} (Build {info.dwBuildNumber})";
                }
            }
            catch { }
            return Environment.OSVersion.ToString();
        }

        /// <summary>把设置里的转储等级字符串解析成 MiniDump 类型标志。</summary>
        public static uint ParseDumpType(string level)
        {
            if (string.Equals(level, "FullMemory", StringComparison.OrdinalIgnoreCase))
                return MiniDumpNormal | MiniDumpWithFullMemory;
            if (string.Equals(level, "WithDataSegments", StringComparison.OrdinalIgnoreCase))
                return MiniDumpNormal | MiniDumpWithDataSegments | MiniDumpWithIndirectlyReferencedMemory;
            return MiniDumpNormal;
        }

        /// <summary>安装崩溃捕获（App 启动时调用一次）。</summary>
        public static void Install(CoreHost host, bool dumpEnabled, uint dumpType)
        {
            _host = host;
            _dumpEnabled = dumpEnabled;
            _dumpType = dumpType;
            _startTime = DateTime.Now;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleException(e.ExceptionObject as Exception);
        }

        /// <summary>处理一次未处理异常（DispatcherUnhandledException 也走这里）。</summary>
        public static void HandleException(Exception ex)
        {
            if (ex == null) return;

            lock (Sync) // 防止多个线程同时崩溃处理
            {
                try
                {
                    var info = BuildDiagnosticInfo(ex);
                    var crashLogPath = WriteCrashLog(info);
                    var dumpPath = _dumpEnabled ? WriteDump() : null;
                    ShowReport(ex, info, crashLogPath, dumpPath);
                }
                catch
                {
                    // 崩溃处理本身失败时不再抛，避免二次崩溃
                }
            }
        }

        private static string BuildDiagnosticInfo(Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== UniversalConvert 崩溃报告 ===");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            sb.AppendLine("=== 软件信息 ===");
            sb.AppendLine("版本: " + (AppVersion.Current?.ToString() ?? "未知"));
            sb.AppendLine("安装目录: " + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine("进程架构: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("运行时长: " + (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss"));
            sb.AppendLine();

            sb.AppendLine("=== 系统信息 ===");
            sb.AppendLine("OS: " + GetOSVersion());
            sb.AppendLine("64 位系统: " + (Environment.Is64BitOperatingSystem ? "是" : "否"));
            sb.AppendLine(".NET: " + Environment.Version);
            sb.AppendLine("进程工作集: " + (Environment.WorkingSet / 1024 / 1024) + " MB");
            sb.AppendLine();

            sb.AppendLine("=== 插件信息 ===");
            if (_host != null)
            {
                sb.AppendLine("已加载插件数: " + _host.Plugins.Count);
                foreach (var p in _host.Plugins)
                {
                    sb.AppendLine("  - " + p.Id + " (" + p.Name + ") v" + p.Version);
                }
                if (_host.LoadErrors.Count > 0)
                {
                    sb.AppendLine("加载错误:");
                    foreach (var e in _host.LoadErrors)
                    {
                        sb.AppendLine("  - " + e.File + ": " + e.Message);
                    }
                }
            }
            else
            {
                sb.AppendLine("(插件信息不可用)");
            }
            sb.AppendLine();

            sb.AppendLine("=== 异常 ===");
            sb.AppendLine(ex.ToString());
            sb.AppendLine();

            sb.AppendLine("=== 最近日志 ===");
            foreach (var line in Log.GetRecent(200))
            {
                sb.AppendLine(line);
            }

            return sb.ToString();
        }

        private static string WriteCrashLog(string info)
        {
            var dir = LogsDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            File.WriteAllText(path, info, Encoding.UTF8);

            // 保留最近 5 份 crash 日志，删更旧的
            var logs = Directory.GetFiles(dir, "crash-*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
            for (int i = KeepCrashLogs; i < logs.Count; i++)
            {
                try { File.Delete(logs[i]); } catch { }
            }

            return path;
        }

        private static string WriteDump()
        {
            var dir = LogsDirectory;
            Directory.CreateDirectory(dir);

            // 只留一份：先删旧的 dmp
            foreach (var f in Directory.GetFiles(dir, "crash-*.dmp"))
            {
                try { File.Delete(f); } catch { }
            }

            var path = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dmp");
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var process = Process.GetCurrentProcess())
                {
                    MiniDumpWriteDump(
                        process.Handle,
                        (uint)process.Id,
                        fs.SafeFileHandle,
                        _dumpType,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        private static void ShowReport(Exception ex, string info, string crashLogPath, string dumpPath)
        {
            var summary =
                Strings.ExceptionLabel + ": " + ex.Message + "\n" +
                Strings.ReportLabel + ": " + crashLogPath +
                (dumpPath != null ? "\n" + Strings.DumpLabel + ": " + dumpPath : "");

            var logText = ReadCurrentLog();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => OpenReportWindow(summary, info, logText));
            }
            else
            {
                OpenReportWindow(summary, info, logText);
            }
        }

        private static void OpenReportWindow(string summary, string info, string logText)
        {
            var window = new CrashReportWindow(summary, info, logText, LogsDirectory);
            window.ShowDialog();
        }

        private static string ReadCurrentLog()
        {
            try
            {
                var path = Log.FilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch { }
            return string.Empty;
        }

        private static string LogsDirectory => Path.Combine(ConfigStore.ConfigDirectory, "logs");
    }
}
