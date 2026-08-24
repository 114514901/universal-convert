using System.Collections.Generic;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Config
{
    /// <summary>
    /// 设置项定义（schema）。设置界面据此通用渲染控件。
    /// 应用与插件均可声明设置项，值统一持久化在 AppConfig.Settings 中。
    /// </summary>
    public sealed class SettingDefinition
    {
        /// <summary>设置键，全局唯一（插件建议用 "插件Id.xxx" 前缀避免冲突）。</summary>
        public string Key { get; set; }

        /// <summary>分组名（设置界面按分组展示）。</summary>
        public string Category { get; set; }

        /// <summary>显示标签。</summary>
        public string Label { get; set; }

        /// <summary>说明（可选）。</summary>
        public string Description { get; set; }

        /// <summary>控件类型。</summary>
        public OptionType Type { get; set; }

        /// <summary>默认值。</summary>
        public string DefaultValue { get; set; }

        /// <summary>是否需重启应用才生效（保存时若有改动会提示重启）。</summary>
        public bool RequiresRestart { get; set; }

        /// <summary>Enum 型时的候选项。</summary>
        public IList<OptionChoice> Choices { get; set; }

        public SettingDefinition()
        {
            Choices = new List<OptionChoice>();
        }
    }
}
