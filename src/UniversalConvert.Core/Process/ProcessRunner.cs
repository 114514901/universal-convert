using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UniversalConvert.Core.Process
{
    /// <summary>外部进程执行结果。</summary>
    public sealed class ProcessRunResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool TimedOut { get; set; }
    }

    /// <summary>
    /// 运行外部转换工具的通用执行器。
    /// 负责：启动进程、重定向输出、异步读取、取消时终止进程、按回调解析进度。
    /// </summary>
    public static class ProcessRunner
    {
        // 暂停外部进程（未文档化 API，被主流工具广泛使用；挂起/恢复是安全的）
        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr processHandle);

        /// <summary>
        /// 运行外部工具。
        /// </summary>
        /// <param name="executable">可执行文件路径。</param>
        /// <param name="arguments">命令行参数（已含引号转义）。</param>
        /// <param name="cancellationToken">取消令牌，触发时终止进程。</param>
        /// <param name="onOutputLine">每读到一行 stderr/stdout 时的回调，用于进度解析。</param>
        /// <param name="workingDirectory">工作目录。</param>
        /// <param name="pauseSignal">暂停信号：置位期间挂起进程，复位后恢复；null 表示不支持暂停。</param>
        public static ProcessRunResult Run(
            string executable,
            string arguments,
            CancellationToken cancellationToken,
            Action<string> onOutputLine = null,
            string workingDirectory = null,
            ManualResetEventSlim pauseSignal = null)
        {
            var result = new ProcessRunResult();
            var output = new StringBuilder();
            var error = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }
            else
            {
                // 默认用系统临时目录作为工作目录：主程序可能从不可写目录（如 Program Files）启动，
                // 外部工具（ffmpeg/markitdown 等）会在当前工作目录创建临时文件，继承只读 CWD 会直接失败
                try { psi.WorkingDirectory = Path.GetTempPath(); } catch { }
            }

            using (var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var outputDone = new ManualResetEventSlim(false);
                var errorDone = new ManualResetEventSlim(false);

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) { outputDone.Set(); return; }
                    output.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) { errorDone.Set(); return; }
                    error.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() => TryKill(process)))
                {
                    // 轮询等待：暂停信号置位期间挂起进程，复位后恢复
                    while (!process.WaitForExit(300))
                    {
                        if (pauseSignal != null && pauseSignal.IsSet)
                        {
                            SuspendProcess(process);
                            try
                            {
                                pauseSignal.Wait(); // 阻塞直到恢复（取消时 TryKill 会终止挂起的进程）
                            }
                            finally
                            {
                                ResumeProcess(process);
                            }
                        }
                    }
                }

                outputDone.Wait(2000);
                errorDone.Wait(2000);

                result.ExitCode = process.ExitCode;
                result.StandardOutput = output.ToString();
                result.StandardError = error.ToString();
            }

            return result;
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    // 用 taskkill /T 杀整棵进程树：PyInstaller onefile（如 whisper/markitdown/pysubs2）
                    // 是"外壳进程 + 实际工作子进程"两级结构，只 Kill 外壳会让子进程变孤儿继续运行
                    try
                    {
                        var psi = new ProcessStartInfo(
                            "taskkill.exe",
                            string.Format("/pid {0} /t /f", process.Id))
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (var killer = System.Diagnostics.Process.Start(psi))
                        {
                            if (killer != null) killer.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        // taskkill 失败则回退直接 Kill
                    }
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
            }
            catch
            {
                // 进程可能已退出
            }
        }

        private static void SuspendProcess(System.Diagnostics.Process process)
        {
            try { NtSuspendProcess(process.Handle); } catch { }
            foreach (var child in GetChildProcessIds(process.Id))
            {
                try
                {
                    using (var p = System.Diagnostics.Process.GetProcessById(child)) { NtSuspendProcess(p.Handle); }
                }
                catch { }
            }
        }

        private static void ResumeProcess(System.Diagnostics.Process process)
        {
            try { NtResumeProcess(process.Handle); } catch { }
            foreach (var child in GetChildProcessIds(process.Id))
            {
                try
                {
                    using (var p = System.Diagnostics.Process.GetProcessById(child)) { NtResumeProcess(p.Handle); }
                }
                catch { }
            }
        }

        /// <summary>枚举直接子进程 PID（tasklist 按 PPID 过滤；PyInstaller onefile 的外壳/工作进程关系）。</summary>
        private static List<int> GetChildProcessIds(int parentId)
        {
            var result = new List<int>();
            try
            {
                var psi = new ProcessStartInfo("tasklist.exe",
                    string.Format("/fi \"PPID eq {0}\" /fo csv /nh", parentId))
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var csv = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    foreach (var line in csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            int id;
                            if (int.TryParse(parts[1].Trim('"', ' '), out id) && id != parentId)
                            {
                                result.Add(id);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 枚举失败则只挂起/恢复主进程
            }
            return result;
        }

        /// <summary>把单个参数按命令行规则加引号（含空格或引号时）。</summary>
        public static string Quote(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";
            if (!argument.Contains(" ") && !argument.Contains("\"")) return argument;
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }
    }
}
