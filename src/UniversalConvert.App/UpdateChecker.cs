using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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
        public string Body { get; set; }
        public bool IsPrerelease { get; set; }
    }

    /// <summary>
    /// 检查 GitHub Release 是否有新版本，并支持带进度的下载。
    /// 通道规则：channel 为 "dev" 时检查含 prerelease 的版本；"stable" 时只查稳定版；
    /// "auto"（默认）则跟随当前版本（当前为 prerelease 就查开发版）。
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
                if (current == null) return null;

                bool isDev = channel == "dev" || (channel == "auto" && current.IsPrerelease);
                var url = isDev ? ApiBase + "?per_page=1" : ApiBase + "/latest";

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "UniversalConvert");
                    client.Encoding = Encoding.UTF8; // 强制 UTF-8，避免中文系统按 GBK 解码导致乱码
                    var json = await client.DownloadStringTaskAsync(url).ConfigureAwait(false);

                    JObject obj = isDev
                        ? (JObject)JArray.Parse(json)[0]
                        : JObject.Parse(json);

                    var tag = (string)obj["tag_name"];
                    var htmlUrl = (string)obj["html_url"];
                    if (string.IsNullOrEmpty(tag)) return null;

                    var latest = SemVersion.Parse(tag);
                    if (latest == null || latest.CompareTo(current) <= 0) return null;

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
            catch
            {
                return null;
            }
        }

        private const int DownloadThreads = 8;

        public static async Task DownloadAsync(string downloadUrl, string destPath, IProgress<double> progress, CancellationToken ct)
        {
            long total = await GetContentLengthAsync(downloadUrl, ct).ConfigureAwait(false);

            if (total <= 0)
            {
                // 拿不到文件大小（或服务器不支持），回退单线程
                await DownloadSequentialAsync(downloadUrl, destPath, progress, ct).ConfigureAwait(false);
                return;
            }

            // 预分配文件，各线程写到自己的区间
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                fs.SetLength(total);
            }

            long chunkSize = (total + DownloadThreads - 1) / DownloadThreads;
            long done = 0;
            var doneLock = new object();

            var tasks = new List<Task>();
            for (int i = 0; i < DownloadThreads; i++)
            {
                long start = (long)i * chunkSize;
                if (start >= total) break;
                long end = Math.Min(total - 1, start + chunkSize - 1);

                tasks.Add(DownloadRangeAsync(downloadUrl, destPath, start, end, n =>
                {
                    lock (doneLock)
                    {
                        done += n;
                        progress?.Report((double)done / total * 100.0);
                    }
                }, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<long> GetContentLengthAsync(string url, CancellationToken ct)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "UniversalConvert";
            req.AllowAutoRedirect = true;
            req.Method = "HEAD";

            using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            {
                return resp.ContentLength;
            }
        }

        private static async Task DownloadRangeAsync(string url, string destPath, long start, long end, Action<int> onBytes, CancellationToken ct)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "UniversalConvert";
            req.AllowAutoRedirect = true;
            req.AddRange(start, end);

            using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            using (var stream = resp.GetResponseStream())
            using (var file = new FileStream(destPath, FileMode.Open, FileAccess.Write, FileShare.Write))
            {
                file.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[8192];
                int n;
                while ((n = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await file.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                    onBytes(n);
                }
            }
        }

        private static async Task DownloadSequentialAsync(string url, string destPath, IProgress<double> progress, CancellationToken ct)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
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
    }
}
