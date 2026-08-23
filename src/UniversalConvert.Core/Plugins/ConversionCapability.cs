using System.Collections.Generic;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>一种输入格式及其可转出的目标格式集合。</summary>
    public sealed class ConversionCapability
    {
        /// <summary>输入扩展名，含点，如 ".mp4"。</summary>
        public string InputExtension { get; set; }

        /// <summary>输入格式的人类可读名称，如 "MP4 Video"。</summary>
        public string InputDisplayName { get; set; }

        /// <summary>该输入可转出的所有目标格式。</summary>
        public IList<OutputFormat> Outputs { get; set; }

        public ConversionCapability()
        {
            Outputs = new List<OutputFormat>();
        }
    }
}
