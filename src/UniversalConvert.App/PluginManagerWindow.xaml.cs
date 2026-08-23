using System.Collections.Generic;
using System.Text;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;

namespace UniversalConvert.App
{
    /// <summary>扩展管理器：展示已加载插件及其版本、兼容性警告与加载错误。</summary>
    public partial class PluginManagerWindow : Window
    {
        private readonly CoreHost _host;

        public PluginManagerWindow(CoreHost host)
        {
            InitializeComponent();
            _host = host;
            Icon = AppIcon.Get();
            Populate();
        }

        private void Populate()
        {
            var rows = new List<PluginRow>();
            foreach (var info in PluginManager.Inspect(_host))
            {
                rows.Add(new PluginRow
                {
                    Name = info.Plugin.Name,
                    Version = info.Plugin.Version ?? "?",
                    Status = GetStatusText(info)
                });
            }
            PluginList.ItemsSource = rows;

            if (_host.LoadErrors != null && _host.LoadErrors.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var error in _host.LoadErrors)
                {
                    sb.AppendLine(error.File + ": " + error.Message);
                }
                ErrorBox.Text = sb.ToString();
                ErrorExpander.Visibility = Visibility.Visible;
            }
        }

        private static string GetStatusText(PluginInfo info)
        {
            switch (info.Compatibility)
            {
                case PluginCompatibility.AppTooOld:
                    return string.Format(Strings.StatusAppTooOld, info.Plugin.MinAppVersion);
                case PluginCompatibility.Unverified:
                    return string.Format(Strings.StatusUnverified, info.Plugin.MaxAppVersion);
                default:
                    return Strings.StatusCompatible;
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnOpenExtensions(object sender, RoutedEventArgs e)
        {
            var window = new ExtensionCenterWindow { Owner = this };
            window.ShowDialog();
        }
    }

    public class PluginRow
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Status { get; set; }
    }
}
