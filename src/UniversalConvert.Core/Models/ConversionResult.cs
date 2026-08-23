using System;

namespace UniversalConvert.Core.Models
{
    /// <summary>转换结果。</summary>
    public sealed class ConversionResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }
        /// <summary>完整错误输出（stdout/stderr），供错误解析与"复制报错"使用。</summary>
        public string FullError { get; set; }
        public int? ExitCode { get; set; }
        public TimeSpan Duration { get; set; }

        public static ConversionResult Succeeded(string outputPath, TimeSpan duration)
        {
            return new ConversionResult
            {
                Success = true,
                OutputPath = outputPath,
                Duration = duration
            };
        }

        public static ConversionResult Failed(string errorMessage, TimeSpan duration, string fullError = null, int? exitCode = null)
        {
            return new ConversionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                FullError = fullError,
                ExitCode = exitCode,
                Duration = duration
            };
        }
    }
}
