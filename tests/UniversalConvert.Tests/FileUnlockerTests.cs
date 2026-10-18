using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Tests
{
    /// <summary>文件占用解除：子进程持有独占句柄，主进程通过 Restart Manager + 句柄关闭解除。</summary>
    internal static class FileUnlockerTests
    {
        public static void RunAll()
        {
            Program.Run("文件占用：关闭子进程的文件句柄", UnlockChildHandle);
        }

        private static void UnlockChildHandle()
        {
            var path = Path.Combine(
                Path.GetTempPath(), "uc_unlock_test_" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

            var executable = typeof(Program).Assembly.Location;
            var startInfo = new ProcessStartInfo(executable, "--hold-lock \"" + path + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            Process child = null;
            try
            {
                child = Process.Start(startInfo);
                Program.True(child != null, "应启动持锁子进程");
                var ready = child.StandardOutput.ReadLine();
                Program.Equal("locked", ready, "子进程应持有文件句柄");

                var lockers = FileUnlocker.FindLockingProcesses(new[] { path });
                Program.True(lockers.Any(item => item.ProcessId == child.Id),
                    "Restart Manager 应识别持锁子进程");

                var result = FileUnlocker.Unlock(new[] { path }, FileUnlockPolicy.All);
                Program.True(result.HandlesClosed >= 1, "应关闭子进程的文件句柄");
                Program.True(FileUnlocker.TryOpenExclusive(path), "解除后文件应可独占打开");
            }
            finally
            {
                try
                {
                    if (child != null && !child.HasExited) child.Kill();
                }
                catch
                {
                    // 清理失败不影响断言
                }
                try { File.Delete(path); } catch { }
            }
        }
    }
}
