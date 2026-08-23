using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>扩展仓库里的一个可用扩展。</summary>
    public sealed class ExtensionInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string MinAppVersion { get; set; }
        public string MaxAppVersion { get; set; }
        public string Homepage { get; set; }
        public string DownloadUrl { get; set; }
    }

    /// <summary>扩展中心：从扩展仓库拉取 index.json，安装/卸载扩展（到用户插件目录）。</summary>
    public static class ExtensionCenter
    {
        private const string IndexUrl =
            "https://raw.githubusercontent.com/114514901/universal-convert-extensions/main/index.json";

        public static async Task<IList<ExtensionInfo>> GetAvailableAsync()
        {
            using (var client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                var json = await client.DownloadStringTaskAsync(IndexUrl).ConfigureAwait(false);
                return Parse(json);
            }
        }

        private static IList<ExtensionInfo> Parse(string json)
        {
            var result = new List<ExtensionInfo>();
            var obj = JObject.Parse(json);
            var array = obj["plugins"] as JArray;
            if (array == null) return result;

            foreach (var item in array)
            {
                result.Add(new ExtensionInfo
                {
                    Id = (string)item["id"],
                    Name = (string)item["name"],
                    Description = (string)item["description"],
                    Version = (string)item["version"],
                    Author = (string)item["author"],
                    MinAppVersion = (string)item["minAppVersion"],
                    MaxAppVersion = (string)item["maxAppVersion"],
                    Homepage = (string)item["homepage"],
                    DownloadUrl = (string)item["downloadUrl"]
                });
            }
            return result;
        }

        public static string GetInstallDirectory(ExtensionInfo info)
        {
            return Path.Combine(ConfigStore.UserPluginsDirectory, info.Name);
        }

        public static bool IsInstalled(ExtensionInfo info)
        {
            return File.Exists(Path.Combine(GetInstallDirectory(info), PluginPackage.ManifestFileName));
        }

        public static string GetInstalledVersion(ExtensionInfo info)
        {
            var manifest = PluginPackage.ReadManifest(GetInstallDirectory(info));
            return manifest?.Version;
        }

        public static async Task InstallAsync(ExtensionInfo info, IProgress<double> progress, CancellationToken ct)
        {
            var dir = GetInstallDirectory(info);
            var temp = Path.Combine(Path.GetTempPath(), "uc_ext_" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                await UpdateChecker.DownloadAsync(info.DownloadUrl, temp, progress, ct).ConfigureAwait(false);

                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                PluginPackage.Extract(temp, dir);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public static void Uninstall(ExtensionInfo info)
        {
            var dir = GetInstallDirectory(info);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
