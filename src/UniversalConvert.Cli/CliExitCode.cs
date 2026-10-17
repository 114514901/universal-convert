namespace UniversalConvert.Cli
{
    /// <summary>命令行退出码，与帮助文档保持一致。</summary>
    internal static class CliExitCode
    {
        public const int Success = 0;
        public const int ConversionFailed = 1;
        public const int UsageError = 2;
        public const int UnsupportedFormat = 3;
        public const int InputNotFound = 4;
        public const int ToolUnavailable = 5;
        public const int PartialFailure = 6;
        public const int Cancelled = 130;
        public const int InternalError = 70;
    }
}
