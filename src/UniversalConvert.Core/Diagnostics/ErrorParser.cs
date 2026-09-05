using System.Text.RegularExpressions;

namespace UniversalConvert.Core.Diagnostics
{
    /// <summary>错误类别，供 UI 映射到本地化的友好说明与建议。</summary>
    public enum ConversionErrorKind
    {
        Unknown,
        ToolNotFound,
        InputFileMissing,
        PermissionDenied,
        UnknownEncoder,
        InvalidInput,
        NoSpaceLeft,
        VersionMismatch,
        InvalidParameters,
        CodecUnsupported,
        ToolCrashed
    }

    /// <summary>错误解析结果。</summary>
    public sealed class ErrorAnalysis
    {
        public ConversionErrorKind Kind { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// 常见转换错误解析器：把工具报错文本（如 ffmpeg stderr）归类为已知错误，
    /// 由 UI 据此给出友好提示与修复建议。
    /// </summary>
    public static class ErrorParser
    {
        /// <summary>
        /// 解析错误。
        /// </summary>
        /// <param name="errorText">工具 stderr/stdout 原文。</param>
        /// <param name="exitCode">工具退出码（如 0xC0000005 访问冲突）。</param>
        /// <param name="outputExtension">目标扩展名（含点，如 ".mp4"），用于格式特定建议。</param>
        public static ErrorAnalysis Parse(string errorText, int? exitCode = null, string outputExtension = null)
        {
            // 1. 退出码优先：工具进程崩溃（访问冲突/非法指令/堆损坏）
            if (exitCode.HasValue && IsCrashExitCode(exitCode.Value))
            {
                return New(ConversionErrorKind.ToolCrashed, "ToolCrashed");
            }

            if (string.IsNullOrEmpty(errorText))
            {
                return new ErrorAnalysis { Kind = ConversionErrorKind.Unknown };
            }

            string text = errorText;

            // 2. 版本不兼容（必须先于 ToolNotFound——其文案含"找不到"）
            if (Regex.IsMatch(text, @"找不到方法|MissingMethodException|method not found|找不到类型|TypeLoadException|FileLoadException|违反继承安全规则|incompatible version", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.VersionMismatch, "VersionMismatch");

            // 3. 参数/滤镜错误（自定义参数写错、滤镜不存在、选项拼错）
            if (Regex.IsMatch(text, @"No such filter|Error opening filter|Invalid option|Unrecognized option|Unknown option|Unable to parse option|Error parsing option|Invalid argument", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.InvalidParameters, "InvalidParameters");

            // 4. 编码器/容器不支持
            if (Regex.IsMatch(text, @"Error while opening encoder|codec not currently supported|Invalid encoder|Unable to find a suitable output format|Could not find tag for codec", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.CodecUnsupported, "CodecUnsupported");

            // 5. 编码器名未知
            if (Regex.IsMatch(text, @"Unknown encoder|Unknown decoder", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.UnknownEncoder, "UnknownEncoder");

            // 6. 输入文件缺失
            if (Regex.IsMatch(text, @"No such file or directory", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.InputFileMissing, "NoSuchFile");

            // 7. 权限
            if (Regex.IsMatch(text, @"Permission denied|Access is denied|Access Denied", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.PermissionDenied, "PermissionDenied");

            // 8. 输入无效/损坏
            if (Regex.IsMatch(text, @"Invalid data found|corrupt|Invalid data|not a valid|moov atom not found|Could not find codec parameters|Truncated", RegexOptions.IgnoreCase))
            {
                // 格式特定建议：mp4/mov + moov 缺失 = 可能未下载完整
                if (Regex.IsMatch(text, @"moov atom not found", RegexOptions.IgnoreCase)
                    && IsMp4Like(outputExtension))
                {
                    return New(ConversionErrorKind.InvalidInput, "InvalidInputIncomplete");
                }
                return New(ConversionErrorKind.InvalidInput, "InvalidInput");
            }

            // 9. 磁盘满
            if (Regex.IsMatch(text, @"No space left on device|not enough space|disk full", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.NoSpaceLeft, "NoSpace");

            // 10. 工具缺失（含 Windows cmd 报错）
            if (Regex.IsMatch(text, @"not found|无法找到|未找到|找不到|No such file|不是内部或外部命令|command not found|'[^']*' 不是内部或外部命令", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.ToolNotFound, "ToolNotFound");

            return new ErrorAnalysis { Kind = ConversionErrorKind.Unknown };
        }

        /// <summary>崩溃类退出码（负的 NTSTATUS：访问冲突/非法指令/堆损坏/断点）。</summary>
        private static bool IsCrashExitCode(int exitCode)
        {
            uint code = unchecked((uint)exitCode);
            return code == 0xC0000005  // 访问冲突
                || code == 0xC000001D  // 非法指令
                || code == 0xC0000374  // 堆损坏
                || code == 0x80000003  // 断点
                || code == 0xC0000094; // 除零
        }

        private static bool IsMp4Like(string outputExtension)
        {
            return string.Equals(outputExtension, ".mp4", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(outputExtension, ".m4a", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(outputExtension, ".mov", System.StringComparison.OrdinalIgnoreCase);
        }

        private static ErrorAnalysis New(ConversionErrorKind kind, string detail)
        {
            return new ErrorAnalysis { Kind = kind, Detail = detail };
        }
    }
}