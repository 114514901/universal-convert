using System;
using System.Collections.Generic;
using System.Reflection;
using UniversalConvert.Core.Models;
using UniversalConvert.Plugin.FFmpeg;

namespace UniversalConvert.Tests
{
    /// <summary>元数据清除级别与 FFmpeg 参数映射。</summary>
    internal static class MetadataTests
    {
        public static void RunAll()
        {
            Program.Run("元数据：级别解析与旧选项兼容", ParseModes);
            Program.Run("FFmpeg：清除标签但保留封面", StripTagsKeepsCover);
            Program.Run("FFmpeg：清除标签与封面", StripAllDropsCover);
            Program.Run("FFmpeg：保留模式不添加清除参数", KeepMode);
            Program.Run("FFmpeg：旧 noCoverArt 只丢弃封面", LegacyNoCover);
        }

        private static void ParseModes()
        {
            Program.Equal(MetadataMode.Keep, MetadataPolicy.Parse(null), "空值应为 Keep");
            Program.Equal(MetadataMode.Keep, MetadataPolicy.Parse("other"), "未知值应为 Keep");
            Program.Equal(MetadataMode.Tags, MetadataPolicy.Parse("tags"), "tags 应解析");
            Program.Equal(MetadataMode.All, MetadataPolicy.Parse("ALL"), "all 应大小写不敏感");

            var legacy = new Dictionary<string, string> { { "noCoverArt", "true" } };
            Program.Equal(MetadataMode.Keep, MetadataPolicy.Resolve(legacy), "旧 noCoverArt 不应升级为全清除");
            Program.True(MetadataPolicy.HasLegacyNoCoverArt(legacy), "应识别旧 noCoverArt");
        }

        private static void StripTagsKeepsCover()
        {
            var args = BuildArgs("tags");
            Program.True(args.Contains("-map_metadata -1"), "应清除全局标签");
            Program.True(args.Contains("-map 0:v?"), "应保留封面流映射");
            Program.True(!args.Contains(" -vn"), "tags 模式不得丢弃封面");
        }

        private static void StripAllDropsCover()
        {
            var args = BuildArgs("all");
            Program.True(args.Contains("-map_metadata -1"), "应清除全局标签");
            Program.True(args.Contains(" -vn"), "all 模式应丢弃封面");
        }

        private static void KeepMode()
        {
            var args = BuildArgs("keep");
            Program.True(!args.Contains("-map_metadata -1"), "keep 不应清除标签");
            Program.True(args.Contains("-map 0:v?"), "keep 应保留封面流映射");
        }

        private static void LegacyNoCover()
        {
            var args = BuildArgs(null, true);
            Program.True(args.Contains(" -vn"), "旧 noCoverArt 应丢弃封面");
            Program.True(!args.Contains("-map_metadata -1"), "旧 noCoverArt 不应清除标签");
        }

        private static string BuildArgs(string metadataMode, bool legacyNoCover = false)
        {
            var plugin = new FFmpegPlugin();
            var request = new ConversionRequest
            {
                InputPath = @"C:\Media\sample.flac",
                OutputPath = @"C:\Media\sample.mp3",
                InputExtension = ".flac",
                OutputExtension = ".mp3"
            };
            if (metadataMode != null) request.Options[MetadataPolicy.OptionKey] = metadataMode;
            if (legacyNoCover) request.Options["noCoverArt"] = "true";

            var method = typeof(FFmpegPlugin).GetMethod(
                "BuildArguments", BindingFlags.Instance | BindingFlags.NonPublic);
            Program.True(method != null, "应找到 BuildArguments");
            return (string)method.Invoke(plugin, new object[] { request, request.OutputPath });
        }
    }
}
