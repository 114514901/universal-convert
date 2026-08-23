using System.Collections.Generic;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>参数类型，决定 UI 动态表单用什么控件渲染。</summary>
    public enum OptionType
    {
        Bool,
        Int,
        Enum,
        String
    }

    /// <summary>枚举型参数的候选项。</summary>
    public sealed class OptionChoice
    {
        /// <summary>实际传给插件的值，如 "320k"。</summary>
        public string Value { get; set; }

        /// <summary>给用户看的标签，如 "320 kbps"。</summary>
        public string Label { get; set; }
    }

    /// <summary>
    /// 参数定义：声明一个可编辑参数的类型、标签、默认值与候选项。
    /// UI 动态表单据此通用渲染控件，插件据此 + 用户值生成命令行。
    /// </summary>
    public sealed class OptionDefinition
    {
        /// <summary>参数键，如 "videoBitrate"。插件按此键取值。</summary>
        public string Key { get; set; }

        /// <summary>显示标签，如 "视频码率"。</summary>
        public string Label { get; set; }

        /// <summary>控件类型。</summary>
        public OptionType Type { get; set; }

        /// <summary>默认值；为空表示不传该参数。</summary>
        public string DefaultValue { get; set; }

        /// <summary>Enum 型时的候选项。</summary>
        public IList<OptionChoice> Choices { get; set; }

        public OptionDefinition()
        {
            Choices = new List<OptionChoice>();
        }
    }
}
