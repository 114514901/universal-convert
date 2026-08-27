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

        /// <summary>全局连接预算上限（同时最多 9 个 HTTP 连接）。多文件共享预算时按块动态分配。</summary>
        private const int MaxConnections = 9;

        /// <summary>分块下载的块大小（8MB）：块级独立下载与全局预算调度，文件完成自动释放连接给剩余文件。</summary>
        private const long ChunkSize = 8L * 1024 * 1024;

        public static async Task DownloadAsync(string downloadUrl, string destPath, IProgress<double> progress, CancellationToken ct, string expectedSha256 = null, ManualResetEventSlim pause = null, SemaphoreSlim sharedBudget = null, IProgress<Tuple<long, long>> byteProgress = null)
        {
            Log.Info($"开始下载更新: {downloadUrl}");
            long total = await GetContentLengthAsync(downloadUrl, ct).ConfigureAwait(false);

            if (total <= 0)
            {
                // 拿不到文件大小（或服务器不支持），回退单线程
                Log.Info("服务器不支持分段下载，回退单线程");
                await DownloadSequentialAsync(downloadUrl, destPath, progress, ct, pause, byteProgress).ConfigureAwait(false);
            }
            else
            {
                // 预分配文件，各块写到自己的区间
                using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Write))
                {
                    fs.SetLength(total);
                }

                // 共享预算（多文件并行时由调用方传入，单文件自建 9 连接预算）
                var budget = sharedBudget ?? new SemaphoreSlim(MaxConnections);
                long done = 0;
                var doneLock = new object();

                // 切成 8MB 块，每块申请 1 个连接预算后下载；
                // 块全部完成后本文件不再竞争预算 → 剩余文件自动获得更多连接
                var tasks = new List<Task>();
                for (long start = 0; start < total; start += ChunkSize)
                {
                    long chunkStart = start;
                    long chunkEnd = Math.Min(total - 1, start + ChunkSize - 1);
                    tasks.Add(DownloadChunkWithBudgetAsync(downloadUrl, destPath, chunkStart, chunkEnd, n =>
                    {
                        lock (doneLock)
                        {
                            done += n;
                            progress?.Report(Math.Min(100.0, (double)done / total * 100.0));
                            byteProgress?.Report(Tuple.Create(done, total));
                        }
                    }, ct, pause, budget));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            // 下载完成后统一做 SHA256 校验（防下载损坏/被篡改）
            VerifySha256(destPath, expectedSha256);
            Log.Info("更新下载完成" + (string.IsNullOrEmpty(expectedSha256) ? "" : "（SHA256 校验通过）"));
        }

        /// <summary>申请连接预算后下载一个块；块完成/失败释放预算给其它文件。</summary>
        private static async Task DownloadChunkWithBudgetAsync(
            string url, string destPath, long start, long end,
            Action<int> onBytes, CancellationToken ct, ManualResetEventSlim pause, SemaphoreSlim budget)
        {
            await budget.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await DownloadChunkAsync(url, destPath, start, end, onBytes, ct, pause).ConfigureAwait(false);
            }
            finally
            {
                budget.Release();
            }
        }

        /// <summary>下载一个块；暂停时等待恢复；连接被服务端掐断（暂停太久触发读超时）时重试该块。</summary>
        private static async Task DownloadChunkAsync(
            string url, string destPath, long start, long end,
            Action<int> onBytes, CancellationToken ct, ManualResetEventSlim pause)
        {
            for (int attempt = 0; ; attempt++)
            {
                WaitIfPaused(pause, ct);
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadRangeAsync(url, destPath, start, end, onBytes, ct, pause).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 暂停引起的读超时/连接中断：等暂停结束再重试；非暂停的持续失败也先重试，过多才放弃
                    Log.Warn($"下载块 {start}-{end} 第 {attempt + 1} 次尝试失败: {ex.Message}");
                    if (attempt >= 20)
                    {
                        throw;
                    }
                    await Task.Delay(400, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>暂停信号置位期间等待（响应取消）。</summary>
        private static void WaitIfPaused(ManualResetEventSlim pause, CancellationToken ct)
        {
            if (pause == null || !pause.IsSet) return;
            while (pause.IsSet)
            {
                ct.ThrowIfCancellationRequested();
                pause.Wait(200);
            }
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

        private static async Task DownloadRangeAsync(string url, string destPath, long start, long end, Action<int> onBytes, CancellationToken ct, ManualResetEventSlim pause = null)
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
                    WaitIfPaused(pause, ct);
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

        private static async Task DownloadSequentialAsync(string url, string destPath, IProgress<double> progress, CancellationToken ct, ManualResetEventSlim pause = null, IProgress<Tuple<long, long>> byteProgress = null)
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
                    WaitIfPaused(pause, ct);
                    ct.ThrowIfCancellationRequested();
                    await file.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                    read += n;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(100.0, (double)read / total * 100.0));
                        byteProgress?.Report(Tuple.Create(read, total));
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
