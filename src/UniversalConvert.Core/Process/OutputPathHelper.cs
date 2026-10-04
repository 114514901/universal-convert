using System;
using System.IO;

namespace UniversalConvert.Core.Process
{
    /// <summary>
    /// 输出路径工具。集中处理「输出路径撞上输入文件」和「输出路径已被占用」这两类问题，
    /// 供各转换器复用，避免每个插件各写一份容易漏改的路径拼接。
    /// </summary>
    public static class OutputPathHelper
    {
        /// <summary>编号上限，避免目录异常时死循环。</summary>
        private const int MaxAttempts = 1000;

        /// <summary>
        /// 输出与输入同名时加 <c>-converted</c> 后缀另存。
        ///
        /// 同格式转换（如 opus→opus 只改码率）按「源目录 + 源文件名 + 目标扩展名」推导时，
        /// 结果会与输入文件完全重合；而外部工具通常不允许原地读写
        /// （FFmpeg 直接报 <c>Output ... same as Input - exiting</c>）。
        ///
        /// 注意：<see cref="ReserveUniqueOutputPath"/> 已包含这个场景（存在即换名），
        /// 此方法保留给「只想避开输入、允许覆盖其他既有文件」的调用方。
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

        /// <summary>
        /// 为输出「预订」一个不冲突的路径：目标已存在（或与输入同一路径）时依次尝试
        /// <c>name (1).ext</c>、<c>name (2).ext</c> …… 并用 <see cref="FileMode.CreateNew"/> 原子占位。
        ///
        /// 原子占位同时也解决并发问题：批量转换里两个 worker 推导出同一路径时，
        /// 只有一个能抢到原名，另一个自动落到 <c>(1)</c>，不会互相覆盖。
        /// 调用方负责在转换失败/取消时删除这个占位文件（内容为空）。
        /// </summary>
        /// <returns>成功占位的路径；目录不可写等情况下回退为原始路径，由调用方报错。</returns>
        public static string ReserveUniqueOutputPath(string desiredPath, string inputPath)
        {
            if (string.IsNullOrEmpty(desiredPath)) return desiredPath;

            var dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(desiredPath);
            var ext = Path.GetExtension(desiredPath);

            for (int i = 0; i < MaxAttempts; i++)
            {
                var candidate = i == 0
                    ? desiredPath
                    : Path.Combine(dir, name + " (" + i + ")" + ext);

                // 与输入同一路径：外部工具不能原地编辑，直接换下一个编号
                if (!string.IsNullOrEmpty(inputPath) &&
                    string.Equals(candidate, inputPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    // CreateNew 是原子的：抢到就归本次转换，抢不到（已存在）就换下一个编号
                    using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                    }
                    return candidate;
                }
                catch (IOException)
                {
                    // 已存在或正被占用 → 试下一个编号
                }
                catch (UnauthorizedAccessException)
                {
                    // 目录不可写：换编号也救不了，交给调用方按原路径报错
                    return desiredPath;
                }
            }

            return desiredPath;
        }
    }
}
