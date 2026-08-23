using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 插件包格式：一个 zip，内含 manifest.json + 插件 DLL（+ 可选的 tools\ 工具二进制）。
    /// 扩展仓库发布、应用安装都遵守此格式。
    /// </summary>
    public static class PluginPackage
    {
        public const string ManifestFileName = "manifest.json";

        /// <summary>从已解压的插件目录读取清单；不存在则返回 null。</summary>
        public static PluginManifest ReadManifest(string directory)
        {
            var path = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(path));
        }

        /// <summary>把清单写入插件目录（打包时用）。</summary>
        public static void WriteManifest(string directory, PluginManifest manifest)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, ManifestFileName),
                JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        /// <summary>把插件包解压到目标目录（安装时用）。</summary>
        public static void Extract(string zipPath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            ZipFile.ExtractToDirectory(zipPath, destinationDirectory);
        }
    }
}
