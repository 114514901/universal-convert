using System;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.Cli
{
    /// <summary>命令行文案。全部使用正式书面语。</summary>
    internal static class CliText
    {
        public const string ProgramName = "UniversalConvert CLI";

        public const string UnknownOption = "错误：未知选项“{0}”。";
        public const string UnknownCommand = "错误：未知命令“{0}”。";
        public const string UnknownCommandHint = "请使用 convert、formats 或 plugins 子命令；使用 --help 查看完整帮助。";
        public const string UsageHint = "使用 --help 查看完整帮助。";
        public const string MissingOptionValue = "错误：选项“{0}”缺少参数值。";
        public const string MissingTargetFormat = "错误：未指定目标格式。请使用 --to <扩展名>。";
        public const string JsonQuietConflict = "错误：--json 与 --quiet 不能同时使用。";
        public const string OutputConflict = "错误：--output 与 --output-dir 不能同时使用。";
        public const string OutputOnlySingle = "错误：--output 仅适用于单文件转换；批量转换请使用 --output-dir。";
        public const string OutputSameAsInput = "错误：输出文件不能与输入文件相同：{0}";
        public const string InvalidJobs = "错误：--jobs 必须为 1 至 64 之间的整数。";
        public const string InvalidProgress = "错误：--progress 仅支持 auto、always、never。";
        public const string InvalidOverride = "错误：参数覆盖项“{0}”格式无效，应为“键=值”。";

        public const string NoInput = "错误：未指定任何输入文件。";
        public const string InputNotFound = "错误：输入文件不存在：{0}。";
        public const string UnsupportedFormat = "错误：目标格式“{0}”不受支持。输入文件：{1}";
        public const string PluginNotFound = "错误：插件“{0}”不在候选列表中。候选插件：{1}";
        public const string PluginAmbiguous = "错误：转换方向存在多个候选插件，请使用 --plugin 指定：{0}";
        public const string PluginUnavailable = "错误：插件“{0}”所需的工具不可用。";
        public const string PresetNotFound = "错误：未找到预设“{0}”。可用预设：{1}";
        public const string OptionUnsupported = "错误：选项“{0}”不受当前格式支持。可用选项：{1}";
        public const string OutputExtensionMismatch = "错误：--output 的扩展名“{0}”与 --to 指定的“{1}”不一致。";
        public const string OutputNoExtension = "错误：--output 指定的文件缺少扩展名：{0}";
        public const string OutputIsDirectory = "错误：--output 指定的路径是目录：{0}";
        public const string FinalizeFailed = "无法写入输出文件：{0}";
        public const string InvalidPath = "错误：路径无效“{0}”：{1}";
        public const string FileListReadFailed = "错误：无法读取文件列表“{0}”：{1}";
        public const string OutputDirCreateFailed = "错误：无法创建输出目录“{0}”：{1}";
        public const string DuplicateOutput = "错误：多个输入文件解析到同一输出路径：{0}";
        public const string OutputExists = "错误：输出文件已存在，且无法预留输出路径：{0}。请使用 --force 覆盖，或指定其他输出路径。";
        public const string InternalError = "错误：执行过程中发生异常：{0}";
        public const string ExecutionException = "执行转换时发生异常：{0}";
        public const string PreparationFailed = "无法准备输出文件：{0}";
        public const string LoadErrors = "插件加载过程中出现错误：{0}";

        public const string SuccessLine = "转换成功：{0} → {1}（用时 {2:F2} 秒）";
        public const string FailureLine = "转换失败：{0}：{1}";
        public const string SkippedLine = "已跳过：{0}";
        public const string ProgressLine = "{0}  {1:F0}%  {2}";
        public const string Cancelled = "已取消。";
        public const string Summary = "转换完成：共 {0} 个文件，成功 {1} 个，失败 {2} 个，跳过 {3} 个，用时 {4:F2} 秒。";
        public const string DryRunSummary = "共解析 {0} 个文件；未执行转换。";
        public const string PlanLine = "计划：{0} → {1}（插件：{2}；预设：{3}）";
        public const string PlanOverwrite = "；目标文件已存在，将覆盖";
        public const string NoPreset = "无";
        public const string PluginLine = "插件：{0}（{1}）版本 {2}{3}";
        public const string PluginAvailable = "；工具可用";
        public const string PluginUnavailableMark = "；工具不可用";
        public const string PluginUntested = "；未经测试";
        public const string FormatsHeader = "输入扩展名\t输出扩展名\t插件";
        public const string NoFormats = "没有可列出的转换格式。";
        public const string NoPlugins = "没有加载任何插件。";
        public const string CapabilityCount = "能力条目：{0}";
        public const string VersionFormat = "UniversalConvert CLI {0}";
        public const string VerboseResolve = "解析结果：{0} → {1}；插件 {2}（{3}）；预设 {4}；参数 {5}";
        public const string VerbosePluginDirectory = "插件目录：{0}";
        public const string VerboseLoaded = "已加载插件 {0} 个，格式条目 {1} 条。";
        public const string ProgressStageStarting = "准备转换";
        public const string ProgressStageRunning = "转换中";
        public const string ProgressStageFinalizing = "正在写入输出";
        public const string ProgressStageCompleted = "已完成";

        public const string Help = @"UniversalConvert 命令行工具

用法：
  UniversalConvert.Cli.exe convert <文件...> --to <扩展名> [选项]
  UniversalConvert.Cli.exe formats [--input <扩展名>] [--json]
  UniversalConvert.Cli.exe plugins [--json]
  UniversalConvert.Cli.exe --help
  UniversalConvert.Cli.exe --version

转换选项：
  --to <扩展名>          目标格式，例如 mp3、mp4（必填）
  --output <文件>        单文件转换的输出文件路径
  --output-dir <目录>    输出目录；目录不存在时自动创建
  --preset <名称>        使用指定预设
  --plugin <插件 Id>     同一转换方向存在多个插件时指定插件
  -O, --option <键=值>   覆盖插件参数，可重复使用
  --file-list <文件>     从 UTF-8 文本文件读取输入路径，每行一个
  --force                覆盖已存在的输出文件
  --dry-run              仅显示解析结果，不执行转换
  --jobs <数量>          并行转换数量，范围 1 至 64，默认 1
  --stop-on-error        任一文件失败后不再启动后续文件
  --progress <模式>      进度显示：auto、always、never，默认 auto
  --json                 以行分隔 JSON 输出事件
  --quiet                仅输出错误信息
  --verbose              输出详细诊断信息
  -h, --help             显示本帮助
  --version              显示版本信息

其他命令：
  formats                列出已注册的转换方向
  plugins                列出已加载的插件
  --input <扩展名>       与 formats 配合使用，仅列出指定输入格式

退出码：
  0    全部成功
  1    转换失败
  2    参数错误
  3    格式不受支持，或无法唯一确定插件
  4    输入文件不存在
  5    所需外部工具不可用
  6    部分文件失败
  70   内部错误
  130  用户取消

说明：
  1. 默认不覆盖已存在的输出文件；重名时自动追加序号。
  2. 使用 --force 时，转换成功后再替换目标文件；转换失败不会破坏原文件。
  3. 输出目录不存在时自动创建。
  4. 同一转换方向存在多个插件时，依次使用 --plugin、设置中的记忆、唯一候选；仍无法确定时报错。
  5. 使用 -- 分隔选项与文件路径，可处理以短横线开头的文件名。
  6. convert 使用 --json 时，全部事件以一行一个 JSON 对象的形式写入标准输出；
     formats 与 plugins 使用 --json 时输出 JSON 数组。
     插件加载警告写入标准错误，不混入 JSON 输出。
  7. --dry-run 不创建输出目录，也不写入任何文件。
  8. --file-list 中的相对路径相对于当前工作目录解析。

示例：
  UniversalConvert.Cli.exe convert ""a.mp4"" --to mp3
  UniversalConvert.Cli.exe convert ""a.mp4"" ""b.mkv"" --to mp4 --output-dir ""D:\Output"" --jobs 2
  UniversalConvert.Cli.exe convert ""a.flac"" --to mp3 --preset ""320 kbps"" --force
  UniversalConvert.Cli.exe formats --input mp4
  UniversalConvert.Cli.exe plugins --json";

        public static string DescribeErrorKind(ConversionErrorKind kind)
        {
            switch (kind)
            {
                case ConversionErrorKind.ToolNotFound:
                    return "未找到所需的外部工具。";
                case ConversionErrorKind.InputFileMissing:
                    return "输入文件不存在。";
                case ConversionErrorKind.PermissionDenied:
                    return "没有访问权限。";
                case ConversionErrorKind.UnknownEncoder:
                    return "编码器不可用。";
                case ConversionErrorKind.InvalidInput:
                    return "输入文件无效或已损坏。";
                case ConversionErrorKind.NoSpaceLeft:
                    return "磁盘空间不足。";
                case ConversionErrorKind.VersionMismatch:
                    return "组件版本不兼容。";
                case ConversionErrorKind.InvalidParameters:
                    return "参数无效。";
                case ConversionErrorKind.CodecUnsupported:
                    return "目标编码器或容器不受支持。";
                case ConversionErrorKind.ToolCrashed:
                    return "外部工具异常终止。";
                default:
                    return "发生未知错误。";
            }
        }
    }
}
