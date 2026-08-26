using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalConvert.Core
{
    /// <summary>
    /// 右键菜单与主程序之间约定的命令行参数格式。
    /// 两者都依赖本契约，保证"点击即转"与"更多设置"的调用一致。
    /// </summary>
    public static class CommandLineContract
    {
        public const string ConvertFlag = "--convert";
        public const string CustomizeFlag = "--customize";
        public const string ToFlag = "--to";
        public const string OutputFlag = "--output";
        public const string PresetFlag = "--preset";
        public const string OpenFlag = "--open";
        public const string ReportFlag = "--report";

        /// <summary>
        /// 构建"点击即转"命令行：App.exe --convert "a.mp4" --to mp3 [--preset "320 kbps"]。
        /// </summary>
        public static string BuildConvertCommand(string inputPath, string outputExtension, string outputPath = null, string presetName = null)
        {
            var args = $"{ConvertFlag} \"{inputPath}\" {ToFlag} {outputExtension}";
            if (!string.IsNullOrEmpty(presetName))
            {
                args += $" {PresetFlag} \"{presetName}\"";
            }
            if (!string.IsNullOrEmpty(outputPath))
            {
                args += $" {OutputFlag} \"{outputPath}\"";
            }
            return args;
        }

        /// <summary>
        /// 构建"多选批量转换"命令行：App.exe --convert "a.mp4" "b.mp4" --to mp3 [--preset ...]。
        /// 输入文件全部以裸参数传递，Parse 时收进 ExtraFiles。
        /// </summary>
        public static string BuildConvertCommandBatch(IList<string> inputPaths, string outputExtension, string presetName = null)
        {
            var args = ConvertFlag;
            foreach (var path in inputPaths)
            {
                args += " \"" + path + "\"";
            }
            args += $" {ToFlag} {outputExtension}";
            if (!string.IsNullOrEmpty(presetName))
            {
                args += $" {PresetFlag} \"{presetName}\"";
            }
            return args;
        }

        /// <summary>
        /// 构建"更多设置"命令行：App.exe --customize "a.mp4" --to mp3。
        /// </summary>
        public static string BuildCustomizeCommand(string inputPath, string outputExtension)
        {
            return $"{CustomizeFlag} \"{inputPath}\" {ToFlag} {outputExtension}";
        }

        /// <summary>解析命令行参数。</summary>
        public sealed class ParsedArguments
        {
            public bool IsConvertMode { get; set; }
            public bool IsCustomizeMode { get; set; }
            public bool IsOpenMode { get; set; }
            public bool IsReportMode { get; set; }
            /// <summary>报告类型（"hang" / "crash"），--report 时使用。</summary>
            public string ReportKind { get; set; }
            /// <summary>日志目录（供报告窗口读取 app.log），--report 时使用。</summary>
            public string ReportDir { get; set; }
            public string InputPath { get; set; }
            public string OutputExtension { get; set; }
            public string OutputPath { get; set; }
            public string PresetName { get; set; }
            /// <summary>未识别的裸参数（文件路径列表，用于 --open 批量）。</summary>
            public string[] ExtraFiles { get; set; } = new string[0];
        }

        public static ParsedArguments Parse(string[] args)
        {
            var result = new ParsedArguments();
            var extras = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case ConvertFlag:
                        // 输入文件不在此消费：--convert 后的所有文件作为裸参数收进 ExtraFiles（支持多文件批量）
                        result.IsConvertMode = true;
                        break;
                    case CustomizeFlag:
                        result.IsCustomizeMode = true;
                        if (i + 1 < args.Length) result.InputPath = args[++i];
                        break;
                    case ToFlag:
                        if (i + 1 < args.Length) result.OutputExtension = args[++i].TrimStart('.');
                        break;
                    case OutputFlag:
                        if (i + 1 < args.Length) result.OutputPath = args[++i];
                        break;
                    case PresetFlag:
                        if (i + 1 < args.Length) result.PresetName = args[++i];
                        break;
                    case OpenFlag:
                        result.IsOpenMode = true;
                        break;
                    case ReportFlag:
                        result.IsReportMode = true;
                        if (i + 1 < args.Length) result.ReportKind = args[++i];
                        if (i + 1 < args.Length) result.ReportDir = args[++i];
                        break;
                    default:
                        if (!arg.StartsWith("--") && File.Exists(arg))
                        {
                            extras.Add(arg);
                        }
                        break;
                }
            }

            result.ExtraFiles = extras.ToArray();
            return result;
        }
    }
}
