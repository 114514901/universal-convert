namespace UniversalConvert.Core.Models
{
    /// <summary>转换进度报告。Percentage 为 0-100，-1 表示不确定进度。</summary>
    public sealed class ConversionProgress
    {
        public double Percentage { get; set; }
        public string Message { get; set; }
        public ConversionStage Stage { get; set; }

        public ConversionProgress(ConversionStage stage, double percentage = -1, string message = null)
        {
            Stage = stage;
            Percentage = percentage;
            Message = message;
        }
    }

    public enum ConversionStage
    {
        Starting,
        Running,
        Finalizing,
        Completed
    }
}
