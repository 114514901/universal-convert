using System;
using System.Globalization;
using System.IO;

namespace UniversalConvert.App
{
    /// <summary>预览音量记忆：内置播放器与 VLC 扩展共享同一配置文件（0-1 双精度）。</summary>
    internal static class VolumeMemory
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UniversalConvert", "preview-volume.txt");

        /// <summary>读取上次音量（0-1）；无记录/解析失败返回 null（调用方回退默认满音量）。</summary>
        public static double? Load()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return null;
                double v;
                var text = File.ReadAllText(path).Trim();
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return null;
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                return v;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(double value)
        {
            try
            {
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, value.ToString("0.###", CultureInfo.InvariantCulture));
            }
            catch
            {
                // 保存失败忽略（不打扰播放）
            }
        }
    }
}