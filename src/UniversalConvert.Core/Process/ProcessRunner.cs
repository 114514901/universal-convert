using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        /// <summary>
        /// 运行外部工具。
        /// </summary>
        /// <param name="executable">可执行文件路径。</param>
        /// <param name="arguments">命令行参数（已含引号转义）。</param>
        /// <param name="cancellationToken">取消令牌，触发时终止进程。</param>
        /// <param name="onOutputLine">每读到一行 stderr/stdout 时的回调，用于进度解析。</param>
        /// <param name="workingDirectory">工作目录。</param>
        public static ProcessRunResult Run(
            string executable,
            string arguments,
            CancellationToken cancellationToken,
            Action<string> onOutputLine = null,
            string workingDirectory = null)
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
                    process.WaitForExit();
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
                    process.Kill();
                }
            }
            catch
            {
                // 进程可能已退出
            }
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
