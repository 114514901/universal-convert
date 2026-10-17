using System;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 插件包格式：一个 zip，内含 manifest.json + 插件 DLL（+ 可选的 tools\ 工具二进制）。
    /// 扩展仓库发布、应用安装都遵守此格式。解压前校验包结构与安全边界：
    /// manifest.json 与 DLL 的存在性、条目路径不越界、条目数量与总解压体积上限。
    /// </summary>
    public static class PluginPackage
    {
        public const string ManifestFileName = "manifest.json";

        /// <summary>单个插件包解压后的总体积上限（2 GB），用于抵御压缩炸弹。</summary>
        public const long MaxTotalUncompressedSize = 2L * 1024 * 1024 * 1024;

        /// <summary>单个插件包允许的条目数量上限（大型扩展如 LibreOffice 包含上千个文件）。</summary>
        public const int MaxEntryCount = 50000;

        /// <summary>
        /// 从已解压的插件目录读取清单。
        /// 文件不存在、无法读取或内容非法时返回 null，不向调用方抛出异常。
        /// </summary>
        public static PluginManifest ReadManifest(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory)) return null;
                var path = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Debug("读取插件清单失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>把清单写入插件目录（打包时用）。</summary>
        public static void WriteManifest(string directory, PluginManifest manifest)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, ManifestFileName),
                JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        /// <summary>
        /// 校验已解压的插件目录：manifest.json 可读取，且至少存在一个 DLL。
        /// 校验失败抛出 InvalidDataException（消息为正式中文），供安装/暂存流程直接回报。
        /// </summary>
        public static void ValidateExtractedDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("插件目录不能为空。", nameof(directory));
            if (!Directory.Exists(directory)) throw new InvalidDataException("插件目录不存在：" + directory);
            if (ReadManifest(directory) == null)
            {
                throw new InvalidDataException("插件目录缺少可读取的 manifest.json：" + directory);
            }
            if (!ContainsDll(directory))
            {
                throw new InvalidDataException("插件目录内未找到插件 DLL：" + directory);
            }
        }

        /// <summary>
        /// 把插件包解压到目标目录（安装时用）。
        /// 解压前校验包结构（manifest.json、至少一个 DLL）、条目路径不越界、
        /// 条目数量与总解压体积上限；校验失败抛出 InvalidDataException。
        /// 目标目录由调用方准备；解压中途失败时由调用方删除该目录。
        /// </summary>
        public static void Extract(string zipPath, string destinationDirectory)
        {
            if (string.IsNullOrEmpty(zipPath))
            {
                throw new ArgumentException("插件包路径不能为空。", nameof(zipPath));
            }
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                throw new ArgumentException("插件解压目录不能为空。", nameof(destinationDirectory));
            }
            if (!File.Exists(zipPath))
            {
                throw new FileNotFoundException("插件包不存在：" + zipPath, zipPath);
            }

            var destinationRoot = Path.GetFullPath(destinationDirectory);

            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(zipPath);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("插件包已损坏或格式不正确：" + ex.Message, ex);
            }
            catch (IOException ex)
            {
                throw new InvalidDataException("插件包无法读取：" + ex.Message, ex);
            }

            using (archive)
            {
                ValidateArchive(archive, destinationRoot);
                Directory.CreateDirectory(destinationRoot);

                foreach (var entry in archive.Entries)
                {
                    bool isDirectory;
                    var target = ResolveEntryTarget(destinationRoot, entry.FullName, out isDirectory, out _);
                    if (isDirectory)
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    entry.ExtractToFile(target, true);
                }
            }
        }

        /// <summary>校验压缩包结构、条目路径、条目数量与总解压体积；不通过时抛出 InvalidDataException。</summary>
        private static void ValidateArchive(ZipArchive archive, string destinationRoot)
        {
            var entries = archive.Entries;
            if (entries.Count == 0)
            {
                throw new InvalidDataException("插件包为空。");
            }
            if (entries.Count > MaxEntryCount)
            {
                throw new InvalidDataException(string.Format(
                    "插件包条目数量超过上限（{0} 条，上限 {1} 条）。", entries.Count, MaxEntryCount));
            }

            bool hasManifest = false;
            bool hasDll = false;
            long totalLength = 0;

            foreach (var entry in entries)
            {
                bool isDirectory;
                string normalizedPath;
                ResolveEntryTarget(destinationRoot, entry.FullName, out isDirectory, out normalizedPath);
                if (isDirectory) continue;

                // 先判断本条目的解压体积再累加，避免超长长度导致的整数溢出
                if (entry.Length < 0 || entry.Length > MaxTotalUncompressedSize
                    || totalLength > MaxTotalUncompressedSize - entry.Length)
                {
                    throw new InvalidDataException(string.Format(
                        "插件包解压后总体积超过上限（{0} 字节）。", MaxTotalUncompressedSize));
                }
                totalLength += entry.Length;

                if (string.Equals(normalizedPath, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    hasManifest = true;
                }
                if (normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    hasDll = true;
                }
            }

            if (!hasManifest)
            {
                throw new InvalidDataException("插件包缺少根目录下的 manifest.json。");
            }
            if (!hasDll)
            {
                throw new InvalidDataException("插件包内未找到插件 DLL。");
            }
        }

        /// <summary>
        /// 解析条目在目标目录中的绝对路径，并校验其不越出目标目录。
        /// 路径非法或越界时抛出 InvalidDataException。
        /// </summary>
        private static string ResolveEntryTarget(
            string destinationRoot, string entryFullName, out bool isDirectory, out string normalizedPath)
        {
            normalizedPath = NormalizeEntryPath(entryFullName, out isDirectory);

            var relative = normalizedPath.Replace('/', Path.DirectorySeparatorChar);
            string target;
            try
            {
                target = Path.GetFullPath(Path.Combine(destinationRoot, relative));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("插件包条目路径无法解析：" + entryFullName, ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidDataException("插件包条目路径无法解析：" + entryFullName, ex);
            }
            catch (PathTooLongException ex)
            {
                throw new InvalidDataException("插件包条目路径过长：" + entryFullName, ex);
            }

            if (!IsPathInside(destinationRoot, target))
            {
                throw new InvalidDataException("插件包条目路径越界：" + entryFullName);
            }
            return target;
        }

        /// <summary>
        /// 规范化 zip 条目路径：统一分隔符、去除尾部目录分隔符与前导 "./"，
        /// 并拒绝空路径、绝对路径与包含冒号的路径（Windows 盘符或备用数据流）。
        /// </summary>
        private static string NormalizeEntryPath(string entryFullName, out bool isDirectory)
        {
            if (string.IsNullOrEmpty(entryFullName))
            {
                throw new InvalidDataException("插件包包含空的条目路径。");
            }

            var normalized = entryFullName.Replace('\\', '/');
            isDirectory = normalized.EndsWith("/", StringComparison.Ordinal);
            normalized = normalized.TrimEnd('/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            if (normalized.Length == 0)
            {
                throw new InvalidDataException("插件包包含无效的空条目路径。");
            }
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
            {
                throw new InvalidDataException("插件包条目使用了非法路径：" + entryFullName);
            }
            return normalized;
        }

        /// <summary>判断候选路径是否位于根目录之内（含根目录本身）；不区分大小写。</summary>
        private static bool IsPathInside(string root, string candidate)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedRoot.Length == 0) return false;
            if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
            return normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>目标目录（含子目录）内是否存在 DLL。</summary>
        private static bool ContainsDll(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories).Length > 0;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
