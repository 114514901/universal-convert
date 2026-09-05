using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.App.Localization
{
    /// <summary>把错误解析器的错误类别映射为本地化的友好说明与建议。</summary>
    public static class ErrorMessages
    {
        public static string GetMessage(ConversionErrorKind kind)
        {
            switch (kind)
            {
                case ConversionErrorKind.ToolNotFound: return Strings.ErrorToolNotFound;
                case ConversionErrorKind.InputFileMissing: return Strings.ErrorInputFileMissing;
                case ConversionErrorKind.PermissionDenied: return Strings.ErrorPermissionDenied;
                case ConversionErrorKind.UnknownEncoder: return Strings.ErrorUnknownEncoder;
                case ConversionErrorKind.InvalidInput: return Strings.ErrorInvalidInput;
                case ConversionErrorKind.NoSpaceLeft: return Strings.ErrorNoSpaceLeft;
                case ConversionErrorKind.VersionMismatch: return Strings.ErrorVersionMismatch;
                case ConversionErrorKind.InvalidParameters: return Strings.ErrorInvalidParameters;
                case ConversionErrorKind.CodecUnsupported: return Strings.ErrorCodecUnsupported;
                case ConversionErrorKind.ToolCrashed: return Strings.ErrorToolCrashed;
                default: return Strings.ErrorUnknown;
            }
        }

        /// <summary>建议文案；detail 用于同类别下的格式特定细分（如 mp4 未下载完整）。</summary>
        public static string GetSuggestion(ConversionErrorKind kind, string detail = null)
        {
            // 格式特定细分优先
            if (kind == ConversionErrorKind.InvalidInput && detail == "InvalidInputIncomplete")
            {
                return Strings.ErrorInvalidInputIncompleteSuggestion;
            }

            switch (kind)
            {
                case ConversionErrorKind.ToolNotFound: return Strings.ErrorToolNotFoundSuggestion;
                case ConversionErrorKind.InputFileMissing: return Strings.ErrorInputFileMissingSuggestion;
                case ConversionErrorKind.PermissionDenied: return Strings.ErrorPermissionDeniedSuggestion;
                case ConversionErrorKind.UnknownEncoder: return Strings.ErrorUnknownEncoderSuggestion;
                case ConversionErrorKind.InvalidInput: return Strings.ErrorInvalidInputSuggestion;
                case ConversionErrorKind.NoSpaceLeft: return Strings.ErrorNoSpaceLeftSuggestion;
                case ConversionErrorKind.VersionMismatch: return Strings.ErrorVersionMismatchSuggestion;
                case ConversionErrorKind.InvalidParameters: return Strings.ErrorInvalidParametersSuggestion;
                case ConversionErrorKind.CodecUnsupported: return Strings.ErrorCodecUnsupportedSuggestion;
                case ConversionErrorKind.ToolCrashed: return Strings.ErrorToolCrashedSuggestion;
                default: return Strings.ErrorUnknownSuggestion;
            }
        }
    }
}