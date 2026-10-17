using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalConvert.Tests
{
    internal sealed class HttpRequestData
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public IDictionary<string, string> Headers { get; private set; }

        public HttpRequestData()
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal sealed class HttpResponseData
    {
        public int StatusCode { get; set; }
        public string Reason { get; set; }
        public IDictionary<string, string> Headers { get; private set; }
        public byte[] Body { get; set; }

        public HttpResponseData()
        {
            StatusCode = 200;
            Reason = "OK";
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Body = new byte[0];
        }

        public static HttpResponseData Ok(byte[] body, string contentType)
        {
            var response = new HttpResponseData { Body = body ?? new byte[0] };
            response.Headers["Content-Type"] = contentType ?? "application/octet-stream";
            return response;
        }

        public static HttpResponseData Partial(byte[] body, long start, long end, long total)
        {
            var response = new HttpResponseData { StatusCode = 206, Reason = "Partial Content", Body = body };
            response.Headers["Content-Type"] = "application/octet-stream";
            response.Headers["Content-Range"] = "bytes " + start + "-" + end + "/" + total;
            return response;
        }

        public static HttpResponseData Redirect(string location)
        {
            var response = new HttpResponseData { StatusCode = 302, Reason = "Found" };
            response.Headers["Location"] = location;
            return response;
        }

        public static HttpResponseData Status(int code, string reason)
        {
            return new HttpResponseData { StatusCode = code, Reason = reason };
        }
    }

    /// <summary>仅用于测试的最小 HTTP 服务器，支持 HEAD/GET 与自定义响应。</summary>
    internal sealed class LocalHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<HttpRequestData, HttpResponseData> _handler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _acceptTask;

        public int Port { get; private set; }

        public LocalHttpServer(Func<HttpRequestData, HttpResponseData> handler)
        {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = Task.Run(async () => await AcceptLoopAsync().ConfigureAwait(false));
        }

        public string Url(string path)
        {
            return "http://127.0.0.1:" + Port + "/" + (path ?? string.Empty).TrimStart('/');
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
                var ignored = Task.Run(async () => await HandleAsync(client).ConfigureAwait(false));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(requestLine)) return;

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2) return;
                    var request = new HttpRequestData { Method = parts[0].ToUpperInvariant(), Path = parts[1] };
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                    {
                        var index = line.IndexOf(':');
                        if (index > 0)
                        {
                            request.Headers[line.Substring(0, index).Trim()] = line.Substring(index + 1).Trim();
                        }
                    }

                    HttpResponseData response;
                    try
                    {
                        response = _handler(request);
                    }
                    catch (Exception ex)
                    {
                        response = HttpResponseData.Status(500, "Internal Server Error");
                        response.Headers["X-Test-Error"] = ex.Message;
                    }

                    var body = response.Body ?? new byte[0];
                    var builder = new StringBuilder();
                    builder.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(response.Reason).Append("\r\n");
                    if (!response.Headers.ContainsKey("Content-Length"))
                    {
                        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                    }
                    foreach (var header in response.Headers)
                    {
                        builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                    }
                    builder.Append("Connection: close\r\n\r\n");

                    var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                    if (!string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase) && body.Length > 0)
                    {
                        await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    }
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // 测试服务器忽略连接异常
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            try { _acceptTask.Wait(1000); } catch { }
            _cts.Dispose();
        }
    }
}
