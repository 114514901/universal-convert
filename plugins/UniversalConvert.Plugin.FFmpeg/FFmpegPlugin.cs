using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Plugin.FFmpeg
{
    /// <summary>
    /// 参考插件：基于 FFmpeg 的音视频转换。
    /// 展示完整扩展点：能力声明 + 参数 schema + 命名预设 + 命令行映射 + 进度解析。
    /// </summary>
    public sealed class FFmpegPlugin : ExternalToolConverterBase
    {
        public override string Id => "com.universalconvert.ffmpeg";
        public override string Name => "FFmpeg";
        public override string Description => "音视频/图片转换，基于 FFmpeg";
        public override string Version => "1.0.0";
        protected override string ToolName => "ffmpeg";

        private static readonly string[] VideoInputs =
            { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".wmv", ".m4v", ".ts" };
        private static readonly string[] VideoOutputs =
            { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".gif" };
        private static readonly string[] AudioInputs =
            { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a", ".wma", ".opus" };
        private static readonly string[] AudioOutputs =
            { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a", ".opus" };
        private static readonly string[] ImageInputs =
            { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff", ".tif" };
        private static readonly string[] ImageOutputs =
            { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff", ".tif" };

        public override IList<ConversionCapability> GetCapabilities()
        {
            var caps = new List<ConversionCapability>();

            foreach (var video in VideoInputs)
            {
                var outputs = new List<OutputFormat>();
                foreach (var v in VideoOutputs)
                {
                    outputs.Add(v == ".gif" ? GifOutput() : VideoOutput(v));
                }
                foreach (var a in AudioOutputs)
                {
                    outputs.Add(AudioOutput(a));
                }

                caps.Add(new ConversionCapability
                {
                    InputExtension = video,
                    InputDisplayName = video.TrimStart('.').ToUpperInvariant() + " 视频",
                    Outputs = outputs
                });
            }

            foreach (var audio in AudioInputs)
            {
                caps.Add(new ConversionCapability
                {
                    InputExtension = audio,
                    InputDisplayName = audio.TrimStart('.').ToUpperInvariant() + " 音频",
                    Outputs = AudioOutputs.Select(AudioOutput).ToList()
                });
            }

            foreach (var image in ImageInputs)
            {
                var outputs = ImageOutputs
                    .Where(o => !string.Equals(o, image, StringComparison.OrdinalIgnoreCase))
                    .Select(ImageOutput)
                    .ToList();

                caps.Add(new ConversionCapability
                {
                    InputExtension = image,
                    InputDisplayName = image.TrimStart('.').ToUpperInvariant() + " 图片",
                    Outputs = outputs
                });
            }

            return caps;
        }

        protected override string BuildArguments(ConversionRequest request, string outputPath)
        {
            var sb = new StringBuilder();
            sb.Append("-y -hide_banner -loglevel info -i ").Append(ProcessRunner.Quote(request.InputPath));

            var outExt = request.OutputExtension ?? string.Empty;
            if (!outExt.StartsWith(".")) outExt = "." + outExt;

            if (ImageOutputs.Contains(outExt))
            {
                // 图片输出：直接转格式，不加 map/vn
            }
            else if (AudioOutputs.Contains(outExt))
            {
                // 音频输出：丢弃视频流
                sb.Append(" -vn");
            }
            else
            {
                sb.Append(" -map 0");
            }

            string value;
            if (TryGet(request.Options, "videoCodec", out value))
                sb.Append(" -c:v ").Append(value);
            if (TryGet(request.Options, "videoBitrate", out value))
                sb.Append(" -b:v ").Append(value);
            if (TryGet(request.Options, "audioBitrate", out value))
                sb.Append(" -b:a ").Append(value);
            if (TryGet(request.Options, "sampleRate", out value))
                sb.Append(" -ar ").Append(value);
            if (TryGet(request.Options, "fps", out value))
                sb.Append(" -r ").Append(value);
            if (TryGet(request.Options, "scale", out value))
                sb.Append(" -vf scale=").Append(value);

            sb.Append(' ').Append(ProcessRunner.Quote(outputPath));
            return sb.ToString();
        }

        protected override void OnOutputLine(string line, ConversionRequest request, IProgress<ConversionProgress> progress)
        {
            if (line == null || progress == null) return;

            var duration = ParseTime(line, @"Duration:\s*(\d+):(\d+):(\d+)");
            if (duration.HasValue)
            {
                progress.Report(new ConversionProgress(ConversionStage.Running, 0, "已获取时长，转换中..."));
            }

            var current = ParseTime(line, @"time=(\d+):(\d+):(\d+)");
            if (current.HasValue)
            {
                var pct = duration.HasValue && duration.Value.TotalSeconds > 0
                    ? Math.Min(100, current.Value.TotalSeconds / duration.Value.TotalSeconds * 100)
                    : -1;
                progress.Report(new ConversionProgress(ConversionStage.Running, pct, "转换中..."));
            }
        }

        private static bool TryGet(IDictionary<string, string> options, string key, out string value)
        {
            value = null;
            return options != null
                && options.TryGetValue(key, out value)
                && !string.IsNullOrEmpty(value);
        }

        private static TimeSpan? ParseTime(string line, string pattern)
        {
            var match = Regex.Match(line, pattern);
            if (!match.Success) return null;

            int h = int.Parse(match.Groups[1].Value);
            int m = int.Parse(match.Groups[2].Value);
            int s = int.Parse(match.Groups[3].Value);
            return new TimeSpan(h, m, s);
        }

        // ---- 参数/预设构造辅助 ----

        private static OptionDefinition EnumOption(string key, string label, string defaultValue, params OptionChoice[] choices)
        {
            return new OptionDefinition
            {
                Key = key,
                Label = label,
                Type = OptionType.Enum,
                DefaultValue = defaultValue,
                Choices = choices.ToList()
            };
        }

        private static OptionChoice Choice(string value, string label)
        {
            return new OptionChoice { Value = value, Label = label };
        }

        private static ConversionPreset Preset(string name, params string[] keyValues)
        {
            var preset = new ConversionPreset { Name = name };
            foreach (var kv in keyValues)
            {
                var i = kv.IndexOf('=');
                if (i > 0)
                {
                    preset.Options[kv.Substring(0, i)] = kv.Substring(i + 1);
                }
            }
            return preset;
        }

        private static OutputFormat VideoOutput(string ext)
        {
            return new OutputFormat
            {
                Extension = ext,
                DisplayName = ext.TrimStart('.').ToUpperInvariant(),
                Options = new List<OptionDefinition>
                {
                    EnumOption("videoCodec", "视频编码", "",
                        Choice("", "原始"),
                        Choice("libx264", "H.264 (libx264)"),
                        Choice("libx265", "H.265 (libx265)"),
                        Choice("libvpx-vp9", "VP9 (libvpx-vp9)"),
                        Choice("mpeg4", "MPEG-4")),
                    EnumOption("scale", "分辨率", "",
                        Choice("", "原始"),
                        Choice("3840:2160", "4K (3840×2160)"),
                        Choice("2560:1440", "2K (2560×1440)"),
                        Choice("1920:1080", "1080p (1920×1080)"),
                        Choice("1280:720", "720p (1280×720)"),
                        Choice("854:480", "480p (854×480)"),
                        Choice("640:360", "360p (640×360)")),
                    EnumOption("videoBitrate", "视频码率", "",
                        Choice("", "原始"),
                        Choice("500k", "500 kbps"),
                        Choice("1000k", "1000 kbps"),
                        Choice("2000k", "2000 kbps"),
                        Choice("4000k", "4000 kbps"),
                        Choice("8000k", "8000 kbps"),
                        Choice("12000k", "12000 kbps"),
                        Choice("20000k", "20000 kbps")),
                    EnumOption("fps", "帧率", "",
                        Choice("", "原始"),
                        Choice("24", "24 fps"),
                        Choice("25", "25 fps"),
                        Choice("30", "30 fps"),
                        Choice("48", "48 fps"),
                        Choice("60", "60 fps"),
                        Choice("120", "120 fps"))
                },
                Presets = new List<ConversionPreset>
                {
                    Preset("高清 1080p", "scale=1920:1080", "videoBitrate=4000k"),
                    Preset("标清 720p", "scale=1280:720", "videoBitrate=2000k"),
                    Preset("流畅 480p", "scale=854:480", "videoBitrate=1000k")
                }
            };
        }

        private static OutputFormat AudioOutput(string ext)
        {
            return new OutputFormat
            {
                Extension = ext,
                DisplayName = ext.TrimStart('.').ToUpperInvariant(),
                Options = new List<OptionDefinition>
                {
                    EnumOption("audioBitrate", "音频码率", "192k",
                        Choice("", "原始"),
                        Choice("96k", "96 kbps"),
                        Choice("128k", "128 kbps"),
                        Choice("160k", "160 kbps"),
                        Choice("192k", "192 kbps"),
                        Choice("256k", "256 kbps"),
                        Choice("320k", "320 kbps")),
                    EnumOption("sampleRate", "采样率", "",
                        Choice("", "原始"),
                        Choice("44100", "44100 Hz"),
                        Choice("48000", "48000 Hz"),
                        Choice("88200", "88200 Hz"),
                        Choice("96000", "96000 Hz"))
                },
                Presets = new List<ConversionPreset>
                {
                    Preset("320 kbps", "audioBitrate=320k"),
                    Preset("192 kbps", "audioBitrate=192k"),
                    Preset("128 kbps", "audioBitrate=128k")
                }
            };
        }

        private static OutputFormat ImageOutput(string ext)
        {
            return new OutputFormat
            {
                Extension = ext,
                DisplayName = ext.TrimStart('.').ToUpperInvariant()
            };
        }

        private static OutputFormat GifOutput()
        {
            // 只有参数、无预设：演示"仅可自定义"的格式（右键菜单里显示 默认 + 更多设置）
            return new OutputFormat
            {
                Extension = ".gif",
                DisplayName = "GIF",
                Options = new List<OptionDefinition>
                {
                    EnumOption("scale", "尺寸", "",
                        Choice("", "原始"),
                        Choice("320:240", "320×240"),
                        Choice("480:320", "480×320"),
                        Choice("640:480", "640×480")),
                    EnumOption("fps", "帧率", "10",
                        Choice("8", "8 fps"),
                        Choice("10", "10 fps"),
                        Choice("15", "15 fps"))
                }
            };
        }
    }
}
