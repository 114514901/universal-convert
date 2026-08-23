using System;
using System.Collections.Generic;

namespace UniversalConvert.Core.Config
{
    /// <summary>应用配置。持久化为 JSON（%AppData%\UniversalConvert\config.json）。</summary>
    public sealed class AppConfig
    {
        /// <summary>安装目录（App.exe 所在目录）。</summary>
        public string InstallDirectory { get; set; }

        /// <summary>插件目录；为空时默认取 InstallDirectory\plugins。</summary>
        public string PluginsDirectory { get; set; }

        /// <summary>外部工具路径表：工具名 -> 可执行文件绝对路径，如 "ffmpeg" -> "C:\tools\ffmpeg.exe"。</summary>
        public IDictionary<string, string> ToolPaths { get; set; }

        /// <summary>用户/插件设置项：键 -> 值。设置界面与插件都通过它读写。</summary>
        public IDictionary<string, string> Settings { get; set; }

        public AppConfig()
        {
            ToolPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ResolvePluginsDirectory()
        {
            if (!string.IsNullOrEmpty(PluginsDirectory)) return PluginsDirectory;
            if (!string.IsNullOrEmpty(InstallDirectory))
            {
                return System.IO.Path.Combine(InstallDirectory, "plugins");
            }
            return null;
        }
    }
}
