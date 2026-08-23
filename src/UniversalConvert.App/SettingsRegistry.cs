using System.Collections.Generic;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// 应用级设置项注册表。新增一个设置 = 在此追加一条 SettingDefinition，
    /// 设置界面会自动渲染，无需改 UI。
    /// </summary>
    public static class SettingsRegistry
    {
        public static readonly IList<SettingDefinition> All = new List<SettingDefinition>
        {
            new SettingDefinition
            {
                Key = "updateChannel",
                Category = "更新",
                Label = "更新渠道",
                Description = "选择接收正式版还是开发版更新",
                Type = OptionType.Enum,
                DefaultValue = "auto",
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "auto", Label = "自动（跟随当前版本）" },
                    new OptionChoice { Value = "stable", Label = "仅正式版" },
                    new OptionChoice { Value = "dev", Label = "包含开发版" }
                }
            }
        };
    }
}
