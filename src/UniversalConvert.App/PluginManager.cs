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
        /// <summary>根据 min/max 应用版本判断与当前应用的兼容性（供已加载插件与扩展中心共用）。</summary>
        public static PluginCompatibility CheckCompatibility(string minAppVersion, string maxAppVersion)
        {
            var app = AppVersion.Current;

            if (!string.IsNullOrEmpty(minAppVersion))
            {
                var min = SemVersion.Parse(minAppVersion);
                if (min != null && app != null && app.CompareTo(min) < 0)
                {
                    return PluginCompatibility.AppTooOld;
                }
            }

            if (!string.IsNullOrEmpty(maxAppVersion))
            {
                var max = SemVersion.Parse(maxAppVersion);
                if (max != null && app != null && app.CompareTo(max) > 0)
                {
                    return PluginCompatibility.Unverified;
                }
            }

            return PluginCompatibility.Compatible;
        }

        public static IList<PluginInfo> Inspect(CoreHost host)
        {
            var result = new List<PluginInfo>();

            foreach (var plugin in host.Plugins)
            {
                result.Add(new PluginInfo
                {
                    Plugin = plugin,
                    Compatibility = CheckCompatibility(plugin.MinAppVersion, plugin.MaxAppVersion)
                });
            }

            return result;
        }
    }
}
