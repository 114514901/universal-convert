using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace UniversalConvert.Watchdog
{
    /// <summary>
    /// 看护进程：监控主程序（UniversalConvert.App.exe）是否卡死。
    /// 两条检测通道：
    ///   1. 后台心跳超时（主程序整体冻死 / 已退出）——阈值 15 秒；
    ///   2. IsHungAppWindow 持续无响应（UI 线程卡死）——连续无响应 ≥15 秒。
    /// 命中后结束主程序并拉起 App.exe --report hang 弹出卡死报告。
    /// 不自动重启（重启由报告窗口的「重启」按钮交给用户）。
    /// </summary>
    internal static class Program
    {
        private const long HangThresholdTicks = 15L * TimeSpan.TicksPerSecond; // 15 秒
        private const int PollIntervalMs = 3000;

        [DllImport("user32.dll")]
        private static extern bool IsHungAppWindow(IntPtr hWnd);

        private static void Main(string[] args)
        {
            int pid = 0;
            string heartbeatPath = null;
            string appPath = null;
            string logsDir = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--pid":
                        if (i + 1 < args.Length) int.TryParse(args[++i], out pid);
                        break;
                    case "--heartbeat":
                        if (i + 1 < args.Length) heartbeatPath = args[++i];
                        break;
                    case "--app":
                        if (i + 1 < args.Length) appPath = args[++i];
                        break;
                    case "--logs":
                        if (i + 1 < args.Length) logsDir = args[++i];
                        break;
                }
            }

            if (pid <= 0 || string.IsNullOrEmpty(heartbeatPath) || string.IsNullOrEmpty(appPath))
            {
                return; // 参数不全则静默退出
            }

            Process target;
            try
            {
                target = Process.GetProcessById(pid);
            }
            catch
            {
                return; // 主程序已退出
            }

            Log("看护进程启动：pid=" + pid);

            try
            {
                Run(target, heartbeatPath, appPath, logsDir);
            }
            finally
            {
                TryDelete(heartbeatPath);
            }
        }

        private static void Run(Process target, string heartbeatPath, string appPath, string logsDir)
        {
            long lastBeat = DateTime.UtcNow.Ticks;
            DateTime? hungSince = null;

            while (true)
            {
                try
                {
                    if (target.HasExited)
                    {
                        return; // 主程序正常/异常退出；崩溃报告由主程序进程内处理
                    }

                    // 1. 心跳超时：进程整体冻死（后台线程也不再写心跳）
                    long beat = ReadHeartbeat(heartbeatPath);
                    if (beat > 0) lastBeat = beat;
                    bool heartbeatStale = (DateTime.UtcNow.Ticks - lastBeat) > HangThresholdTicks;

                    // 2. UI 无响应：IsHungAppWindow 持续 15 秒
                    target.Refresh();
                    var hwnd = target.MainWindowHandle;
                    bool uiHung = hwnd != IntPtr.Zero && IsHungAppWindow(hwnd);
                    if (uiHung)
                    {
                        if (hungSince == null) hungSince = DateTime.UtcNow;
                    }
                    else
                    {
                        hungSince = null;
                    }
                    bool uiHungSustained = hungSince.HasValue
                        && (DateTime.UtcNow - hungSince.Value).TotalSeconds >= 15.0;

                    if (heartbeatStale || uiHungSustained)
                    {
                        Log("卡死检测触发：heartbeatStale=" + heartbeatStale + ", uiHungSustained=" + uiHungSustained);
                        try { target.Kill(); } catch { }
                        LaunchReport(appPath, logsDir);
                        return;
                    }
                }
                catch
                {
                    // 进程可能已退出，下一轮 HasExited 会捕获
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        private static long ReadHeartbeat(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                long ticks;
                return long.TryParse(File.ReadAllText(path).Trim(), out ticks) ? ticks : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void LaunchReport(string appPath, string logsDir)
        {
            try
            {
                var args = "--report hang";
                if (!string.IsNullOrEmpty(logsDir))
                {
                    args += " \"" + logsDir + "\"";
                }
                Process.Start(appPath, args);
            }
            catch
            {
                // 启动报告失败
            }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UniversalConvert", "logs");
                Directory.CreateDirectory(dir);
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [Watchdog]  " + message;
                File.AppendAllText(Path.Combine(dir, "watchdog.log"), line + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响看护
            }
        }
    }
}
