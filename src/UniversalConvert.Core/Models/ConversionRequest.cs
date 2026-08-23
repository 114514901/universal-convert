using System;
using System.Collections.Generic;

namespace UniversalConvert.Core.Models
{
    /// <summary>一次转换请求的完整描述。</summary>
    public sealed class ConversionRequest
    {
        /// <summary>目标插件 Id，如 "com.universalconvert.ffmpeg"。</summary>
        public string PluginId { get; set; }

        /// <summary>源文件完整路径。</summary>
        public string InputPath { get; set; }

        /// <summary>输出文件完整路径（可为空，由引擎/插件自动生成）。</summary>
        public string OutputPath { get; set; }

        /// <summary>输入扩展名（含点），便于插件分发。</summary>
        public string InputExtension { get; set; }

        /// <summary>目标输出扩展名（含点）。</summary>
        public string OutputExtension { get; set; }

        /// <summary>覆盖参数（预设合并结果），插件自定义语义。</summary>
        public IDictionary<string, string> Options { get; set; }

        public ConversionRequest()
        {
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
