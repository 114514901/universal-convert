using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;

namespace UniversalConvert.App
{
    public partial class ExtensionCenterWindow : Window
    {
        private readonly List<ExtensionInfo> _extensions = new List<ExtensionInfo>();

        public ExtensionCenterWindow()
        {
            InitializeComponent();
            Icon = AppIcon.Get();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void OnRefresh(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            StatusText.Text = Strings.LoadingExtensions;
            DescText.Text = string.Empty;

            try
            {
                _extensions.Clear();
                var available = await ExtensionCenter.GetAvailableAsync();
                _extensions.AddRange(available);

                var rows = _extensions.Select(BuildRow).ToList();
                ExtensionList.ItemsSource = rows;

                var selected = ExtensionList.SelectedItem as ExtensionRow;
                DescText.Text = selected?.Info?.Description ?? string.Empty;
                StatusText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Strings.LoadExtensionsFailed, ex.Message);
            }
        }

        private ExtensionRow BuildRow(ExtensionInfo info)
        {
            var installedVersion = ExtensionCenter.GetInstalledVersion(info);

            var row = new ExtensionRow
            {
                Info = info,
                InstalledText = installedVersion == null
                    ? Strings.NotInstalled
                    : string.Format(Strings.InstalledVersionFormat, installedVersion),
                CompatibilityText = GetCompatibilityText(info),
                SizeText = FormatSize(info.Size)
            };
            return row;
        }

        /// <summary>把字节数格式化为人类可读体积（未知则留空）。</summary>
        private static string FormatSize(long? bytes)
        {
            if (bytes == null || bytes <= 0) return string.Empty;
            const long kb = 1024, mb = 1024 * 1024, gb = 1024 * 1024 * 1024;
            if (bytes >= gb) return (bytes.Value / (double)gb).ToString("0.0") + " GB";
            if (bytes >= mb) return (bytes.Value / (double)mb).ToString("0") + " MB";
            if (bytes >= kb) return (bytes.Value / (double)kb).ToString("0") + " KB";
            return bytes + " B";
        }

        private static string GetCompatibilityText(ExtensionInfo info)
        {
            switch (PluginManager.CheckCompatibility(info.MinAppVersion, info.MaxAppVersion))
            {
                case PluginCompatibility.AppTooOld:
                    return string.Format(Strings.StatusAppTooOld, info.MinAppVersion);
                case PluginCompatibility.Unverified:
                    return string.Format(Strings.StatusUnverified, info.MaxAppVersion);
                default:
                    return Strings.StatusCompatible;
            }
        }

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var rows = ExtensionList.SelectedItems.Cast<ExtensionRow>().ToList();
            DescText.Text = rows.Count > 0 ? rows[0].Info.Description : string.Empty;

            // 卸载仍针对当前选中行（SelectedItem）
            var row = ExtensionList.SelectedItem as ExtensionRow;
            var installedVersion = row != null ? ExtensionCenter.GetInstalledVersion(row.Info) : null;
            UninstallButton.IsEnabled = installedVersion != null;

            // 安装/更新：多选时统计所有可操作的项，按状态显示按钮文字
            var actionable = rows.Where(CanInstallOrUpdate).ToList();
            InstallButton.IsEnabled = actionable.Count > 0;
            if (actionable.Count == 0)
            {
                InstallButton.Content = Strings.Install;
            }
            else if (actionable.All(r => ExtensionCenter.GetInstalledVersion(r.Info) != null))
            {
                InstallButton.Content = Strings.Update;
            }
            else if (actionable.All(r => ExtensionCenter.GetInstalledVersion(r.Info) == null))
            {
                InstallButton.Content = Strings.Install;
            }
            else
            {
                InstallButton.Content = Strings.InstallUpdate;
            }
        }

        /// <summary>该扩展当前可安装（未安装）或可更新（有新版本）。</summary>
        private static bool CanInstallOrUpdate(ExtensionRow row)
        {
            var installed = ExtensionCenter.GetInstalledVersion(row.Info);
            if (installed == null) return true;
            var newer = SemVersion.Parse(row.Info.Version)?.CompareTo(SemVersion.Parse(installed)) > 0;
            return newer == true;
        }

        private async void OnInstall(object sender, RoutedEventArgs e)
        {
            // 多选批量安装/更新：复用进度列表弹窗（并行下载）
            var rows = ExtensionList.SelectedItems.Cast<ExtensionRow>()
                .Where(CanInstallOrUpdate)
                .ToList();
            if (rows.Count == 0) return;

            var allUpdates = rows.All(r => ExtensionCenter.GetInstalledVersion(r.Info) != null);
            var title = allUpdates ? Strings.ExtensionUpdatingTitle : Strings.ExtensionInstallingTitle;

            var window = new ExtensionUpdateWindow(title, rows.Select(r => r.Info)) { Owner = this };
            window.ShowDialog();
            StatusText.Text = window.Summary;
            await RefreshAsync();
        }

        private void OnUninstall(object sender, RoutedEventArgs e)
        {
            // 支持多选批量卸载：处理所有选中且已安装的扩展
            var rows = ExtensionList.SelectedItems.Cast<ExtensionRow>()
                .Where(r => ExtensionCenter.GetInstalledVersion(r.Info) != null)
                .ToList();
            if (rows.Count == 0) return;

            var confirmText = rows.Count == 1
                ? string.Format(Strings.UninstallConfirm, rows[0].Info.Name)
                : string.Format(Strings.UninstallConfirmMany, rows.Count);
            var confirm = MessageBox.Show(confirmText, "UniversalConvert",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            bool staged = false;
            var first = rows[0];
            foreach (var row in rows)
            {
                var result = ExtensionCenter.Uninstall(row.Info);
                if (result == ExtensionInstallResult.StagedForRestart)
                {
                    staged = true;
                }
            }

            StatusText.Text = rows.Count == 1
                ? (staged
                    ? string.Format(Strings.UninstalledRestart, first.Info.Name)
                    : string.Format(Strings.Uninstalled, first.Info.Name))
                : string.Format(Strings.UninstalledMany, rows.Count);
            _ = RefreshAsync();

            // 有暂存待重启的卸载（插件已加载被锁定）就提示重启
            if (staged)
            {
                AppRestart.PromptAndRestart();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public sealed class ExtensionRow
    {
        public ExtensionInfo Info { get; set; }
        public string Name => Info.Name;
        public string Version => Info.Version;
        public string InstalledText { get; set; }
        public string CompatibilityText { get; set; }
        public string SizeText { get; set; }
    }
}
