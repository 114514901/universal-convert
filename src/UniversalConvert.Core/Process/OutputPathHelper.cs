using System;
using System.IO;

namespace UniversalConvert.Core.Process
{
    /// <summary>
    /// 输出路径工具。集中处理「输出路径撞上输入文件」这一类问题，供各转换器复用，
    /// 避免每个插件各写一份容易漏改的路径拼接。
    /// </summary>
    public static class OutputPathHelper
    {
        /// <summary>
        /// 输出与输入同名时加 <c>-converted</c> 后缀另存。
        ///
        /// 同格式转换（如 opus→opus 只改码率）按「源目录 + 源文件名 + 目标扩展名」推导时，
        /// 结果会与输入文件完全重合；而外部工具通常不允许原地读写
        /// （FFmpeg 直接报 <c>Output ... same as Input - exiting</c>）。
        /// </summary>
        public static string AvoidSameAsInput(string outputPath, string inputPath)
        {
            if (string.IsNullOrEmpty(outputPath) || string.IsNullOrEmpty(inputPath)) return outputPath;
            if (!string.Equals(outputPath, inputPath, StringComparison.OrdinalIgnoreCase)) return outputPath;

            var dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(outputPath);
            var ext = Path.GetExtension(outputPath);
            return Path.Combine(dir, name + "-converted" + ext);
        }
    }
}
