using System;

namespace UniversalConvert.Core.Models
{
    /// <summary>转换结果。</summary>
    public sealed class ConversionResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }
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

        public static ConversionResult Failed(string errorMessage, TimeSpan duration)
        {
            return new ConversionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Duration = duration
            };
        }
    }
}
