using System.Collections.Generic;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// 应用级设置项注册表。新增一个设置 = 在此追加一条 SettingDefinition，
    /// 设置界面会自动渲染，无需改 UI。
    /// 标签/分组/候选项用 '@资源键' 表示（经 Strings.L 本地化），插件设置可用普通字符串。
    /// </summary>
    public static class SettingsRegistry
    {
        public static readonly IList<SettingDefinition> All = new List<SettingDefinition>
        {
            new SettingDefinition
            {
                Key = "language",
                Category = "@SettingsCategoryGeneral",
                Label = "@SettingLanguage",
                Description = "@SettingLanguageDescription",
                Type = OptionType.Enum,
                DefaultValue = "auto",
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "auto", Label = "@LanguageAuto" },
                    new OptionChoice { Value = "zh", Label = "@LanguageZh" },
                    new OptionChoice { Value = "en", Label = "@LanguageEn" }
                }
            },
            new SettingDefinition
            {
                Key = "updateChannel",
                Category = "@SettingsCategoryUpdate",
                Label = "@SettingUpdateChannel",
                Description = "@SettingUpdateChannelDescription",
                Type = OptionType.Enum,
                DefaultValue = "auto",
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "auto", Label = "@UpdateChannelAuto" },
                    new OptionChoice { Value = "stable", Label = "@UpdateChannelStable" },
                    new OptionChoice { Value = "dev", Label = "@UpdateChannelDev" }
                }
            }
        };
    }
}
