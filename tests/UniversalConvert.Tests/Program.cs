using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UniversalConvert.Cli;
using UniversalConvert.Core;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Plugins;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.WriteLine("UniversalConvert 命令行回归测试");
            Console.WriteLine();

            Run("契约：--convert 基本解析", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--convert", "a.mp4", "--to", "mp3" });
                True(parsed.IsConvertMode, "应识别 --convert");
                Equal("mp3", parsed.OutputExtension, "--to 应去除前导点");
                Equal(1, parsed.ExtraFiles.Length, "应保留一个输入文件");
                Equal("a.mp4", parsed.ExtraFiles[0], "输入文件应原样保留");
            });

            Run("契约：选项名不区分大小写", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--CONVERT", "a.mp4", "--TO", "MP3" });
                True(parsed.IsConvertMode, "应识别大写 --CONVERT");
                Equal("mp3", parsed.OutputExtension, "扩展名应规范化为小写");
            });

            Run("契约：不存在的文件仍作为输入保留", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--convert", "missing.mp4", "--to", "mp3" });
                Equal(1, parsed.ExtraFiles.Length, "语法解析不应做文件存在性过滤");
            });

            Run("契约：--customize 消费输入路径", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--customize", "a.mp4", "--to", "mp3" });
                True(parsed.IsCustomizeMode, "应识别 --customize");
                Equal("a.mp4", parsed.InputPath, "输入路径应被消费");
                Equal(0, parsed.ExtraFiles.Length, "输入路径不应重复进入 ExtraFiles");
            });

            Run("契约：--output-dir 与 --plugin", delegate
            {
                var parsed = CommandLineContract.Parse(new[]
                {
                    "--convert", "a.mp4", "--to", "mp3", "--output-dir", "out", "--plugin", "p1"
                });
                Equal("out", parsed.OutputDir, "--output-dir 应被解析");
                Equal("p1", parsed.PluginId, "--plugin 应被解析");
            });

            Run("契约：缺少参数值", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--convert", "a.mp4", "--to" });
                True(parsed.MissingOptionValues.Contains("--to"), "应记录缺少值的 --to");
            });

            Run("契约：未知选项", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--convert", "a.mp4", "--to", "mp3", "--ouput", "x" });
                True(parsed.UnknownOptions.Count == 1, "应记录未知选项");
                Equal("--ouput", parsed.UnknownOptions[0], "未知选项应原样记录");
            });

            Run("契约：-- 终止符", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--convert", "--to", "mp3", "--", "--x.mp4" });
                Equal(1, parsed.ExtraFiles.Length, "应保留终止符后的文件名");
                Equal("--x.mp4", parsed.ExtraFiles[0], "以短横线开头的文件名应保留");
            });

            Run("契约：内联值", delegate
            {
                var parsed = CommandLineContract.Parse(new[] { "--convert", "a.mp4", "--to=mp3" });
                True(parsed.UnknownOptions.Count == 0, "合法的内联值不应被记为未知选项");
                Equal("mp3", parsed.OutputExtension, "--to=mp3 应被解析");

                var invalid = CommandLineContract.Parse(new[] { "--convert=a.mp4" });
                True(invalid.UnknownOptions.Count == 1, "不接受值的选项若带内联值，应记为未知选项");
            });

            Run("契约：打开命令", delegate
            {
                var command = CommandLineContract.BuildOpenCommand(new[] { "a b.mp4", "c.mp4" });
                True(command.StartsWith("--open", StringComparison.Ordinal), "应包含 --open");
                True(command.Contains("\"a b.mp4\""), "含空格的路径应加引号");
            });

            Run("CLI：convert 基本选项", delegate
            {
                var options = CliOptions.Parse(new[]
                {
                    "convert", "a.mp4", "--to", "mp3", "--jobs", "4",
                    "-O", "audioBitrate=96k", "--preset", "320 kbps"
                });
                Equal(CliCommand.Convert, options.Command, "应识别 convert");
                Equal(1, options.Files.Count, "应保留一个输入文件");
                Equal(4, options.Jobs, "--jobs 应被解析");
                Equal(1, options.Overrides.Count, "应解析一个参数覆盖项");
                Equal("audioBitrate", options.Overrides[0].Key, "覆盖项键应正确");
                Equal("96k", options.Overrides[0].Value, "覆盖项值应正确");
                Equal("320 kbps", options.Preset, "含空格的预设名应作为单个参数");
                Equal(0, options.Errors.Count, "不应产生错误");
            });

            Run("CLI：内联值与短选项", delegate
            {
                var options = CliOptions.Parse(new[]
                {
                    "convert", "a.mp4", "--to=mp3", "--output-dir=out", "--force", "--json"
                });
                Equal("mp3", options.To, "--to=mp3 应被解析");
                Equal("out", options.OutputDir, "--output-dir=out 应被解析");
                True(options.Force, "--force 应生效");
                True(options.Json, "--json 应生效");
            });

            Run("CLI：冲突与非法值", delegate
            {
                var options = CliOptions.Parse(new[]
                {
                    "convert", "a.mp4", "--to", "mp3", "--output", "x.mp3", "--output-dir", "out",
                    "--json", "--quiet", "--jobs", "0", "--progress", "bad"
                });
                True(options.Errors.Count >= 4, "应记录全部组合错误");
            });

            Run("CLI：未知选项", delegate
            {
                var options = CliOptions.Parse(new[] { "convert", "a.mp4", "--to", "mp3", "--ouput", "x" });
                True(options.Errors.Any(e => e.Contains("--ouput")), "应报告未知选项");
            });

            Run("CLI：-- 终止符", delegate
            {
                var options = CliOptions.Parse(new[] { "convert", "--to", "mp3", "--", "--x.mp4" });
                Equal(1, options.Files.Count, "应保留终止符后的文件");
                Equal("--x.mp4", options.Files[0], "文件名应保留");
            });

            Run("CLI：formats 与 plugins", delegate
            {
                var formats = CliOptions.Parse(new[] { "formats", "--input", "mp4", "--json" });
                Equal(CliCommand.Formats, formats.Command, "应识别 formats");
                Equal("mp4", formats.InputExtension, "--input 应被解析");
                True(formats.Json, "--json 应生效");

                var plugins = CliOptions.Parse(new[] { "plugins", "--json" });
                Equal(CliCommand.Plugins, plugins.Command, "应识别 plugins");
                True(plugins.Json, "--json 应生效");
            });

            Run("CLI：帮助与版本", delegate
            {
                Equal(CliCommand.Help, CliOptions.Parse(new string[0]).Command, "无参数时应显示帮助");
                Equal(CliCommand.Help, CliOptions.Parse(new[] { "--help" }).Command, "--help 应显示帮助");
                Equal(CliCommand.Version, CliOptions.Parse(new[] { "--version" }).Command, "--version 应显示版本");
            });

            Run("选择器：唯一候选", delegate
            {
                var selection = FormatSelector.Choose(
                    new List<ConversionEntry> { Entry("p1", "A", "mp4", "mp3") },
                    "mp4", "mp3");
                True(selection.Selected != null, "唯一候选应被选中");
                Equal("p1", selection.Selected.PluginId, "应选中 p1");
            });

            Run("选择器：显式插件优先", delegate
            {
                var candidates = new List<ConversionEntry>
                {
                    Entry("p1", "A", "mp4", "mp3"),
                    Entry("p2", "B", "mp4", "mp3")
                };
                var selection = FormatSelector.Choose(candidates, "mp4", "mp3", "p2", null);
                Equal("p2", selection.Selected.PluginId, "显式插件应优先");
            });

            Run("选择器：显式插件不存在", delegate
            {
                var candidates = new List<ConversionEntry>
                {
                    Entry("p1", "A", "mp4", "mp3"),
                    Entry("p2", "B", "mp4", "mp3")
                };
                var selection = FormatSelector.Choose(candidates, "mp4", "mp3", "missing", null);
                True(selection.PreferredNotFound, "应标记显式插件不存在");
                True(selection.Selected == null, "不应回退到其他插件");
            });

            Run("选择器：记忆插件", delegate
            {
                var candidates = new List<ConversionEntry>
                {
                    Entry("p1", "A", "mp4", "mp3"),
                    Entry("p2", "B", "mp4", "mp3")
                };
                var selection = FormatSelector.Choose(candidates, "mp4", "mp3", null, "p1");
                Equal("p1", selection.Selected.PluginId, "应使用记忆插件");
            });

            Run("选择器：多候选歧义", delegate
            {
                var candidates = new List<ConversionEntry>
                {
                    Entry("p1", "A", "mp4", "mp3"),
                    Entry("p2", "B", "mp4", "mp3")
                };
                var selection = FormatSelector.Choose(candidates, "mp4", "mp3", null, null);
                True(selection.IsAmbiguous, "应标记歧义");
                True(selection.Selected == null, "不应自动选择");
                Equal(2, selection.Candidates.Count, "候选数量应正确");
            });

            Run("单位：码率与采样率归一化", delegate
            {
                Equal("320k", ValueUnits.NormalizeBitrate("320 kbps"), "码率应归一化");
                Equal("24k", ValueUnits.Normalize("24kbps", ValueUnitKind.Bitrate), "码率应归一化");
                Equal("44100", ValueUnits.NormalizeSampleRate("44.1 kHz"), "采样率应归一化");
                Equal("48000", ValueUnits.NormalizeSampleRate("48k"), "采样率应归一化");
            });

            Run("输出路径：避免覆盖", delegate
            {
                var directory = Path.Combine(Path.GetTempPath(), "uc_tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    var existing = Path.Combine(directory, "a.mp3");
                    File.WriteAllText(existing, "x");
                    var input = Path.Combine(directory, "input.wav");
                    File.WriteAllText(input, "x");

                    bool reservedFlag;
                    var reserved = OutputPathHelper.ReserveUniqueOutputPath(existing, input, out reservedFlag);
                    Equal("a (1).mp3", Path.GetFileName(reserved), "重名时应追加序号");
                    True(reservedFlag, "应报告占位文件已创建");
                    True(File.Exists(reserved), "应创建占位文件");

                    var sameAsInput = OutputPathHelper.ReserveUniqueOutputPath(input, input);
                    Equal("input (1).wav", Path.GetFileName(sameAsInput), "输出不能与输入相同");
                }
                finally
                {
                    try { Directory.Delete(directory, true); } catch { }
                }
            });

            DownloadTests.RunAll();
            PackageTests.RunAll();
            CliTests.RunAll();

            Console.WriteLine();
            Console.WriteLine(string.Format("合计：{0} 项，通过 {1} 项，失败 {2} 项。", _passed + _failed, _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static ConversionEntry Entry(string id, string name, string input, string output)
        {
            return new ConversionEntry
            {
                PluginId = id,
                PluginName = name,
                InputExtension = input,
                OutputExtension = output,
                IsAvailable = true
            };
        }

        internal static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("失败：" + name);
                Console.WriteLine("  " + ex.Message);
            }
        }

        internal static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(string.Format(
                    "{0}；期望“{1}”，实际“{2}”。", message, expected, actual));
            }
        }
    }
}
