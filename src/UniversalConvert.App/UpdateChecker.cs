using System;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UniversalConvert.App
{
    public sealed class UpdateInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    /// 检查 GitHub Release 是否有新版本。
    /// 注意：私有仓库的 Release API 需要鉴权，匿名访问会失败并返回 null（即静默跳过）。
    /// 若要启用自动更新，仓库需设为公开，或在配置中提供 token。
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiUrl = "https://api.github.com/repos/114514901/universal-convert/releases/latest";

        public static async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "UniversalConvert");
                    var json = await client.DownloadStringTaskAsync(ApiUrl).ConfigureAwait(false);

                    var tag = Extract(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    var url = Extract(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                    if (string.IsNullOrEmpty(tag)) return null;

                    var latest = ParseVersion(tag);
                    var current = CurrentVersion();
                    if (latest == null || current == null || latest <= current) return null;

                    return new UpdateInfo { Version = tag, Url = url };
                }
            }
            catch
            {
                return null;
            }
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

        private static string Extract(string json, string pattern)
        {
            var m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
