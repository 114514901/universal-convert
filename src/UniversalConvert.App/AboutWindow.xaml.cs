using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;

namespace UniversalConvert.App
{
    /// <summary>关于页面：应用信息、版本、第三方组件声明、支持的格式（按来源插件标注）。</summary>
    public partial class AboutWindow : Window
    {
        private const string ProjectUrl = "https://github.com/114514901/universal-convert";

        public AboutWindow(CoreHost host)
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            IconImage.Source = AppIcon.Get();
            VersionText.Text = string.Format(Strings.VersionFormat, AppVersion.Current?.ToString() ?? "?");
            DescText.Text = Strings.AboutDescription;
            ThirdPartyText.Text = Strings.ThirdPartyText;
            LicenseText.Text = Strings.LicenseText;
            LoadSupportedFormats(host);
        }

        /// <summary>
        /// 从格式注册表动态汇总所有支持的格式（输入 + 输出去重），标注来源插件。
        /// 内置插件与用户在线安装的扩展都会出现在注册表里，故无需硬编码。
        /// </summary>
        private void LoadSupportedFormats(CoreHost host)
        {
            if (host?.Registry == null) return;

            var formats = new Dictionary<string, FormatInfo>(StringComparer.OrdinalIgnoreCase);

            // 第一遍：输入格式（显示名优先用输入的，如「MP4 视频」）
            foreach (var e in host.Registry.Entries)
            {
                if (string.IsNullOrEmpty(e.InputExtension)) continue;
                FormatInfo info;
                if (!formats.TryGetValue(e.InputExtension, out info))
                {
                    info = new FormatInfo { Extension = e.InputExtension, DisplayName = e.InputDisplayName };
                    formats[e.InputExtension] = info;
                }
                AddSource(info, e.PluginName);
            }

            // 第二遍：输出格式（已有输入名的保留；纯输出格式用输出名）
            foreach (var e in host.Registry.Entries)
            {
                if (string.IsNullOrEmpty(e.OutputExtension)) continue;
                FormatInfo info;
                if (!formats.TryGetValue(e.OutputExtension, out info))
                {
                    info = new FormatInfo { Extension = e.OutputExtension, DisplayName = e.OutputDisplayName };
                    formats[e.OutputExtension] = info;
                }
                AddSource(info, e.PluginName);
            }

            FormatsList.ItemsSource = formats.Values
                .OrderBy(f => f.Extension, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddSource(FormatInfo info, string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) return;
            if (string.IsNullOrEmpty(info.Sources))
            {
                info.Sources = pluginName;
            }
            else if (!info.Sources.Split(',').Any(s => s.Trim().Equals(pluginName, StringComparison.OrdinalIgnoreCase)))
            {
                info.Sources += ", " + pluginName;
            }
        }

        private void OnOpenProject(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(ProjectUrl);
            }
            catch
            {
                // 忽略打开失败
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>关于窗口里「支持的格式」列表的一行。</summary>
    public class FormatInfo
    {
        /// <summary>扩展名（含点，小写），如 ".mp4"。</summary>
        public string Extension { get; set; }

        /// <summary>人类可读名称，如 "MP4 视频"。</summary>
        public string DisplayName { get; set; }

        /// <summary>来源插件名（逗号分隔，多个插件支持同一格式时并列）。</summary>
        public string Sources { get; set; }

        /// <summary>展示文本：显示名 + 扩展名。</summary>
        public string Format => string.IsNullOrEmpty(DisplayName)
            ? "." + Extension.ToUpperInvariant()
            : DisplayName + " (." + Extension + ")";
    }
}
