using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.App
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string DownloadUrl { get; set; }
        public string Body { get; set; }
        public bool IsPrerelease { get; set; }

        /// <summary>安装包 SHA256（十六进制，不含前缀）；未知为 null。</summary>
        public string Sha256 { get; set; }
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

        /// <summary>安装包资产名，与 CI 打包/Inno Setup 输出保持一致（packaging/UniversalConvert.iss）。</summary>
        private const string AssetName = "UniversalConvert-Setup.exe";

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

                    Log.Info($"发现新版本 {tag}" + (obj["prerelease"] != null && (bool)obj["prerelease"] ? " (预发布)" : ""));
                    string sha256;
                    var downloadUrl = FindDownloadUrl(obj, out sha256);
                    return new UpdateInfo
                    {
                        Version = tag,
                        Url = htmlUrl,
                        DownloadUrl = downloadUrl,
                        Body = (string)obj["body"],
                        IsPrerelease = obj["prerelease"] != null && (bool)obj["prerelease"],
                        Sha256 = sha256
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Warn("检查更新失败: " + ex.Message);
                return null;
            }
        }

        private const int DownloadThreads = 8;

        public static async Task DownloadAsync(string downloadUrl, string destPath, IProgress<double> progress, CancellationToken ct, string expectedSha256 = null)
        {
            Log.Info($"开始下载更新: {downloadUrl}");
            long total = await GetContentLengthAsync(downloadUrl, ct).ConfigureAwait(false);

            if (total <= 0)
            {
                // 拿不到文件大小（或服务器不支持），回退单线程
                Log.Info("服务器不支持分段下载，回退单线程");
                await DownloadSequentialAsync(downloadUrl, destPath, progress, ct).ConfigureAwait(false);
            }
            else
            {
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

            // 下载完成后统一做 SHA256 校验（防下载损坏/被篡改）
            VerifySha256(destPath, expectedSha256);
            Log.Info("更新下载完成" + (string.IsNullOrEmpty(expectedSha256) ? "" : "（SHA256 校验通过）"));
        }

        /// <summary>计算文件 SHA256（十六进制小写）。</summary>
        public static string ComputeSha256(string filePath)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>校验文件 SHA256；期望值为空时跳过；不匹配抛异常。</summary>
        private static void VerifySha256(string filePath, string expectedSha256)
        {
            if (string.IsNullOrEmpty(expectedSha256)) return;
            var actual = ComputeSha256(filePath);
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "SHA256 校验失败（期望 " + expectedSha256 + "，实际 " + actual + "）");
            }
        }

        private static async Task<long> GetContentLengthAsync(string url, CancellationToken ct)
        {
            var req = CreateRequest(url, "HEAD");
            using (var resp = await GetResponseAsync(req, ct).ConfigureAwait(false))
            {
                return resp.ContentLength;
            }
        }

        private static async Task DownloadRangeAsync(string url, string destPath, long start, long end, Action<int> onBytes, CancellationToken ct)
        {
            var req = CreateRequest(url, "GET");
            req.AddRange(start, end);

            using (var resp = await GetResponseAsync(req, ct).ConfigureAwait(false))
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

        private static HttpWebRequest CreateRequest(string url, string method)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "UniversalConvert";
            req.AllowAutoRedirect = false; // 手动跟随重定向：.NET Framework 自动重定向会丢失 Range 头，导致分段下载错乱
            req.Method = method;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            return req;
        }

        /// <summary>手动跟随 HTTP 重定向（保留 Range 头），最多 5 跳，避免下载静默卡死。</summary>
        private static async Task<HttpWebResponse> GetResponseAsync(HttpWebRequest req, CancellationToken ct)
        {
            var response = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false);

            for (int hop = 0; hop < 5 && IsRedirect(response.StatusCode); hop++)
            {
                var location = response.Headers["Location"];
                response.Dispose();
                if (string.IsNullOrEmpty(location)) throw new WebException("重定向缺少 Location");

                var next = CreateRequest(new Uri(req.Address, location).AbsoluteUri, req.Method);

                // 复制 Range 头（bytes=from-to）
                var range = req.Headers["Range"];
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes="))
                {
                    var parts = range.Substring(6).Split('-');
                    long from = -1, to = -1;
                    long.TryParse(parts[0], out from);
                    if (parts.Length > 1) long.TryParse(parts[1], out to);
                    if (from >= 0 && to >= from) next.AddRange(from, to);
                }

                req = next;
                response = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false);
            }
            return response;
        }

        private static bool IsRedirect(HttpStatusCode code)
        {
            int c = (int)code;
            return c == 301 || c == 302 || c == 303 || c == 307 || c == 308;
        }

        private static async Task DownloadSequentialAsync(string url, string destPath, IProgress<double> progress, CancellationToken ct)
        {
            var req = CreateRequest(url, "GET");

            using (var resp = await GetResponseAsync(req, ct).ConfigureAwait(false))
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

        private static string FindDownloadUrl(JObject obj, out string sha256)
        {
            sha256 = null;
            var assets = obj["assets"] as JArray;
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    var name = (string)asset["name"];
                    if (name != null && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        sha256 = NormalizeSha256((string)asset["digest"]);
                        return (string)asset["browser_download_url"];
                    }
                }
            }

            // 资产列表为空（release 刚创建、CI 上传尚未完成）时，按固定命名规则构造下载地址兜底，
            // 否则会出现「发现新版本但下载更新按钮不出现」的竞态（此场景拿不到 digest，跳过校验）
            var tag = (string)obj["tag_name"];
            if (!string.IsNullOrEmpty(tag))
            {
                return "https://github.com/114514901/universal-convert/releases/download/" + tag + "/" + AssetName;
            }
            return null;
        }

        /// <summary>把 GitHub 的 digest（"sha256:xxxx"）规范化为裸十六进制；无效返回 null。</summary>
        private static string NormalizeSha256(string digest)
        {
            if (string.IsNullOrEmpty(digest)) return null;
            var d = digest.Trim();
            if (d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) d = d.Substring(7);
            return d.Length == 64 ? d.ToLowerInvariant() : null;
        }
    }
}
