using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using UniversalConvert.App;

namespace UniversalConvert.Tests
{
    /// <summary>下载层回归测试：使用本地 HTTP 服务覆盖 Range、状态码、进度与校验。</summary>
    internal static class DownloadTests
    {
        private static readonly byte[] LargeBody = CreateBody(10 * 1024 * 1024);
        private static readonly byte[] SmallBody = CreateBody(2 * 1024 * 1024);

        public static void RunAll()
        {
            Program.Run("下载：忽略 Range 的服务器应回退单线程", RangeIgnoredFallsBack);
            Program.Run("下载：正确 206 分块与 SHA256", CorrectRangeSucceeds);
            Program.Run("下载：分块重试不得重复计入进度", RetryDoesNotOvercount);
            Program.Run("下载：HEAD 405 应回退单线程", HeadFailureFallsBack);
            Program.Run("下载：错误 SHA256 应失败", WrongShaFails);
            Program.Run("下载：重定向循环应失败", RedirectLoopFails);
            Program.Run("更新包：非 PE 文件不得通过校验", InstallerHeaderValidation);
        }

        private static void RangeIgnoredFallsBack()
        {
            using (var server = new LocalHttpServer(delegate
            {
                // 对所有请求返回 200 与完整文件，模拟忽略 Range 的代理
                return HttpResponseData.Ok(LargeBody, "application/octet-stream");
            }))
            {
                var destination = NewTempFile();
                long maxProgress = 0;
                var byteProgress = new InlineProgress<Tuple<long, long>>(delegate(Tuple<long, long> value)
                {
                    if (value.Item1 > maxProgress) maxProgress = value.Item1;
                });

                try
                {
                    UpdateChecker.DownloadAsyncForTests(
                        server.Url("big.bin"), destination, null, CancellationToken.None,
                        Sha256Hex(LargeBody), byteProgress).GetAwaiter().GetResult();

                    Program.Equal(LargeBody.Length, (int)new FileInfo(destination).Length, "回退后文件长度应正确");
                    Program.Equal(Sha256Hex(LargeBody), UpdateChecker.ComputeSha256(destination), "回退后内容应一致");
                    Program.True(maxProgress <= LargeBody.Length, "进度不得超过文件总大小");
                }
                finally
                {
                    Delete(destination);
                }
            }
        }

        private static void CorrectRangeSucceeds()
        {
            var rangeRequests = 0;
            using (var server = new LocalHttpServer(delegate(HttpRequestData request)
            {
                if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return HttpResponseData.Ok(LargeBody, "application/octet-stream");
                }

                long start;
                long end;
                if (!TryGetRange(request, out start, out end))
                {
                    return HttpResponseData.Ok(LargeBody, "application/octet-stream");
                }

                Interlocked.Increment(ref rangeRequests);
                return Slice(LargeBody, start, end);
            }))
            {
                var destination = NewTempFile();
                try
                {
                    UpdateChecker.DownloadAsyncForTests(
                        server.Url("big.bin"), destination, null, CancellationToken.None,
                        Sha256Hex(LargeBody)).GetAwaiter().GetResult();

                    Program.Equal(Sha256Hex(LargeBody), UpdateChecker.ComputeSha256(destination), "分块内容应一致");
                    Program.True(rangeRequests >= 2, "10MB 文件应至少产生两个分块请求");
                }
                finally
                {
                    Delete(destination);
                }
            }
        }

