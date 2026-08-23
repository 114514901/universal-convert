using System.Collections.Generic;
using UniversalConvert.Core;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>插件与当前应用的兼容性状态。</summary>
    public enum PluginCompatibility
    {
        Compatible,
        AppTooOld,
        Unverified
    }

    public sealed class PluginInfo
    {
        public IConverterPlugin Plugin { get; set; }
        public PluginCompatibility Compatibility { get; set; }
    }

    /// <summary>根据插件的版本约束（Min/MaxAppVersion）判断与当前应用的兼容性。</summary>
    public static class PluginManager
    {
        public static IList<PluginInfo> Inspect(CoreHost host)
        {
            var app = AppVersion.Current;
            var result = new List<PluginInfo>();

            foreach (var plugin in host.Plugins)
            {
                var info = new PluginInfo { Plugin = plugin, Compatibility = PluginCompatibility.Compatible };

                if (!string.IsNullOrEmpty(plugin.MinAppVersion))
                {
                    var min = SemVersion.Parse(plugin.MinAppVersion);
                    if (min != null && app != null && app.CompareTo(min) < 0)
                    {
                        info.Compatibility = PluginCompatibility.AppTooOld;
                    }
                }

                if (info.Compatibility == PluginCompatibility.Compatible && !string.IsNullOrEmpty(plugin.MaxAppVersion))
                {
                    var max = SemVersion.Parse(plugin.MaxAppVersion);
                    if (max != null && app != null && app.CompareTo(max) > 0)
                    {
                        info.Compatibility = PluginCompatibility.Unverified;
                    }
                }

                result.Add(info);
            }

            return result;
        }
    }
}
