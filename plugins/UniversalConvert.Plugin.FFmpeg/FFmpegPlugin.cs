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
            { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a", ".opus", ".wma" };
        private static readonly string[] ImageInputs =
            { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff", ".tif", ".heic", ".heif", ".avif" };
        private static readonly string[] ImageOutputs =
            { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff", ".tif", ".heic", ".heif", ".avif" };

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
            else if (outExt == ".gif")
            {
                // GIF 无音频：只取第一个视频流，丢弃音频
                sb.Append(" -map 0:v:0");
            }
            else if (AudioOutputs.Contains(outExt))
            {
                // 音频输出：目标容器支持封面（attached pic）时保留封面（mjpeg 原样 copy），
                // 否则丢弃视频流（封面会被当成普通视频流转码进不支持的容器导致失败，如 mp3→m4a）。
                if (SupportsCoverArt(outExt))
                {
                    sb.Append(" -map 0:a? -map 0:v? -c:v copy");
                }
                else
                {
                    sb.Append(" -vn");
                }
            }
            else
            {
                sb.Append(" -map 0");
            }

            string value;
            if (TryGet(request.Options, "videoCodec", out value))
                sb.Append(" -c:v ").Append(value);
            if (TryGet(request.Options, "videoBitrate", out value))
                sb.Append(" -b:v ").Append(NormalizeBitrate(value));
            if (TryGet(request.Options, "audioBitrate", out value))
                sb.Append(" -b:a ").Append(NormalizeBitrate(value));
            if (TryGet(request.Options, "sampleRate", out value))
                sb.Append(" -ar ").Append(NormalizeSampleRate(value));
            if (TryGet(request.Options, "fps", out value))
                sb.Append(" -r ").Append(value);
            if (TryGet(request.Options, "scale", out value))
                sb.Append(" -vf scale=").Append(value);
            if (TryGet(request.Options, "crf", out value))
                sb.Append(" -crf ").Append(value);
            if (TryGet(request.Options, "preset", out value))
                sb.Append(" -preset ").Append(value);
            if (TryGet(request.Options, "audioChannels", out value))
                sb.Append(" -ac ").Append(value);
            if (TryGet(request.Options, "audioCodec", out value))
                sb.Append(" -c:a ").Append(value);

            // 视频滤镜（-vf）
            if (TryGet(request.Options, "videoFilter", out value))
            {
                string args;
                TryGet(request.Options, "videoFilterArgs", out args);
                var vf = BuildVideoFilter(value, args);
                if (!string.IsNullOrEmpty(vf))
                {
                    sb.Append(" -vf ").Append(ProcessRunner.Quote(vf));
                }
            }

            // 音频滤镜（-af）
            if (TryGet(request.Options, "audioFilter", out value))
            {
                string args;
                TryGet(request.Options, "audioFilterArgs", out args);
                var af = BuildAudioFilter(value, args);
                if (!string.IsNullOrEmpty(af))
                {
                    sb.Append(" -af ").Append(ProcessRunner.Quote(af));
                }
            }

            // 高级参数：原样拼接（放末尾，用户参数覆盖内置）
            if (TryGet(request.Options, "extraArgs", out value))
            {
                sb.Append(' ').Append(value);
            }

            sb.Append(' ').Append(ProcessRunner.Quote(outputPath));
            return sb.ToString();
        }

        /// <summary>目标音频容器是否支持嵌入封面（attached pic）：mp3/m4a/flac 支持，其余（ogg/opus/aac/wma 等）不支持。</summary>
        private static bool SupportsCoverArt(string outExt)
        {
            return outExt == ".mp3" || outExt == ".m4a" || outExt == ".flac";
        }

        /// <summary>
        /// 归一化码率参数：预设显示可读单位（如 "320 kbps"），用户自定义可能照抄带单位；
        /// FFmpeg 实际需要 "320k"。提取数字补 "k"；无法识别则原样返回（由 FFmpeg 报错提示）。
        /// </summary>
        private static string NormalizeBitrate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var m = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"(\d+(?:\.\d+)?)");
            if (!m.Success) return value;
            return m.Groups[1].Value + "k";
        }

        /// <summary>
        /// 归一化采样率参数：预设显示 "44.1 kHz"，FFmpeg 需要 Hz（44100）。
        /// 识别 "44.1 kHz" / "44.1k" / "44100" / "48k" → 转整数 Hz；无法识别则原样返回。
        /// </summary>
        private static string NormalizeSampleRate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var m = System.Text.RegularExpressions.Regex.Match(
                value.Trim(), @"(\d+(?:\.\d+)?)\s*(k|khz|hz)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return value;
            double num;
            if (!double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out num)) return value;
            var unit = (m.Groups[2].Value ?? string.Empty).ToLowerInvariant();
            if (unit.StartsWith("k")) num *= 1000.0; // kHz → Hz
            return ((int)Math.Round(num)).ToString();
        }

        /// <summary>视频滤镜 → FFmpeg -vf 表达式。custom 直接透传 args。</summary>
        private static string BuildVideoFilter(string filter, string args)
        {
            switch (filter)
            {
                case "scale": return "scale=" + (args ?? "");
                case "crop": return "crop=" + (args ?? "");
                case "hflip": return "hflip";
                case "vflip": return "vflip";
                case "eq": return "eq=" + (args ?? "");
                case "hue": return "hue=" + (args ?? "");
                case "custom": return args;
                default: return null;
            }
        }

        /// <summary>音频滤镜 → FFmpeg -af 表达式。custom 直接透传 args。</summary>
        private static string BuildAudioFilter(string filter, string args)
        {
            switch (filter)
            {
                case "volume": return "volume=" + (args ?? "");
                case "equalizer": return "equalizer=" + (args ?? "");
                case "atempo": return "atempo=" + (args ?? "");
                case "custom": return args;
                default: return null;
            }
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

        private static OptionDefinition EnumOption(string key, string label, string defaultValue, string alias = null, params OptionChoice[] choices)
        {
            return new OptionDefinition
            {
                Key = key,
                Label = label,
                Type = OptionType.Enum,
                DefaultValue = defaultValue,
                Choices = choices.ToList(),
                AdvancedAlias = alias
            };
        }

        private static OptionDefinition StringOption(string key, string label, string defaultValue, string alias = null, bool advancedEntry = false)
        {
            return new OptionDefinition
            {
                Key = key,
                Label = label,
                Type = OptionType.String,
                DefaultValue = defaultValue,
                AdvancedAlias = alias,
                IsAdvancedEntry = advancedEntry
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
                    EnumOption("videoCodec", "@ParamVideoCodec", "", "-c:v",
                        Choice("", "@Original"),
                        Choice("libx264", "H.264 (libx264)"),
                        Choice("libx265", "H.265 (libx265)"),
                        Choice("libvpx-vp9", "VP9 (libvpx-vp9)"),
                        Choice("mpeg4", "MPEG-4")),
                    EnumOption("scale", "@ParamScale", "",
                        Choice("", "@Original"),
                        Choice("3840:2160", "4K (3840×2160)"),
                        Choice("2560:1440", "2K (2560×1440)"),
                        Choice("1920:1080", "1080p (1920×1080)"),
                        Choice("1280:720", "720p (1280×720)"),
                        Choice("854:480", "480p (854×480)"),
                        Choice("640:360", "360p (640×360)")),
                    EnumOption("videoBitrate", "@ParamVideoBitrate", "", "-b:v",
                        Choice("", "@Original"),
                        Choice("500k", "500 kbps"),
                        Choice("1000k", "1000 kbps"),
                        Choice("2000k", "2000 kbps"),
                        Choice("4000k", "4000 kbps"),
                        Choice("8000k", "8000 kbps"),
                        Choice("12000k", "12000 kbps"),
                        Choice("20000k", "20000 kbps")),
                    EnumOption("fps", "@ParamFps", "", "-r",
                        Choice("", "@Original"),
                        Choice("24", "24 fps"),
                        Choice("25", "25 fps"),
                        Choice("30", "30 fps"),
                        Choice("48", "48 fps"),
                        Choice("60", "60 fps"),
                        Choice("120", "120 fps")),
                    EnumOption("videoFilter", "@ParamVideoFilter", "",
                        Choice("", "@Original"),
                        Choice("scale", "@FilterScale"),
                        Choice("crop", "@FilterCrop"),
                        Choice("hflip", "@FilterHFlip"),
                        Choice("vflip", "@FilterVFlip"),
                        Choice("eq", "@FilterEq"),
                        Choice("hue", "@FilterHue"),
                        Choice("custom", "@FilterCustom")),
                    StringOption("videoFilterArgs", "@ParamVideoFilterArgs", ""),
                    EnumOption("crf", "@ParamCrf", "", "-crf",
                        Choice("", "@Original"),
                        Choice("18", "18（高质量）"),
                        Choice("23", "23（默认）"),
                        Choice("28", "28（较小体积）")),
                    EnumOption("preset", "@ParamPreset", "", "-preset",
                        Choice("", "@Original"),
                        Choice("ultrafast", "ultrafast"),
                        Choice("veryfast", "veryfast"),
                        Choice("medium", "medium"),
                        Choice("slow", "slow"),
                        Choice("veryslow", "veryslow")),
                    StringOption("audioCodec", "@ParamAudioCodec", "", "-c:a"),
                    StringOption("extraArgs", "@ParamExtraArgs", "", null, true)
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
                    EnumOption("audioBitrate", "@ParamAudioBitrate", "192k", "-b:a",
                        Choice("", "@Original"),
                        Choice("96k", "96 kbps"),
                        Choice("128k", "128 kbps"),
                        Choice("160k", "160 kbps"),
                        Choice("192k", "192 kbps"),
                        Choice("256k", "256 kbps"),
                        Choice("320k", "320 kbps")),
                    EnumOption("sampleRate", "@ParamSampleRate", "", "-ar",
                        Choice("", "@Original"),
                        Choice("44100", "44100 Hz"),
                        Choice("48000", "48000 Hz"),
                        Choice("88200", "88200 Hz"),
                        Choice("96000", "96000 Hz")),
                    EnumOption("audioFilter", "@ParamAudioFilter", "",
                        Choice("", "@Original"),
                        Choice("volume", "@FilterVolume"),
                        Choice("equalizer", "@FilterEqualizer"),
                        Choice("atempo", "@FilterAtempo"),
                        Choice("custom", "@FilterCustom")),
                    StringOption("audioFilterArgs", "@ParamAudioFilterArgs", ""),
                    EnumOption("audioChannels", "@ParamAudioChannels", "", "-ac",
                        Choice("", "@Original"),
                        Choice("1", "@ChMono"),
                        Choice("2", "@ChStereo"),
                        Choice("6", "@Ch51")),
                    StringOption("audioCodec", "@ParamAudioCodec", "", "-c:a"),
                    StringOption("extraArgs", "@ParamExtraArgs", "", null, true)
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
                    EnumOption("scale", "@ParamSize", "",
                        Choice("", "@Original"),
                        Choice("320:240", "320×240"),
                        Choice("480:320", "480×320"),
                        Choice("640:480", "640×480")),
                    EnumOption("fps", "@ParamFps", "10", "-r",
                        Choice("8", "8 fps"),
                        Choice("10", "10 fps"),
                        Choice("15", "15 fps")),
                    StringOption("extraArgs", "@ParamExtraArgs", "", null, true)
                }
            };
        }
    }
}
