using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;

namespace UniversalConvert.App
{
    /// <summary>扩展管理器：展示已加载插件及其版本、兼容性警告与加载错误；可卸载用户扩展、批量检查扩展更新。</summary>
    public partial class PluginManagerWindow : Window
    {
        private readonly CoreHost _host;
        private readonly List<PluginRow> _rows = new List<PluginRow>();

        public PluginManagerWindow(CoreHost host)
        {
            InitializeComponent();
            _host = host;
            Icon = AppIcon.Get();
            Populate();
        }

        private void Populate()
        {
            _rows.Clear();
            foreach (var info in PluginManager.Inspect(_host))
            {
                var userPlugin = PluginManager.IsUserPlugin(info.Plugin);
                _rows.Add(new PluginRow
                {
                    Id = info.Plugin.Id,
                    Name = info.Plugin.Name,
                    Version = info.Plugin.Version ?? "?",
                    SourceText = userPlugin ? Strings.Extension : Strings.BuiltIn,
                    IsUserPlugin = userPlugin,
                    BaseStatus = GetStatusText(info)
                });
            }
            PluginList.ItemsSource = _rows;

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
            if (info.Plugin.IsUntested)
            {
                return Strings.StatusUntested;
            }

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

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var row = PluginList.SelectedItem as PluginRow;
            UninstallButton.IsEnabled = row != null && row.IsUserPlugin;
        }

        private void OnUninstall(object sender, RoutedEventArgs e)
        {
            var row = PluginList.SelectedItem as PluginRow;
            if (row == null || !row.IsUserPlugin) return;

            var confirm = MessageBox.Show(
                string.Format(Strings.UninstallConfirm, row.Name),
                "UniversalConvert", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var plugin = _host.Plugins.FirstOrDefault(p => p.Id == row.Id);
            if (plugin == null) return;

            if (PluginManager.UninstallUserPlugin(plugin))
            {
                UpdateStatusText.Text = string.Format(Strings.UninstalledRestart, row.Name);
                row.BaseStatus = Strings.UninstalledRestart; // 目录已删，重启后即消失
                RefreshList();
                UninstallButton.IsEnabled = false;
            }
        }

        private async void OnCheckUpdates(object sender, RoutedEventArgs e)
        {
            UpdateStatusText.Text = Strings.CheckingExtensionUpdates;
            CheckUpdatesButton.IsEnabled = false;
            try
            {
                var available = await ExtensionCenter.GetAvailableAsync();
                var byId = available.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

                int updated = 0;
                foreach (var row in _rows)
                {
                    if (CheckUpdateForRow(row, byId)) updated++;
                }

                UpdateStatusText.Text = updated > 0
                    ? string.Format(Strings.ExtensionUpdatesFound, updated)
                    : Strings.NoExtensionUpdates;
                RefreshList();
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = string.Format(Strings.LoadExtensionsFailed, ex.Message);
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private async void OnCheckUpdateSingle(object sender, RoutedEventArgs e)
        {
            var row = PluginList.SelectedItem as PluginRow;
            if (row == null || !row.IsUserPlugin) return;

            UpdateStatusText.Text = Strings.CheckingExtensionUpdates;
            try
            {
                var available = await ExtensionCenter.GetAvailableAsync();
                var byId = available.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

                UpdateStatusText.Text = CheckUpdateForRow(row, byId)
                    ? row.Name + "：" + row.UpdateText
                    : string.Format(Strings.ExtensionUpToDate, row.Name);
                RefreshList();
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = string.Format(Strings.LoadExtensionsFailed, ex.Message);
            }
        }

        private static bool CheckUpdateForRow(PluginRow row, IDictionary<string, ExtensionInfo> byId)
        {
            ExtensionInfo ext;
            if (!byId.TryGetValue(row.Id, out ext)) return false;

            var installed = SemVersion.Parse(row.Version);
            var latest = SemVersion.Parse(ext.Version);
            if (installed != null && latest != null && latest.CompareTo(installed) > 0)
            {
                row.UpdateText = string.Format(Strings.ExtensionUpdateFormat, ext.Version);
                return true;
            }
            return false;
        }

        private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 右键点击的行选中它，并据此启用/禁用右键菜单项
            var item = ItemsControl.ContainerFromElement(
                PluginList, e.OriginalSource as DependencyObject) as ListViewItem;
            if (item != null)
            {
                item.IsSelected = true;
                PluginList.SelectedItem = item.DataContext;
            }

            var row = PluginList.SelectedItem as PluginRow;
            bool isUser = row != null && row.IsUserPlugin;
            if (PluginList.ContextMenu != null)
            {
                foreach (var menuItem in PluginList.ContextMenu.Items.OfType<MenuItem>())
                {
                    menuItem.IsEnabled = isUser;
                }
            }
        }

        private void RefreshList()
        {
            PluginList.ItemsSource = null;
            PluginList.ItemsSource = _rows;
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
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public bool IsUserPlugin { get; set; }
        public string SourceText { get; set; }
        public string BaseStatus { get; set; }
        public string UpdateText { get; set; }

        public string Status => string.IsNullOrEmpty(UpdateText)
            ? BaseStatus
            : BaseStatus + "（" + UpdateText + "）";
    }
}
