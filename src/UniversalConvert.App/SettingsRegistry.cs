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
                RequiresRestart = true,
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "auto", Label = "@LanguageAuto" },
                    new OptionChoice { Value = "zh", Label = "@LanguageZh" },
                    new OptionChoice { Value = "en", Label = "@LanguageEn" }
                }
            },
            new SettingDefinition
            {
                Key = "workerThreads",
                Category = "@SettingsCategoryGeneral",
                Label = "@SettingWorkerThreads",
                Description = "@SettingWorkerThreadsDescription",
                Type = OptionType.Enum,
                DefaultValue = "2",
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "1", Label = "1" },
                    new OptionChoice { Value = "2", Label = "2" },
                    new OptionChoice { Value = "4", Label = "4" },
                    new OptionChoice { Value = "8", Label = "8" },
                    new OptionChoice { Value = "auto", Label = "@WorkerThreadsAuto" }
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
            },
            new SettingDefinition
            {
                Key = "logLevel",
                Category = "@SettingsCategoryAdvanced",
                Label = "@SettingLogLevel",
                Description = "@SettingLogLevelDescription",
                Type = OptionType.Enum,
                DefaultValue = "Info",
                RequiresRestart = true,
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "Debug", Label = "Debug" },
                    new OptionChoice { Value = "Info", Label = "Info" },
                    new OptionChoice { Value = "Warn", Label = "Warn" },
                    new OptionChoice { Value = "Error", Label = "Error" }
                }
            },
            new SettingDefinition
            {
                Key = "crashDumpEnabled",
                Category = "@SettingsCategoryAdvanced",
                Label = "@SettingCrashDumpEnabled",
                Description = "@SettingCrashDumpEnabledDescription",
                Type = OptionType.Bool,
                DefaultValue = "true",
                RequiresRestart = true
            },
            new SettingDefinition
            {
                Key = "crashDumpLevel",
                Category = "@SettingsCategoryAdvanced",
                Label = "@SettingCrashDumpLevel",
                Description = "@SettingCrashDumpLevelDescription",
                Type = OptionType.Enum,
                DefaultValue = "Normal",
                RequiresRestart = true,
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "Normal", Label = "Normal" },
                    new OptionChoice { Value = "WithDataSegments", Label = "WithDataSegments" },
                    new OptionChoice { Value = "FullMemory", Label = "FullMemory" }
                }
            },
            new SettingDefinition
            {
                Key = "themeAccent",
                Category = "@SettingsCategoryPersonalization",
                Label = "@SettingThemeAccent",
                Description = "@SettingThemeAccentDescription",
                Type = OptionType.Enum,
                DefaultValue = "#0078D4",
                Choices = new List<OptionChoice>
                {
                    new OptionChoice { Value = "#0078D4", Label = "@ThemeAccentBlue" },
                    new OptionChoice { Value = "#E81123", Label = "@ThemeAccentRed" },
                    new OptionChoice { Value = "#107C10", Label = "@ThemeAccentGreen" },
                    new OptionChoice { Value = "#FF8C00", Label = "@ThemeAccentOrange" },
                    new OptionChoice { Value = "#8764B8", Label = "@ThemeAccentPurple" },
                    new OptionChoice { Value = "#00B7C3", Label = "@ThemeAccentTeal" }
                }
            },
        };
    }
}
