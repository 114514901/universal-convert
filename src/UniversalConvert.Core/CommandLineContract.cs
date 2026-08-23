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
                        result.IsConvertMode = true;
                        if (i + 1 < args.Length) result.InputPath = args[++i];
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
