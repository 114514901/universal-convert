using System.Collections.Generic;
using UniversalConvert.Core.Config;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 插件可选实现：声明插件自定义设置项。
    /// 实现后这些设置会出现在应用设置界面中，插件通过 IPluginContext.GetSetting 读取其值。
    /// </summary>
    public interface ISettingsContributor
    {
        IList<SettingDefinition> GetSettings();
    }
}
