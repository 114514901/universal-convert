using System;
using System.IO;
using System.Threading;
using UniversalConvert.Core.Config;

namespace UniversalConvert.App
{
    /// <summary>
    /// 后台心跳：用一个后台线程定时把当前时间写入心跳文件，供看护进程判断主程序是否还活着。
    /// 用后台线程（而非 UI 线程）——UI 卡死由看护进程的 IsHungAppWindow 单独检测，
    /// 心跳只回答「进程是否整体冻死 / 已退出」。
    /// </summary>
    public static class Heartbeat
    {
        private const int BeatIntervalMs = 2000;

        private static Timer _timer;
        private static string _path;

        /// <summary>启动心跳，返回心跳文件路径（供看护进程读取）。</summary>
        public static string Start()
        {
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            _path = Path.Combine(ConfigStore.ConfigDirectory, "logs", "heartbeat-" + pid + ".txt");

            try { Directory.CreateDirectory(Path.GetDirectoryName(_path)); } catch { }

            Write(); // 立即写一次，避免看护进程启动时读到空文件
            _timer = new Timer(_ => Write(), null, BeatIntervalMs, BeatIntervalMs);
            return _path;
        }

        public static void Stop()
        {
            var timer = _timer;
            _timer = null;
            if (timer != null)
            {
                try { timer.Dispose(); } catch { }
            }
        }

        private static void Write()
        {
            try
            {
                File.WriteAllText(_path, DateTime.UtcNow.Ticks.ToString());
            }
            catch
            {
                // 心跳写失败不影响主流程
            }
        }
    }
}
