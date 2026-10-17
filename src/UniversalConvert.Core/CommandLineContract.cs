using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalConvert.Core
{
    /// <summary>
    /// 主程序与右键菜单之间约定的命令行参数格式。
    /// 两者都依赖本契约，保证「点击即转」与「更多设置」的调用一致。
    ///
    /// 约定：
    /// - 选项名不区分大小写；
    /// - 支持 "--name value" 与 "--name=value" 两种写法；
    /// - "--" 之后的参数一律视为文件路径；
    /// - 本类型只做语法解析，不检查文件是否存在，由调用方统一校验。
    /// </summary>
    public static class CommandLineContract
    {
        public const string ConvertFlag = "--convert";
        public const string CustomizeFlag = "--customize";
        public const string ToFlag = "--to";
        public const string OutputFlag = "--output";
        public const string OutputDirFlag = "--output-dir";
        public const string PresetFlag = "--preset";
        public const string PluginFlag = "--plugin";
        public const string OpenFlag = "--open";
        public const string ReportFlag = "--report";
        public const string HelpFlag = "--help";
        public const string VersionFlag = "--version";

        /// <summary>构建「点击即转」命令行：App.exe --convert "a.mp4" --to mp3 [--preset "320 kbps"]。</summary>
        public static string BuildConvertCommand(
            string inputPath,
            string outputExtension,
            string outputPath = null,
            string presetName = null,
            string outputDir = null,
            string pluginId = null)
        {
            var args = ConvertFlag + " " + Quote(inputPath) + " " + ToFlag + " " + outputExtension;
            if (!string.IsNullOrEmpty(presetName))
            {
                args += " " + PresetFlag + " " + Quote(presetName);
            }
            if (!string.IsNullOrEmpty(pluginId))
            {
                args += " " + PluginFlag + " " + Quote(pluginId);
            }
            if (!string.IsNullOrEmpty(outputPath))
            {
                args += " " + OutputFlag + " " + Quote(outputPath);
            }
            if (!string.IsNullOrEmpty(outputDir))
            {
                args += " " + OutputDirFlag + " " + Quote(outputDir);
            }
            return args;
        }

        /// <summary>
        /// 构建「多选批量转换」命令行：App.exe --convert "a.mp4" "b.mp4" --to mp3 [--preset ...]。
        /// 输入文件全部以裸参数传递，Parse 时收进 ExtraFiles。
        /// </summary>
        public static string BuildConvertCommandBatch(
            IList<string> inputPaths,
            string outputExtension,
            string presetName = null,
            string outputDir = null,
            string pluginId = null)
        {
            var args = ConvertFlag;
            foreach (var path in inputPaths)
            {
                args += " " + Quote(path);
            }
            args += " " + ToFlag + " " + outputExtension;
            if (!string.IsNullOrEmpty(presetName))
            {
                args += " " + PresetFlag + " " + Quote(presetName);
            }
            if (!string.IsNullOrEmpty(pluginId))
            {
                args += " " + PluginFlag + " " + Quote(pluginId);
            }
            if (!string.IsNullOrEmpty(outputDir))
            {
                args += " " + OutputDirFlag + " " + Quote(outputDir);
            }
            return args;
        }

        /// <summary>构建「更多设置」命令行：App.exe --customize "a.mp4" --to mp3。</summary>
        public static string BuildCustomizeCommand(string inputPath, string outputExtension, string pluginId = null)
        {
            var args = CustomizeFlag + " " + Quote(inputPath) + " " + ToFlag + " " + outputExtension;
            if (!string.IsNullOrEmpty(pluginId))
            {
                args += " " + PluginFlag + " " + Quote(pluginId);
            }
            return args;
        }

        /// <summary>构建「使用 UniversalConvert 打开」命令行：App.exe --open "a.mp4" "b.mp4"。</summary>
        public static string BuildOpenCommand(IList<string> inputPaths)
        {
            var args = OpenFlag;
            if (inputPaths != null)
            {
                foreach (var path in inputPaths)
                {
                    args += " " + Quote(path);
                }
            }
            return args;
        }

        /// <summary>解析命令行参数。</summary>
        public sealed class ParsedArguments
        {
            public ParsedArguments()
            {
                ExtraFiles = new string[0];
                UnknownOptions = new List<string>();
                MissingOptionValues = new List<string>();
            }

            public bool IsConvertMode { get; set; }
            public bool IsCustomizeMode { get; set; }
            public bool IsOpenMode { get; set; }
            public bool IsReportMode { get; set; }
            public bool IsHelp { get; set; }
            public bool IsVersion { get; set; }

            /// <summary>报告类型（"hang" / "crash"），--report 时使用。</summary>
            public string ReportKind { get; set; }

            /// <summary>日志目录（供报告窗口读取 app.log），--report 时使用。</summary>
            public string ReportDir { get; set; }

            public string InputPath { get; set; }
            public string OutputExtension { get; set; }
            public string OutputPath { get; set; }

            /// <summary>批量转换的输出目录（--output-dir）。</summary>
            public string OutputDir { get; set; }

            public string PresetName { get; set; }

            /// <summary>显式指定的插件 Id（--plugin），用于同一方向存在多个候选插件时消除歧义。</summary>
            public string PluginId { get; set; }

            /// <summary>未识别的选项（含写法错误的选项），由调用方决定如何提示。</summary>
            public IList<string> UnknownOptions { get; private set; }

            /// <summary>缺少参数值的选项名，由调用方决定如何提示。</summary>
            public IList<string> MissingOptionValues { get; private set; }

            /// <summary>未识别的裸参数（文件路径列表）。</summary>
            public string[] ExtraFiles { get; set; }

            /// <summary>是否存在任何输入文件。</summary>
            public bool HasInputs
            {
                get
                {
                    return (ExtraFiles != null && ExtraFiles.Length > 0)
                        || !string.IsNullOrEmpty(InputPath);
                }
            }
        }

        public static ParsedArguments Parse(string[] args)
        {
            var result = new ParsedArguments();
            if (args == null) return result;

            var files = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == null) continue;

                // "--" 之后一律视为文件路径，允许文件名以 "--" 开头
                if (arg == "--")
                {
                    for (i++; i < args.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(args[i])) files.Add(args[i]);
                    }
                    break;
                }

                if (arg.Length == 0)
                {
                    continue;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string name = arg;
                    string inlineValue = null;
                    var eq = arg.IndexOf('=');
                    if (eq > 2)
                    {
                        name = arg.Substring(0, eq);
                        inlineValue = arg.Substring(eq + 1);
                    }

                    switch (name.ToLowerInvariant())
                    {
                        case ConvertFlag:
                            RequireNoInlineValue(result, arg, inlineValue);
                            result.IsConvertMode = true;
                            break;

                        case CustomizeFlag:
                            result.IsCustomizeMode = true;
                            result.InputPath = TakeValue(args, ref i, name, inlineValue, result);
                            break;

                        case ToFlag:
                            var to = TakeValue(args, ref i, name, inlineValue, result);
                            result.OutputExtension = NormalizeExtension(to);
                            break;

                        case OutputFlag:
                            result.OutputPath = TakeValue(args, ref i, name, inlineValue, result);
                            break;

                        case OutputDirFlag:
                            result.OutputDir = TakeValue(args, ref i, name, inlineValue, result);
                            break;

                        case PresetFlag:
                            result.PresetName = TakeValue(args, ref i, name, inlineValue, result);
                            break;

                        case PluginFlag:
                            result.PluginId = TakeValue(args, ref i, name, inlineValue, result);
                            break;

                        case OpenFlag:
                            RequireNoInlineValue(result, arg, inlineValue);
                            result.IsOpenMode = true;
                            break;

                        case ReportFlag:
                            result.IsReportMode = true;
                            result.ReportKind = TakeValue(args, ref i, name, inlineValue, result);
                            result.ReportDir = TakeValue(args, ref i, name, null, result);
                            break;

                        case HelpFlag:
                            RequireNoInlineValue(result, arg, inlineValue);
                            result.IsHelp = true;
                            break;

                        case VersionFlag:
                            RequireNoInlineValue(result, arg, inlineValue);
                            result.IsVersion = true;
                            break;

                        default:
                            result.UnknownOptions.Add(arg);
                            break;
                    }
                }
                else
                {
                    files.Add(arg);
                }
            }

            result.ExtraFiles = files.ToArray();
            return result;
        }

        private static string TakeValue(
            string[] args,
            ref int index,
            string optionName,
            string inlineValue,
            ParsedArguments result)
        {
            if (inlineValue != null)
            {
                if (inlineValue.Length == 0)
                {
                    result.MissingOptionValues.Add(optionName);
                    return null;
                }
                return inlineValue;
            }

            if (index + 1 >= args.Length || args[index + 1] == null || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result.MissingOptionValues.Add(optionName);
                return null;
            }

            index++;
            return args[index];
        }

        private static void RequireNoInlineValue(ParsedArguments result, string raw, string inlineValue)
        {
            if (inlineValue != null)
            {
                result.UnknownOptions.Add(raw);
            }
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return null;
            var trimmed = extension.Trim().TrimStart('.');
            return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
        }

        private static string Quote(string argument)
        {
            var quote = ((char)34).ToString();
            if (string.IsNullOrEmpty(argument)) return quote + quote;
            return quote + argument + quote;
        }
    }
}