        private static void RetryDoesNotOvercount()
        {
            var getCount = 0;
            using (var server = new LocalHttpServer(delegate(HttpRequestData request)
            {
                if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return HttpResponseData.Ok(SmallBody, "application/octet-stream");
                }

                var attempt = Interlocked.Increment(ref getCount);
                if (attempt == 1)
                {
                    // 第一次响应声明完整长度，但只写入一半后断开，触发分块重试
                    var half = new byte[SmallBody.Length / 2];
                    Buffer.BlockCopy(SmallBody, 0, half, 0, half.Length);
                    var truncated = HttpResponseData.Partial(half, 0, SmallBody.Length - 1, SmallBody.Length);
                    truncated.Headers["Content-Length"] = SmallBody.Length.ToString();
                    return truncated;
                }

                return Slice(SmallBody, 0, SmallBody.Length - 1);
            }))
            {
                var destination = NewTempFile();
                long maxProgress = 0;
                var byteProgress = new InlineProgress<Tuple<long, long>>(delegate(Tuple<long, long> value)
                {
                    if (value.Item1 > maxProgress) maxProgress = value.Item1;
                });

                try
                {
                    UpdateChecker.DownloadAsyncForTests(
                        server.Url("retry.bin"), destination, null, CancellationToken.None,
                        Sha256Hex(SmallBody), byteProgress).GetAwaiter().GetResult();

                    Program.Equal(Sha256Hex(SmallBody), UpdateChecker.ComputeSha256(destination), "重试后内容应一致");
                    Program.True(maxProgress <= SmallBody.Length, "重试不得让进度超过总大小");
                }
                finally
                {
                    Delete(destination);
                }
            }
        }

        private static void HeadFailureFallsBack()
        {
            using (var server = new LocalHttpServer(delegate(HttpRequestData request)
            {
                if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return HttpResponseData.Status(405, "Method Not Allowed");
                }
                return HttpResponseData.Ok(SmallBody, "application/octet-stream");
            }))
            {
                var destination = NewTempFile();
                try
                {
                    UpdateChecker.DownloadAsyncForTests(
                        server.Url("small.bin"), destination, null, CancellationToken.None,
                        Sha256Hex(SmallBody)).GetAwaiter().GetResult();
                    Program.Equal(Sha256Hex(SmallBody), UpdateChecker.ComputeSha256(destination), "HEAD 失败后应回退成功");
                }
                finally
                {
                    Delete(destination);
                }
            }
        }

        private static void WrongShaFails()
        {
            using (var server = new LocalHttpServer(delegate(HttpRequestData request)
            {
                if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return HttpResponseData.Ok(SmallBody, "application/octet-stream");
                }
                return Slice(SmallBody, 0, SmallBody.Length - 1);
            }))
            {
                var destination = NewTempFile();
                try
                {
                    var failed = false;
                    try
                    {
                        UpdateChecker.DownloadAsyncForTests(
                            server.Url("small.bin"), destination, null, CancellationToken.None,
                            new string('0', 64)).GetAwaiter().GetResult();
                    }
                    catch (InvalidDataException)
                    {
                        failed = true;
                    }
                    Program.True(failed, "错误 SHA256 必须抛出 InvalidDataException");
                }
                finally
                {
                    Delete(destination);
                }
            }
        }

        private static void RedirectLoopFails()
        {
            LocalHttpServer server = null;
            server = new LocalHttpServer(delegate
            {
                return HttpResponseData.Redirect(server.Url("loop"));
            });

            using (server)
            {
                var destination = NewTempFile();
                try
                {
                    var failed = false;
                    try
                    {
                        UpdateChecker.DownloadAsyncForTests(
                            server.Url("loop"), destination, null, CancellationToken.None,
                            null).GetAwaiter().GetResult();
                    }
                    catch (Exception)
                    {
                        failed = true;
                    }
                    Program.True(failed, "重定向循环必须失败");
                }
                finally
                {
                    Delete(destination);
                }
            }
        }

        private static void InstallerHeaderValidation()
        {
            var text = NewTempFile();
            var executable = NewTempFile();
            try
            {
                File.WriteAllText(text, new string('A', 2048));
                var rejected = false;
                try
                {
                    UpdateChecker.VerifyInstallerFile(text);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Program.True(rejected, "非 MZ 文件必须被拒绝");

                var bytes = new byte[2048];
                bytes[0] = (byte)'M';
                bytes[1] = (byte)'Z';
                File.WriteAllBytes(executable, bytes);
                UpdateChecker.VerifyInstallerFile(executable);
            }
            finally
            {
                Delete(text);
                Delete(executable);
            }
        }

        private static bool TryGetRange(HttpRequestData request, out long start, out long end)
        {
            start = 0;
            end = 0;
            string value;
            if (!request.Headers.TryGetValue("Range", out value) || string.IsNullOrEmpty(value))
            {
                return false;
            }
            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var parts = value.Substring(6).Split('-');
            return parts.Length >= 2
                && long.TryParse(parts[0], out start)
                && long.TryParse(parts[1], out end);
        }

        private static HttpResponseData Slice(byte[] body, long start, long end)
        {
            var length = (int)(end - start + 1);
            var slice = new byte[length];
            Buffer.BlockCopy(body, (int)start, slice, 0, length);
            return HttpResponseData.Partial(slice, start, end, body.Length);
        }

        private static byte[] CreateBody(int length)
        {
            var body = new byte[length];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 251);
            }
            return body;
        }

        private static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string NewTempFile()
        {
            return Path.Combine(Path.GetTempPath(), "uc_download_test_" + Guid.NewGuid().ToString("N") + ".bin");
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 测试清理失败不影响断言结果
            }
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public InlineProgress(Action<T> report)
            {
                _report = report;
            }

            public void Report(T value)
            {
                _report(value);
            }
        }
    }
}
