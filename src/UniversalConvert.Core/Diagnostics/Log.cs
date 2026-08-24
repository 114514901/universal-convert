using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace UniversalConvert.Core.Diagnostics
{
    /// <summary>日志级别，数值越大越严重。</summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// 统一日志设施：主程序、右键菜单、安装器、插件共用。
    /// 支持级别过滤、写文件（追加 + UTF-8）、调试器输出、以及供崩溃报告使用的环形缓冲。
    /// 不同入口通过 Configure 指定各自的日志文件（app.log / contextmenu.log / install.log）。
    /// </summary>
    public static class Log
    {
        private const int RingCapacity = 500;

        private static readonly object Sync = new object();
        private static readonly Queue<string> Ring = new Queue<string>();

        private static string _filePath;
        private static LogLevel _minLevel = LogLevel.Info;

        /// <summary>当前日志文件完整路径；未配置时为 null。</summary>
        public static string FilePath
        {
            get { lock (Sync) { return _filePath; } }
        }

        /// <summary>配置日志输出目标与最低级别（启动时调用一次）。</summary>
        public static void Configure(string filePath, LogLevel minLevel)
        {
            lock (Sync)
            {
                _filePath = filePath;
                _minLevel = minLevel;
            }
        }

        public static void Debug(string message) => Write(LogLevel.Debug, message);
        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warn, message);
        public static void Error(string message) => Write(LogLevel.Error, message);

        public static void Error(string message, Exception ex)
        {
            Write(LogLevel.Error, message + Environment.NewLine + ex);
        }

        /// <summary>从设置字符串解析级别；无法识别时回退 Info。</summary>
        public static LogLevel ParseLevel(string value)
        {
            if (string.IsNullOrEmpty(value)) return LogLevel.Info;
            LogLevel level;
            return Enum.TryParse(value, true, out level) ? level : LogLevel.Info;
        }

        /// <summary>取最近若干条日志（供崩溃报告用）。</summary>
        public static string[] GetRecent(int count)
        {
            lock (Sync)
            {
                var arr = Ring.ToArray();
                if (arr.Length <= count) return arr;
                var result = new string[count];
                Array.Copy(arr, arr.Length - count, result, 0, count);
                return result;
            }
        }

        private static void Write(LogLevel level, string message)
        {
            if (level < _minLevel) return;
            var line = FormatLine(level, message);

            lock (Sync)
            {
                Ring.Enqueue(line);
                while (Ring.Count > RingCapacity) Ring.Dequeue();

                if (!string.IsNullOrEmpty(_filePath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(_filePath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
                    }
                    catch
                    {
                        // 写日志失败不影响主流程
                    }
                }
            }

            Debug.WriteLine("[UniversalConvert." + level + "] " + message);
        }

        private static string FormatLine(LogLevel level, string message)
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + level + "]  " + message;
        }
    }
}
