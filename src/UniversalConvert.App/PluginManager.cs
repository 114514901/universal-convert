using System;
using System.Collections.Generic;
using System.IO;
using UniversalConvert.Core;
using UniversalConvert.Core.Config;
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
        /// <summary>插件 DLL 是否位于用户插件目录（%AppData%\UniversalConvert\plugins，即非内置、在线安装的扩展）。</summary>
        public static bool IsUserPlugin(IConverterPlugin plugin)
        {
            try
            {
                var location = plugin.GetType().Assembly.Location;
                if (string.IsNullOrEmpty(location)) return false;
                var userDir = ConfigStore.UserPluginsDirectory;
                return !string.IsNullOrEmpty(userDir)
                    && location.StartsWith(userDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 卸载用户插件：删除其所在目录（若 DLL 直接在用户插件根目录则只删文件）。
        /// 仅允许操作用户插件目录内的内容；被加载锁定时标记待重启删除。
        /// </summary>
        public static ExtensionInstallResult UninstallUserPlugin(IConverterPlugin plugin)
        {
            try
            {
                var location = plugin.GetType().Assembly.Location;
                if (string.IsNullOrEmpty(location)) return ExtensionInstallResult.Failed;

                var userDir = ConfigStore.UserPluginsDirectory;
                if (string.IsNullOrEmpty(userDir)) return ExtensionInstallResult.Failed;
                if (!location.StartsWith(userDir, StringComparison.OrdinalIgnoreCase)) return ExtensionInstallResult.Failed;

                var dir = Path.GetDirectoryName(location);
                if (string.IsNullOrEmpty(dir)) return ExtensionInstallResult.Failed;

                // DLL 直接在用户插件根目录时只删该文件，否则删整个目录；被锁定则暂存待重启
                var path = string.Equals(dir, userDir, StringComparison.OrdinalIgnoreCase) ? location : dir;
                return ExtensionCenter.DeleteOrStage(path);
            }
            catch
            {
                return ExtensionInstallResult.Failed;
            }
        }

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
