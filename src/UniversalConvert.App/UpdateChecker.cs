using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

        /// <summary>是否具备自动安装所需的地址与摘要。缺少任一项时只能打开发布页。</summary>
        public bool CanAutoInstall
        {
            get { return !string.IsNullOrEmpty(DownloadUrl) && !string.IsNullOrEmpty(Sha256); }
        }
    }

    /// <summary>更新检查结果：区分“没有新版本”和“检查失败”。</summary>
    public sealed class UpdateCheckResult
    {
        public UpdateInfo Update { get; set; }
        public string ErrorMessage { get; set; }
        public bool Failed
        {
            get { return !string.IsNullOrEmpty(ErrorMessage); }
        }
    }

    /// <summary>
    /// 检查 GitHub Release 并下载安装包。
    /// 通道规则：channel 为 "dev" 时检查含 prerelease 的版本；"stable" 时只查稳定版；
    /// "auto"（默认）则跟随当前版本（当前为 prerelease 就查开发版）。
    ///
    /// 下载完整性约定：
    /// - 分块下载必须收到 206 与匹配的 Content-Range；
    /// - 服务器不支持 Range 时回退单线程；
    /// - 最终校验文件长度与 SHA256；
    /// - 缺少摘要时不提供自动安装地址（由调用方打开发布页）。
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiBase = "https://api.github.com/repos/114514901/universal-convert/releases";
        private const string AssetSuffix = "Setup.exe";
        private const string AssetName = "UniversalConvert-Setup.exe";

        /// <summary>全局连接预算上限（同时最多 9 个分块请求）。</summary>
        private const int MaxConnections = 9;

        /// <summary>.NET Framework 默认每主机连接数为 2，必须显式提高，否则并发预算无效。</summary>
        private const int ConnectionLimit = 16;

        private const long ChunkSize = 8L * 1024 * 1024;
        private const int MaxChunkAttempts = 5;
        private const int MaxSequentialAttempts = 3;
        private const int MaxRedirects = 5;
        private const int ProgressIntervalMs = 100;

        private static readonly Regex Sha256Pattern =
            new Regex("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
        private static readonly Regex ContentRangePattern =
            new Regex(@"^\s*bytes\s+(\d+)-(\d+)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static UpdateChecker()
        {
            try
            {
                if (ServicePointManager.DefaultConnectionLimit < ConnectionLimit)
                {
                    ServicePointManager.DefaultConnectionLimit = ConnectionLimit;
                }
            }
            catch
            {
                // 连接上限设置失败不影响功能
            }
        }

        public static async Task<UpdateInfo> CheckAsync(string channel = "auto")
        {
            var result = await CheckWithResultAsync(channel, CancellationToken.None).ConfigureAwait(false);
            return result.Update;
        }

        /// <summary>
        /// 检查更新，并区分“没有新版本”和“检查失败”。
        /// ct 取消时抛出 OperationCanceledException；其它网络/解析错误通过 ErrorMessage 返回。
        /// </summary>
        public static async Task<UpdateCheckResult> CheckWithResultAsync(string channel, CancellationToken ct)
        {
            try
            {
                var current = AppVersion.Current;
                if (current == null)
                {
                    return new UpdateCheckResult { ErrorMessage = "无法读取当前应用版本。" };
                }

                bool isDev = channel == "dev" || (channel == "auto" && current.IsPrerelease);
                var url = isDev ? ApiBase + "?per_page=1" : ApiBase + "/latest";
                var json = await DownloadStringAsync(url, ct, "application/vnd.github+json").ConfigureAwait(false);

                JObject obj;
                if (isDev)
                {
                    var array = JArray.Parse(json);
                    if (array.Count == 0)
                    {
                        return new UpdateCheckResult { ErrorMessage = "发布列表为空。" };
                    }
                    obj = (JObject)array[0];
                }
                else
                {
                    obj = JObject.Parse(json);
                }

                var tag = (string)obj["tag_name"];
                var htmlUrl = (string)obj["html_url"];
                if (string.IsNullOrEmpty(tag))
                {
                    return new UpdateCheckResult { ErrorMessage = "发布信息缺少版本标签。" };
                }

                var latest = SemVersion.Parse(tag);
                if (latest == null)
                {
                    return new UpdateCheckResult { ErrorMessage = "发布版本号格式无效：" + tag };
                }
                if (latest.CompareTo(current) <= 0)
                {
                    return new UpdateCheckResult { Update = null };
                }

                Log.Info("发现新版本 " + tag +
                    (obj["prerelease"] != null && (bool)obj["prerelease"] ? "（预发布）" : string.Empty));

                string sha256;
                var downloadUrl = FindDownloadUrl(obj, out sha256);
                return new UpdateCheckResult
                {
                    Update = new UpdateInfo
                    {
                        Version = tag,
                        Url = htmlUrl,
                        DownloadUrl = downloadUrl,
                        Body = (string)obj["body"],
                        IsPrerelease = obj["prerelease"] != null && (bool)obj["prerelease"],
                        Sha256 = sha256
                    }
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("检查更新失败: " + ex.Message);
                return new UpdateCheckResult { ErrorMessage = "检查更新失败：" + ex.Message };
            }
        }

        /// <summary>
        /// 下载任意文本资源（更新接口、扩展索引）。校验 HTTP 状态并支持取消。
        /// </summary>
        public static async Task<string> DownloadStringAsync(
            string url,
            CancellationToken ct,
            string accept = null)
        {
            var request = CreateRequest(url, "GET");
            request.Accept = string.IsNullOrEmpty(accept) ? "*/*" : accept;

            using (var response = await GetResponseAsync(request, ct).ConfigureAwait(false))
            {
                EnsureSuccess(response, "读取文本资源");
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 下载文件到目标路径。支持分块、暂停、取消与共享连接预算。
        /// expectedSha256 为空时仅执行长度/协议校验，调用方必须自行判断是否允许安装。
        /// </summary>
        public static Task DownloadAsync(
            string downloadUrl,
            string destPath,
            IProgress<double> progress,
            CancellationToken ct,
            string expectedSha256 = null,
            ManualResetEventSlim pause = null,
            SemaphoreSlim sharedBudget = null,
            IProgress<Tuple<long, long>> byteProgress = null)
        {
            return DownloadAsyncCore(
                downloadUrl, destPath, progress, ct, expectedSha256,
                pause, sharedBudget, byteProgress, false);
        }

        /// <summary>仅供测试：允许使用本地 HTTP 服务验证 Range/状态码/进度行为。</summary>
        internal static Task DownloadAsyncForTests(
            string downloadUrl,
            string destPath,
            IProgress<double> progress,
            CancellationToken ct,
            string expectedSha256,
            IProgress<Tuple<long, long>> byteProgress = null)
        {
            return DownloadAsyncCore(
                downloadUrl, destPath, progress, ct, expectedSha256,
                null, null, byteProgress, true);
        }

        private static async Task DownloadAsyncCore(
            string downloadUrl,
            string destPath,
            IProgress<double> progress,
            CancellationToken ct,
            string expectedSha256,
            ManualResetEventSlim pause,
            SemaphoreSlim sharedBudget,
            IProgress<Tuple<long, long>> byteProgress,
            bool allowUntrustedUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new ArgumentException("下载地址为空。", "downloadUrl");
            }
            if (!allowUntrustedUrl && !IsTrustedDownloadUrl(downloadUrl))
            {
                throw new InvalidOperationException("下载地址不受信任，必须为 GitHub HTTPS 地址。");
            }
            if (string.IsNullOrEmpty(destPath))
            {
                throw new ArgumentException("目标路径为空。", "destPath");
            }

            Log.Info("开始下载更新: " + downloadUrl);
            var total = await GetContentLengthAsync(downloadUrl, ct).ConfigureAwait(false);

            if (total <= 0)
            {
                Log.Info("服务器未提供文件大小或 HEAD 失败，回退单线程。");
                await DownloadSequentialWithRetryAsync(
                    downloadUrl, destPath, progress, ct, pause, byteProgress).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await DownloadChunkedAsync(
                        downloadUrl, destPath, total, progress, ct, pause, sharedBudget, byteProgress)
                        .ConfigureAwait(false);
                }
                catch (RangeNotSupportedException ex)
                {
                    Log.Warn("服务器不支持 Range，回退单线程: " + ex.Message);
                    TryDelete(destPath);
                    progress?.Report(0);
                    byteProgress?.Report(Tuple.Create(0L, total));
                    await DownloadSequentialWithRetryAsync(
                        downloadUrl, destPath, progress, ct, pause, byteProgress).ConfigureAwait(false);
                }
            }

            VerifyFileLength(destPath, total);
            VerifySha256(destPath, expectedSha256);
            if (string.IsNullOrEmpty(expectedSha256))
            {
                Log.Warn("下载未提供 SHA256；本次仅校验长度与传输协议。");
            }

            progress?.Report(100);
            if (total > 0)
            {
                byteProgress?.Report(Tuple.Create(total, total));
            }
            Log.Info("更新下载完成" + (string.IsNullOrEmpty(expectedSha256)
                ? string.Empty : "（SHA256 校验通过）"));
        }

        private static async Task DownloadChunkedAsync(
            string url,
            string destPath,
            long total,
            IProgress<double> progress,
            CancellationToken ct,
            ManualResetEventSlim pause,
            SemaphoreSlim sharedBudget,
            IProgress<Tuple<long, long>> byteProgress)
        {
            using (var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                file.SetLength(total);
            }

            var budget = sharedBudget ?? new SemaphoreSlim(MaxConnections);
            var ownsBudget = sharedBudget == null;
            var throttle = new ProgressThrottle(progress, byteProgress, total);
            var tasks = new List<Task>();

            try
            {
                for (long start = 0; start < total; start += ChunkSize)
                {
                    long chunkStart = start;
                    long chunkEnd = Math.Min(total - 1, start + ChunkSize - 1);
                    tasks.Add(DownloadChunkWithBudgetAsync(
                        url, destPath, chunkStart, chunkEnd,
                        delegate(long delta) { throttle.Add(delta); },
                        ct, pause, budget));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            finally
            {
                if (ownsBudget)
                {
                    budget.Dispose();
                }
            }
        }

        private static async Task DownloadChunkWithBudgetAsync(
            string url,
            string destPath,
            long start,
            long end,
            Action<long> onBytes,
            CancellationToken ct,
            ManualResetEventSlim pause,
            SemaphoreSlim budget)
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

        private static async Task DownloadChunkAsync(
            string url,
            string destPath,
            long start,
            long end,
            Action<long> onBytes,
            CancellationToken ct,
            ManualResetEventSlim pause)
        {
            for (var attempt = 0; attempt < MaxChunkAttempts; attempt++)
            {
                WaitIfPaused(pause, ct);
                ct.ThrowIfCancellationRequested();

                long reported = 0;
                try
                {
                    await DownloadRangeAsync(url, destPath, start, end, delegate(int n)
                    {
                        reported += n;
                        onBytes(n);
                    }, ct, pause).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RangeNotSupportedException)
                {
                    if (reported > 0) onBytes(-reported);
                    throw;
                }
                catch (Exception ex)
                {
                    if (reported > 0) onBytes(-reported);
                    Log.Warn(string.Format(
                        "下载块 {0}-{1} 第 {2} 次尝试失败: {3}",
                        start, end, attempt + 1, ex.Message));
                    if (attempt >= MaxChunkAttempts - 1)
                    {
                        throw;
                    }
                    await Task.Delay(GetRetryDelay(attempt, ex, 500), ct).ConfigureAwait(false);
                }
            }
        }

        private static async Task DownloadRangeAsync(
            string url,
            string destPath,
            long start,
            long end,
            Action<int> onBytes,
            CancellationToken ct,
            ManualResetEventSlim pause)
        {
            var expected = end - start + 1;
            var request = CreateRequest(url, "GET");
            request.AddRange(start, end);

            HttpWebResponse response;
            try
            {
                response = await GetResponseAsync(request, ct).ConfigureAwait(false);
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    http.Dispose();
                    throw new RangeNotSupportedException("服务器返回 416。");
                }
                throw;
            }

            using (response)
            {
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new RangeNotSupportedException(
                        "服务器未返回 206，实际状态 " + (int)response.StatusCode + "。");
                }
                if (IsTextContent(response.ContentType))
                {
                    throw new RangeNotSupportedException("服务器返回了文本内容而非文件。");
                }

                var contentRange = response.Headers["Content-Range"];
                if (!string.IsNullOrEmpty(contentRange))
                {
                    long rangeStart;
                    long rangeEnd;
                    if (!TryParseContentRange(contentRange, out rangeStart, out rangeEnd)
                        || rangeStart != start
                        || rangeEnd != end)
                    {
                        throw new RangeNotSupportedException("Content-Range 与请求区间不一致。");
                    }
                }

                if (response.ContentLength > 0 && response.ContentLength != expected)
                {
                    throw new IOException("分块长度与请求不一致。");
                }

                using (var stream = response.GetResponseStream())
                using (var file = new FileStream(destPath, FileMode.Open, FileAccess.Write, FileShare.Write))
                {
                    file.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[8192];
                    var remaining = expected;

                    while (remaining > 0)
                    {
                        var toRead = (int)Math.Min(buffer.Length, remaining);
                        var n = await stream.ReadAsync(buffer, 0, toRead).ConfigureAwait(false);
                        if (n <= 0) break;

                        WaitIfPaused(pause, ct);
                        ct.ThrowIfCancellationRequested();
                        await file.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                        onBytes(n);
                        remaining -= n;
                    }

                    if (remaining != 0)
                    {
                        throw new IOException("分块下载不完整。");
                    }
                }
            }
        }

        private static async Task DownloadSequentialWithRetryAsync(
            string url,
            string destPath,
            IProgress<double> progress,
            CancellationToken ct,
            ManualResetEventSlim pause,
            IProgress<Tuple<long, long>> byteProgress)
        {
            for (var attempt = 0; attempt < MaxSequentialAttempts; attempt++)
            {
                try
                {
                    await DownloadSequentialAsync(url, destPath, progress, ct, pause, byteProgress)
                        .ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warn(string.Format("单线程下载第 {0} 次尝试失败: {1}", attempt + 1, ex.Message));
                    if (attempt >= MaxSequentialAttempts - 1)
                    {
                        throw;
                    }
                    await Task.Delay(GetRetryDelay(attempt, ex, 800), ct).ConfigureAwait(false);
                }
            }
        }

        private static async Task DownloadSequentialAsync(
            string url,
            string destPath,
            IProgress<double> progress,
            CancellationToken ct,
            ManualResetEventSlim pause,
            IProgress<Tuple<long, long>> byteProgress)
        {
            var request = CreateRequest(url, "GET");
            using (var response = await GetResponseAsync(request, ct).ConfigureAwait(false))
            {
                EnsureSuccess(response, "下载文件");
                if (IsTextContent(response.ContentType))
                {
                    throw new InvalidDataException("服务器返回了文本内容而非文件。");
                }

                var total = response.ContentLength;
                using (var stream = response.GetResponseStream())
                using (var file = File.Create(destPath))
                {
                    var throttle = new ProgressThrottle(progress, byteProgress, total);
                    var buffer = new byte[8192];
                    long read = 0;
                    int n;
                    while ((n = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        WaitIfPaused(pause, ct);
                        ct.ThrowIfCancellationRequested();
                        await file.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                        read += n;
                        throttle.Add(n);
                    }

                    if (total > 0 && read != total)
                    {
                        throw new IOException("下载内容长度与服务器声明不一致。");
                    }
                }
            }
        }

        private static async Task<long> GetContentLengthAsync(string url, CancellationToken ct)
        {
            try
            {
                var request = CreateRequest(url, "HEAD");
                using (var response = await GetResponseAsync(request, ct).ConfigureAwait(false))
                {
                    if (!IsSuccess(response.StatusCode)) return -1;
                    return response.ContentLength;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("HEAD 请求失败，回退单线程: " + ex.Message);
                return -1;
            }
        }

        private static HttpWebRequest CreateRequest(string url, string method)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "UniversalConvert";
            request.AllowAutoRedirect = false; // 手动跟随重定向，保留 Range 头
            request.Method = method;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.KeepAlive = true;
            try
            {
                request.ServicePoint.ConnectionLimit = Math.Max(
                    request.ServicePoint.ConnectionLimit, ConnectionLimit);
            }
            catch
            {
                // 某些非 HTTP 地址无法取得 ServicePoint
            }
            return request;
        }

        private static async Task<HttpWebResponse> GetResponseAsync(
            HttpWebRequest request,
            CancellationToken ct)
        {
            var current = request;
            for (var hop = 0; ; hop++)
            {
                ct.ThrowIfCancellationRequested();
                var response = await GetResponseOnceAsync(current, ct).ConfigureAwait(false);
                if (!IsRedirect(response.StatusCode))
                {
                    return response;
                }

                if (hop >= MaxRedirects)
                {
                    response.Dispose();
                    throw new WebException("重定向次数超过上限。");
                }

                var location = response.Headers["Location"];
                if (string.IsNullOrEmpty(location))
                {
                    response.Dispose();
                    throw new WebException("重定向响应缺少 Location。");
                }

                Uri nextUri;
                if (!Uri.TryCreate(current.Address, location, out nextUri))
                {
                    response.Dispose();
                    throw new WebException("重定向地址无效：" + location);
                }

                var next = CreateRequest(nextUri.AbsoluteUri, current.Method);
                CopyRangeHeader(current, next);
                response.Dispose();
                current = next;
            }
        }

        private static async Task<HttpWebResponse> GetResponseOnceAsync(
            HttpWebRequest request,
            CancellationToken ct)
        {
            using (ct.Register(delegate
            {
                try { request.Abort(); } catch { }
            }))
            {
                try
                {
                    return (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
                }
                catch (WebException)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    throw;
                }
            }
        }

        private static void CopyRangeHeader(HttpWebRequest source, HttpWebRequest target)
        {
            var range = source.Headers["Range"];
            if (string.IsNullOrEmpty(range) || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var parts = range.Substring(6).Split('-');
            long from;
            long to;
            if (parts.Length >= 2
                && long.TryParse(parts[0], out from)
                && long.TryParse(parts[1], out to)
                && from >= 0
                && to >= from)
            {
                target.AddRange(from, to);
            }
        }

        private static bool TryParseContentRange(string value, out long start, out long end)
        {
            start = -1;
            end = -1;
            var match = ContentRangePattern.Match(value ?? string.Empty);
            if (!match.Success) return false;
            return long.TryParse(match.Groups[1].Value, out start)
                && long.TryParse(match.Groups[2].Value, out end);
        }

        private static void EnsureSuccess(HttpWebResponse response, string operation)
        {
            if (!IsSuccess(response.StatusCode))
            {
                throw new WebException(string.Format(
                    "{0}失败：HTTP {1} {2}", operation, (int)response.StatusCode, response.StatusDescription));
            }
        }

        private static bool IsSuccess(HttpStatusCode code)
        {
            var value = (int)code;
            return value >= 200 && value <= 299;
        }

        private static bool IsRedirect(HttpStatusCode code)
        {
            var value = (int)code;
            return value == 301 || value == 302 || value == 303 || value == 307 || value == 308;
        }

        private static bool IsTextContent(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return false;
            var value = contentType.ToLowerInvariant();
            return value.StartsWith("text/", StringComparison.Ordinal)
                || value.Contains("json")
                || value.Contains("html")
                || value.Contains("xml");
        }

        private static TimeSpan GetRetryDelay(int attempt, Exception exception, int baseMilliseconds)
        {
            var web = exception as WebException;
            var response = web != null ? web.Response as HttpWebResponse : null;
            if (response != null)
            {
                var retryAfter = response.Headers["Retry-After"];
                if (!string.IsNullOrEmpty(retryAfter))
                {
                    int seconds;
                    if (int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
                    {
                        return TimeSpan.FromSeconds(Math.Max(1, Math.Min(seconds, 30)));
                    }

                    DateTime when;
                    if (DateTime.TryParse(
                        retryAfter,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out when))
                    {
                        var delay = when - DateTime.UtcNow;
                        if (delay > TimeSpan.Zero && delay < TimeSpan.FromSeconds(30))
                        {
                            return delay;
                        }
                    }
                }
            }

            var exponential = baseMilliseconds * Math.Pow(2, attempt);
            var jitter = new Random(Guid.NewGuid().GetHashCode()).Next(0, 250);
            return TimeSpan.FromMilliseconds(Math.Min(8000, exponential + jitter));
        }

        private static void WaitIfPaused(ManualResetEventSlim pause, CancellationToken ct)
        {
            if (pause == null || !pause.IsSet) return;
            while (pause.IsSet)
            {
                ct.ThrowIfCancellationRequested();
                pause.Wait(200);
            }
        }

        public static string ComputeSha256(string filePath)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var hash = sha.ComputeHash(file);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

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

        private static void VerifyFileLength(string filePath, long expectedLength)
        {
            if (expectedLength <= 0) return;
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length != expectedLength)
            {
                throw new InvalidDataException(string.Format(
                    "下载文件长度不一致（期望 {0}，实际 {1}）。",
                    expectedLength,
                    info.Exists ? info.Length : 0));
            }
        }

        /// <summary>校验安装包的最低可执行文件特征，阻止 HTML 错误页被当作 EXE 启动。</summary>
        public static void VerifyInstallerFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("安装包不存在。", filePath);
            }

            var info = new FileInfo(filePath);
            if (info.Length < 1024)
            {
                throw new InvalidDataException("安装包体积异常。");
            }

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var header = new byte[2];
                if (stream.Read(header, 0, 2) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                {
                    throw new InvalidDataException("下载的更新包不是有效的 Windows 可执行文件。");
                }
            }
        }

        private static string FindDownloadUrl(JObject obj, out string sha256)
        {
            sha256 = null;
            var assets = obj["assets"] as JArray;
            if (assets == null || assets.Count == 0)
            {
                Log.Warn("发布资产尚未就绪，已禁用自动安装。");
                return null;
            }

            JObject match = null;
            foreach (var token in assets)
            {
                var asset = token as JObject;
                if (asset == null) continue;
                var name = (string)asset["name"];
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    match = asset;
                    break;
                }
            }

            if (match == null)
            {
                foreach (var token in assets)
                {
                    var asset = token as JObject;
                    if (asset == null) continue;
                    var name = (string)asset["name"];
                    if (name != null && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        match = asset;
                        break;
                    }
                }
            }

            if (match == null)
            {
                Log.Warn("未找到安装包资产，已禁用自动安装。");
                return null;
            }

            var downloadUrl = (string)match["browser_download_url"];
            var digest = NormalizeSha256((string)match["digest"]);
            if (string.IsNullOrEmpty(downloadUrl) || !IsTrustedDownloadUrl(downloadUrl))
            {
                Log.Warn("安装包资产地址不受信任，已禁用自动安装。");
                return null;
            }
            if (digest == null)
            {
                Log.Warn("安装包资产缺少 SHA256 摘要，已禁用自动安装。");
                return null;
            }

            sha256 = digest;
            return downloadUrl;
        }

        private static string NormalizeSha256(string digest)
        {
            if (string.IsNullOrEmpty(digest)) return null;
            var value = digest.Trim();
            if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(7);
            }
            return Sha256Pattern.IsMatch(value) ? value.ToLowerInvariant() : null;
        }

        internal static bool IsTrustedDownloadUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        private sealed class RangeNotSupportedException : IOException
        {
            public RangeNotSupportedException(string message) : base(message)
            {
            }
        }

        private sealed class ProgressThrottle
        {
            private readonly object _sync = new object();
            private readonly IProgress<double> _progress;
            private readonly IProgress<Tuple<long, long>> _byteProgress;
            private readonly long _total;
            private long _done;
            private int _lastReportTicks;

            public ProgressThrottle(
                IProgress<double> progress,
                IProgress<Tuple<long, long>> byteProgress,
                long total)
            {
                _progress = progress;
                _byteProgress = byteProgress;
                _total = total;
                _lastReportTicks = Environment.TickCount;
            }

            public void Add(long delta)
            {
                if (_total <= 0) return;

                long current;
                var report = false;
                lock (_sync)
                {
                    _done = Math.Max(0, _done + delta);
                    current = Math.Min(_done, _total);
                    var now = Environment.TickCount;
                    if (now - _lastReportTicks >= ProgressIntervalMs || current >= _total || delta < 0)
                    {
                        _lastReportTicks = now;
                        report = true;
                    }
                }

                if (report)
                {
                    _progress?.Report(current * 100.0 / _total);
                    _byteProgress?.Report(Tuple.Create(current, _total));
                }
            }
        }
    }
}
