using System.Collections.Generic;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>一种可输出的目标格式。</summary>
    public sealed class OutputFormat
    {
        /// <summary>输出扩展名，含点，如 ".mp3"。</summary>
        public string Extension { get; set; }

        /// <summary>人类可读名称，如 "MP3 Audio"。</summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 该输出格式的可选预设（如 mp3 的 128/192/320kbps），
        /// 为空表示使用默认参数。预设 = 在默认参数之上的一组覆盖。
        /// </summary>
        public IList<ConversionPreset> Presets { get; set; }

        /// <summary>
        /// 该输出格式可编辑参数的 schema，供 UI 动态表单渲染。
        /// 为空表示该格式无需参数（右键菜单里直接点击即转）。
        /// </summary>
        public IList<OptionDefinition> Options { get; set; }

        public OutputFormat()
        {
            Presets = new List<ConversionPreset>();
            Options = new List<OptionDefinition>();
        }
    }

    /// <summary>转换预设：一组带名字的参数覆盖。</summary>
    public sealed class ConversionPreset
    {
        public string Name { get; set; }
        public IDictionary<string, string> Options { get; set; }

        public ConversionPreset()
        {
            Options = new Dictionary<string, string>();
        }
    }
}
