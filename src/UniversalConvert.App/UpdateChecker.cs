using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core.Config;

namespace UniversalConvert.App
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string DownloadUrl { get; set; }
        public string Body { get; set; }
        public bool IsPrerelease { get; set; }
    }

    /// <summary>
    /// 检查 GitHub Release 是否有新版本，并支持带进度的下载。
    /// 通道规则：channel 为 "dev" 时检查含 prerelease 的版本；"stable" 时只查稳定版；
    /// "auto"（默认）则跟随当前版本（当前为 prerelease 就查开发版）。
    /// 检查过程写入 %AppData%\UniversalConvert\update.log 便于排查。
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiBase = "https://api.github.com/repos/114514901/universal-convert/releases";
        private const string AssetSuffix = "Setup.exe";

        public static async Task<UpdateInfo> CheckAsync(string channel = "auto")
        {
            try
            {
                var current = AppVersion.Current;
                if (current == null)
                {
                    Log("当前版本解析失败，跳过检查");
                    return null;
                }

                bool isDev = channel == "dev" || (channel == "auto" && current.IsPrerelease);
                var url = isDev ? ApiBase + "?per_page=1" : ApiBase + "/latest";
                Log("检查更新: 当前=" + current + ", 渠道=" + channel + ", isDev=" + isDev + ", url=" + url);

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "UniversalConvert");
                    var json = await client.DownloadStringTaskAsync(url).ConfigureAwait(false);

                    JObject obj = isDev
                        ? (JObject)JArray.Parse(json)[0]
                        : JObject.Parse(json);

                    var tag = (string)obj["tag_name"];
                    var htmlUrl = (string)obj["html_url"];
                    if (string.IsNullOrEmpty(tag))
                    {
                        Log("响应无 tag_name");
                        return null;
                    }

                    var latest = SemVersion.Parse(tag);
                    if (latest == null || latest.CompareTo(current) <= 0)
                    {
                        Log("无更新: latest=" + tag + " <= current=" + current);
                        return null;
                    }

                    Log("发现更新: " + tag);
                    return new UpdateInfo
                    {
                        Version = tag,
                        Url = htmlUrl,
                        DownloadUrl = FindDownloadUrl(obj),
                        Body = (string)obj["body"],
                        IsPrerelease = obj["prerelease"] != null && (bool)obj["prerelease"]
                    };
                }
            }
            catch (Exception ex)
            {
                Log("检查失败: " + ex);
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

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.ConfigDirectory);
                File.AppendAllText(
                    Path.Combine(ConfigStore.ConfigDirectory, "update.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
