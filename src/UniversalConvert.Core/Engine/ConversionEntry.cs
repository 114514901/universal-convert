using System.Collections.Generic;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Engine
{
    /// <summary>
    /// 格式注册表中的一条"可转换"记录：某插件能把 .extA 转成 .extB。
    /// 由 FormatRegistry 从所有插件的 GetCapabilities() 展开得到，
    /// 是 UI 与右键菜单共用的唯一数据源。
    /// </summary>
    public sealed class ConversionEntry
    {
        public string PluginId { get; set; }
        public string PluginName { get; set; }

        /// <summary>输入扩展名，规范化为小写、不带点，如 "mp4"。</summary>
        public string InputExtension { get; set; }

        /// <summary>输入格式显示名。</summary>
        public string InputDisplayName { get; set; }

        /// <summary>输出扩展名，规范化为小写、不带点，如 "mp3"。</summary>
        public string OutputExtension { get; set; }

        /// <summary>输出格式显示名。</summary>
        public string OutputDisplayName { get; set; }

        /// <summary>插件当前是否可用（工具已安装）。</summary>
        public bool IsAvailable { get; set; }

        /// <summary>该输出的命名预设（可能为空）。</summary>
        public IList<ConversionPreset> Presets { get; set; }

        /// <summary>该输出可编辑参数的 schema（可能为空）。</summary>
        public IList<OptionDefinition> Options { get; set; }

        /// <summary>是否有预设或可编辑参数（决定右键菜单显示子菜单还是直接点击）。</summary>
        public bool HasCustomizableOptions
        {
            get
            {
                return (Presets != null && Presets.Count > 0) || (Options != null && Options.Count > 0);
            }
        }

        public ConversionEntry()
        {
            Presets = new List<ConversionPreset>();
            Options = new List<OptionDefinition>();
        }
    }
}
