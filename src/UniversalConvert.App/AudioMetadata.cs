using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace UniversalConvert.App
{
    public sealed class AudioStreamInfo
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitDepth { get; set; }
    }

    /// <summary>分包码率时间线：根据当前播放时间估算实时（VBR）码率。</summary>
    public sealed class BitrateTimeline
    {
        private readonly List<double> _times = new List<double>();
        private readonly List<long> _cumBytes = new List<long>();

        public bool IsValid => _times.Count > 0;

        public void Add(double time, long cumulativeBytes)
        {
            _times.Add(time);
            _cumBytes.Add(cumulativeBytes);
        }

        public int GetBitrateKbps(double seconds)
        {
            if (!IsValid) return 0;

            int end = FindIndex(seconds);
            if (end < 0) return 0;

            int start = FindIndex(seconds - 1.0);
            if (start < 0) start = 0;

            double dt = _times[end] - _times[start];
            long db = _cumBytes[end] - _cumBytes[start];
            if (dt <= 0) return 0;

            return (int)(db * 8.0 / dt / 1000.0);
        }

        private int FindIndex(double seconds)
        {
            int lo = 0, hi = _times.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_times[mid] <= seconds) { ans = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return ans;
        }
    }

    /// <summary>通过 ffprobe（随安装包分发的 tools\ffprobe.exe）读取音频流信息与分包码率。</summary>
    public static class AudioMetadataReader
    {
        public static string FindFfprobe()
        {
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffprobe.exe");
            if (File.Exists(local)) return local;

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), "ffprobe.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static string FindFfmpeg()
        {
            // 仅用随包 ffmpeg；不查系统 PATH（PATH 中可能是旧版/带漏洞版本，
            // 如 ffmpeg < 8.1.2 的 CVE-2026-8461 越界写入，静默使用有安全风险）
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe");
            if (File.Exists(local)) return local;
            return null;
        }

        public static AudioStreamInfo ReadStreamInfo(string ffprobe, string input)
        {
            var json = Run(ffprobe,
                "-v error -select_streams a:0 -show_entries stream=sample_rate,channels,bits_per_sample -of json "
                + Quote(input));
            if (string.IsNullOrEmpty(json)) return null;

            var obj = JObject.Parse(json);
            var streams = obj["streams"] as JArray;
            if (streams == null || streams.Count == 0) return null;
            var stream = streams[0] as JObject;

            var info = new AudioStreamInfo();
            int.TryParse(stream["sample_rate"]?.ToString(), out var sampleRate);
            int.TryParse(stream["channels"]?.ToString(), out var channels);
            int.TryParse(stream["bits_per_sample"]?.ToString(), out var bitDepth);
            info.SampleRate = sampleRate;
            info.Channels = channels;
            info.BitDepth = bitDepth;
            return info;
        }

        public static BitrateTimeline ReadBitrateTimeline(string ffprobe, string input)
        {
            var json = Run(ffprobe,
                "-v error -select_streams a:0 -show_entries packet=pts_time,size -of json "
                + Quote(input));
            if (string.IsNullOrEmpty(json)) return null;

            var obj = JObject.Parse(json);
            var packets = obj["packets"] as JArray;
            if (packets == null || packets.Count == 0) return null;

            var timeline = new BitrateTimeline();
            long cumBytes = 0;
            foreach (var packet in packets)
            {
                double time;
                long size;
                if (!double.TryParse(packet["pts_time"]?.ToString(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out time)) continue;
                if (!long.TryParse(packet["size"]?.ToString(), out size)) continue;

                cumBytes += size;
                timeline.Add(time, cumBytes);
            }
            return timeline;
        }

        private static string Run(string ffprobe, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    if (process == null) return null;
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }
    }
}
