using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
            // 多选支持：选中项中含用户扩展即可卸载
            UninstallButton.IsEnabled = PluginList.SelectedItems
                .Cast<PluginRow>()
                .Any(r => r.IsUserPlugin);
        }

        private void OnUninstall(object sender, RoutedEventArgs e)
        {
            // 批量卸载：处理所有选中且为用户扩展的行
            var rows = PluginList.SelectedItems.Cast<PluginRow>()
                .Where(r => r.IsUserPlugin)
                .ToList();
            if (rows.Count == 0) return;

            var confirmText = rows.Count == 1
                ? string.Format(Strings.UninstallConfirm, rows[0].Name)
                : string.Format(Strings.UninstallConfirmMany, rows.Count);
            var confirm = MessageBox.Show(confirmText, "UniversalConvert",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            bool staged = false;
            bool anyFailed = false;
            foreach (var row in rows)
            {
                var plugin = _host.Plugins.FirstOrDefault(p => p.Id == row.Id);
                if (plugin == null) continue;

                var result = PluginManager.UninstallUserPlugin(plugin);
                if (result == ExtensionInstallResult.Failed)
                {
                    anyFailed = true;
                    continue;
                }

                if (result == ExtensionInstallResult.StagedForRestart)
                {
                    staged = true;
                    row.BaseStatus = string.Format(Strings.UninstalledRestart, row.Name);
                }
                else
                {
                    row.BaseStatus = string.Format(Strings.Uninstalled, row.Name);
                }
            }

            if (anyFailed)
            {
                MessageBox.Show(Strings.ExtensionUninstallFailed, "UniversalConvert",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            UpdateStatusText.Text = rows.Count == 1
                ? rows[0].BaseStatus
                : (anyFailed
                    ? string.Format(Strings.UninstalledManyFailed, rows.Count)
                    : string.Format(Strings.UninstalledMany, rows.Count));
            RefreshList();
            UninstallButton.IsEnabled = false;

            // 有暂存待重启的卸载就提示重启
            if (staged)
            {
                AppRestart.PromptAndRestart();
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

                // 只检查用户扩展（内置插件不在扩展仓库）；并行比对
                foreach (var row in _rows) row.UpdateText = null;
                var userRows = _rows.Where(r => r.IsUserPlugin).ToList();
                int updated = 0;
                Parallel.ForEach(userRows, row =>
                {
                    if (CheckUpdateForRow(row, byId)) Interlocked.Increment(ref updated);
                });
                RefreshList();

                if (updated > 0)
                {
                    var confirm = MessageBox.Show(
                        string.Format(Strings.ExtensionUpdatesPrompt, updated),
                        "UniversalConvert", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm == MessageBoxResult.Yes)
                    {
                        // 弹出进度列表窗口并行更新（含下载进度与失败明细）
                        UpdateStatusText.Text = string.Empty;
                        var targets = userRows.Where(r => r.UpdateText != null)
                            .Select(r => byId[r.Id]).ToList();
                        var window = new ExtensionUpdateWindow(Strings.ExtensionUpdatingTitle, targets) { Owner = this };
                        window.ShowDialog();
                        UpdateStatusText.Text = window.Summary;
                    }
                    else
                    {
                        // 选「否」：保留行内「发现新版本」提示，状态栏不再停留「正在检查」
                        UpdateStatusText.Text = Strings.UpdateSkipped;
                    }
                }
                else
                {
                    UpdateStatusText.Text = Strings.NoExtensionUpdates;
                }
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

                if (!CheckUpdateForRow(row, byId))
                {
                    UpdateStatusText.Text = string.Format(Strings.ExtensionUpToDate, row.Name);
                    return;
                }

                ExtensionInfo ext;
                if (!byId.TryGetValue(row.Id, out ext)) return;

                var confirm = MessageBox.Show(
                    string.Format(Strings.SingleExtensionUpdatePrompt, ext.Version),
                    "UniversalConvert", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = Strings.UpdateSkipped;
                    return;
                }

                // 单插件更新同样走进度弹窗
                UpdateStatusText.Text = string.Empty;
                var window = new ExtensionUpdateWindow(Strings.ExtensionUpdatingTitle, new[] { ext }) { Owner = this };
                window.ShowDialog();
                UpdateStatusText.Text = window.Summary;
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
