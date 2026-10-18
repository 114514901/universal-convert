using System;
using System.IO;
using System.Linq;
using UniversalConvert.Cli;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Tests
{
    /// <summary>命令行输出、冲突策略与时间节流的回归测试。</summary>
    internal static class CliTests
    {
        public static void RunAll()
        {
            Program.Run("CLI：--skip 与冲突策略", SkipAndConflict);
            Program.Run("CLI：formats 的 --plugin 过滤参数", FormatsPlugin);
            Program.Run("CLI：--version 实际输出", VersionOutput);
            Program.Run("CLI：Core 版本一致", CoreVersionConsistent);
            Program.Run("CLI：JSON 保留中文原文", JsonKeepsChinese);
            Program.Run("CLI：JSON progress 按时间节流", JsonProgressThrottle);
            Program.Run("CLI：summary 包含总字节与平均速度", SummaryMetrics);
            Program.Run("CLI：精确占位不追加序号", ExactReservation);
        }

        private static void SkipAndConflict()
        {
            var options = CliOptions.Parse(new[] { "convert", "a.mp4", "--to", "mp3", "--skip" });
            Program.Equal(CliCommand.Convert, options.Command, "应识别 convert");
            Program.True(options.Skip, "--skip 应生效");
            Program.Equal(0, options.Errors.Count, "--skip 不应产生错误");

            var conflict = CliOptions.Parse(new[]
            {
                "convert", "a.mp4", "--to", "mp3", "--force", "--skip"
            });
            Program.True(conflict.Errors.Count > 0, "--force 与 --skip 应报告冲突");

            Program.True(CliText.Help.Contains("--skip"), "帮助应说明 --skip");
            Program.True(CliText.Help.Contains("默认（rename）"), "帮助应说明默认改名策略");
        }

        private static void FormatsPlugin()
        {
            var options = CliOptions.Parse(new[]
            {
                "formats", "--input", "mp4", "--plugin", "com.example.plugin", "--json"
            });
            Program.Equal(CliCommand.Formats, options.Command, "应识别 formats");
            Program.Equal("mp4", options.InputExtension, "--input 应被解析");
            Program.Equal("com.example.plugin", options.PluginId, "--plugin 应被解析");
            Program.True(options.Json, "--json 应生效");
            Program.Equal(0, options.Errors.Count, "不应产生错误");
        }

        private static void VersionOutput()
        {
            var text = Capture(delegate
            {
                var code = new CliRunner(CliOptions.Parse(new[] { "--version" })).Run();
                Program.Equal(0, code, "--version 应返回成功");
            });
            Program.True(text.Contains("UniversalConvert CLI"), "应输出版本行");
            Program.True(text.Contains("UniversalConvert Core"), "应输出 Core 版本行");
            Program.True(text.Trim().Length > 0, "版本输出不能为空");
        }

        private static void CoreVersionConsistent()
        {
            var error = CliCompatibility.Check();
            Program.True(string.IsNullOrEmpty(error), error ?? "CLI 与 Core 版本应一致");
            Program.True(!string.IsNullOrEmpty(CliCompatibility.GetCoreVersion()), "应能读取 Core 版本");
        }

        private static void JsonKeepsChinese()
        {
            var text = Capture(delegate
            {
                var output = new CliOutput(true, false, false, CliProgressMode.Never);
                output.Plan(
                    "C:\\音乐\\示例.mp4",
                    "C:\\输出\\示例.mp3",
                    "示例插件",
                    "预设",
                    false,
                    true,
                    false,
                    "输出文件已存在");
            });

            Program.True(text.Contains("示例.mp4"), "JSON 应保留中文路径");
            Program.True(text.Contains("输出文件已存在"), "JSON 应保留中文原因");
            Program.True(!text.Contains("\\u"), "JSON 不应使用 Unicode 转义");
        }

        private static void JsonProgressThrottle()
        {
            var text = Capture(delegate
            {
                var output = new CliOutput(true, false, false, CliProgressMode.Never);
                for (var i = 0; i < 40; i++)
                {
                    output.Progress(
                        "C:\\示例.mp4", 1, 1,
                        new ConversionProgress(ConversionStage.Running, i * 2, "转换中"));
                }
                output.Progress(
                    "C:\\示例.mp4", 1, 1,
                    new ConversionProgress(ConversionStage.Completed, 100, "完成"));
            });

            var progressEvents = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains("\"event\":\"progress\""));
            Program.True(progressEvents <= 5, "40 次快速进度报告应被节流");
            Program.True(progressEvents >= 2, "应保留起始与完成事件");
        }

        private static void SummaryMetrics()
        {
            var text = Capture(delegate
            {
                new CliOutput(true, false, false, CliProgressMode.Never)
                    .Summary(2, 1, 0, 1, 2.0, 1000);
            });
            Program.True(text.Contains("\"totalBytes\":1000"), "summary 应包含总字节数");
            Program.True(text.Contains("\"averageBytesPerSecond\":500"), "summary 应包含平均速度");
            Program.True(text.Contains("\"durationMs\":2000"), "summary 应保留耗时");
        }

        private static void ExactReservation()
        {
            var root = Path.Combine(Path.GetTempPath(), "uc_cli_test_" + Guid.NewGuid().ToString("N"));
            var existing = Path.Combine(root, "existing.bin");
            var candidate = Path.Combine(root, "new.bin");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(existing, "x");
                Program.True(!OutputPathHelper.TryReserveExactPath(existing), "已存在的路径不得占位");
                Program.True(OutputPathHelper.TryReserveExactPath(candidate), "不存在的路径应占位成功");
                Program.True(File.Exists(candidate), "占位应创建文件");
                Program.True(!OutputPathHelper.TryReserveExactPath(candidate), "重复占位应失败");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static string Capture(Action action)
        {
            var original = Console.Out;
            using (var writer = new StringWriter())
            {
                Console.SetOut(writer);
                try
                {
                    action();
                }
                finally
                {
                    Console.SetOut(original);
                }
                return writer.ToString();
            }
        }
    }
}
