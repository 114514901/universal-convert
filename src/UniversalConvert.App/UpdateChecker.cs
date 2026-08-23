using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalConvert.App
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string DownloadUrl { get; set; }
    }

    /// <summary>
    /// 检查 GitHub Release 是否有新版本，并支持带进度的下载。
    /// 注意：私有仓库的 Release API 与资产下载都需要鉴权，匿名访问会失败。
    /// 若要启用自动更新，仓库需设为公开，或在配置中提供 token。
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiUrl = "https://api.github.com/repos/114514901/universal-convert/releases/latest";
        private const string AssetSuffix = "Setup.exe";

        public static async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "UniversalConvert");
                    var json = await client.DownloadStringTaskAsync(ApiUrl).ConfigureAwait(false);

                    var obj = JObject.Parse(json);
                    var tag = (string)obj["tag_name"];
                    var url = (string)obj["html_url"];
                    if (string.IsNullOrEmpty(tag)) return null;

                    var latest = ParseVersion(tag);
                    var current = CurrentVersion();
                    if (latest == null || current == null || latest <= current) return null;

                    return new UpdateInfo
                    {
                        Version = tag,
                        Url = url,
                        DownloadUrl = FindDownloadUrl(obj)
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task DownloadAsync(string downloadUrl, string destPath, IProgress<double> progress, CancellationToken ct)
        {
            var req = (HttpWebRequest)WebRequest.Create(downloadUrl);
            req.UserAgent = "UniversalConvert";
            req.AllowAutoRedirect = true;

            using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            using (var stream = resp.GetResponseStream())
            using (var file = File.Create(destPath))
            {
                long total = resp.ContentLength;
                long read = 0;
                var buffer = new byte[8192];
                int n;
                while ((n = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await file.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                    read += n;
                    if (total > 0)
                    {
                        progress?.Report((double)read / total * 100.0);
                    }
                }
            }
        }

        private static string FindDownloadUrl(JObject obj)
        {
            var assets = obj["assets"] as JArray;
            if (assets == null) return null;

            foreach (var asset in assets)
            {
                var name = (string)asset["name"];
                if (name != null && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return (string)asset["browser_download_url"];
                }
            }
            return null;
        }

        private static Version CurrentVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return new Version(v.Major, v.Minor, v.Build);
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var t = tag.TrimStart('v', 'V');
            Version v;
            return Version.TryParse(t, out v) ? v : null;
        }
    }
}
