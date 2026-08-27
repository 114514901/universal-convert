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
        VersionMismatch
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
        public static ErrorAnalysis Parse(string errorText)
        {
            if (string.IsNullOrEmpty(errorText))
            {
                return new ErrorAnalysis { Kind = ConversionErrorKind.Unknown };
            }

            string text = errorText;

            // 版本不兼容：旧版扩展二进制把可选参数方法编译成旧签名，新 Core 中方法签名不匹配
            // （MissingMethodException/找不到方法等）。必须先于 ToolNotFound 判断（其文案含"找不到"）。
            if (Regex.IsMatch(text, @"找不到方法|MissingMethodException|method not found|找不到类型|TypeLoadException|FileLoadException|违反继承安全规则|incompatible version", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.VersionMismatch, "VersionMismatch");

            if (Regex.IsMatch(text, @"Unknown encoder|Unknown decoder|Unrecognized option|Unable to find a suitable output format", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.UnknownEncoder, "UnknownEncoder");

            if (Regex.IsMatch(text, @"No such file or directory", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.InputFileMissing, "NoSuchFile");

            if (Regex.IsMatch(text, @"Permission denied|Access is denied|Access Denied", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.PermissionDenied, "PermissionDenied");

            if (Regex.IsMatch(text, @"Invalid data found when processing input|Invalid argument|corrupt|Invalid data|not a valid|moov atom not found", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.InvalidInput, "InvalidInput");

            if (Regex.IsMatch(text, @"No space left on device|not enough space|disk full", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.NoSpaceLeft, "NoSpace");

            if (Regex.IsMatch(text, @"not found|无法找到|未找到|找不到|No such file", RegexOptions.IgnoreCase))
                return New(ConversionErrorKind.ToolNotFound, "ToolNotFound");

            return new ErrorAnalysis { Kind = ConversionErrorKind.Unknown };
        }

        private static ErrorAnalysis New(ConversionErrorKind kind, string detail)
        {
            return new ErrorAnalysis { Kind = kind, Detail = detail };
        }
    }
}
