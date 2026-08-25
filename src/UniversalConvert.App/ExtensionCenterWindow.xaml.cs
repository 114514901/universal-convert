using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
                CompatibilityText = GetCompatibilityText(info)
            };
            return row;
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
            var row = ExtensionList.SelectedItem as ExtensionRow;
            DescText.Text = row?.Info?.Description ?? string.Empty;

            if (row == null)
            {
                InstallButton.IsEnabled = false;
                UninstallButton.IsEnabled = false;
                return;
            }

            var installedVersion = ExtensionCenter.GetInstalledVersion(row.Info);
            UninstallButton.IsEnabled = installedVersion != null;

            if (installedVersion == null)
            {
                InstallButton.IsEnabled = true;
                InstallButton.Content = Strings.Install;
            }
            else
            {
                var newer = SemVersion.Parse(row.Info.Version)?.CompareTo(SemVersion.Parse(installedVersion)) > 0;
                if (newer == true)
                {
                    InstallButton.IsEnabled = true;
                    InstallButton.Content = Strings.Update;
                }
                else
                {
                    InstallButton.IsEnabled = false;
                    InstallButton.Content = Strings.Install;
                }
            }
        }

        private async void OnInstall(object sender, RoutedEventArgs e)
        {
            var row = ExtensionList.SelectedItem as ExtensionRow;
            if (row == null) return;

            SetBusy(true);
            try
            {
                var progress = new Progress<double>(p =>
                {
                    Progress.Value = p;
                    StatusText.Text = string.Format(Strings.Downloading, (int)p);
                });

                StatusText.Text = Strings.Installing;
                var result = await ExtensionCenter.InstallAsync(row.Info, progress, CancellationToken.None);

                if (result == ExtensionInstallResult.StagedForRestart)
                {
                    // 更新已加载插件：已暂存，重启后生效
                    StatusText.Text = string.Format(Strings.UpdatedRestart, row.Info.Name);
                    await RefreshAsync();
                    AppRestart.PromptAndRestart();
                }
                else if (result == ExtensionInstallResult.Installed)
                {
                    StatusText.Text = Strings.InstallDone;
                    await RefreshAsync();
                    // 新安装的扩展也要重启才会被应用加载，同样询问是否立即重启
                    AppRestart.PromptAndRestart();
                }
                else
                {
                    StatusText.Text = Strings.ExtensionUpdateFailed;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Strings.InstallFailed, ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnUninstall(object sender, RoutedEventArgs e)
        {
            var row = ExtensionList.SelectedItem as ExtensionRow;
            if (row == null) return;

            var confirm = MessageBox.Show(
                string.Format(Strings.UninstallConfirm, row.Info.Name),
                "UniversalConvert", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            var result = ExtensionCenter.Uninstall(row.Info);
            if (result == ExtensionInstallResult.StagedForRestart)
            {
                // 插件已加载、目录被锁定：已标记待卸载，重启后删除
                StatusText.Text = string.Format(Strings.UninstalledRestart, row.Info.Name);
                _ = RefreshAsync();
                AppRestart.PromptAndRestart();
            }
            else if (result == ExtensionInstallResult.Installed)
            {
                StatusText.Text = string.Format(Strings.Uninstalled, row.Info.Name);
                _ = RefreshAsync();
            }
        }

        private void SetBusy(bool busy)
        {
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.IsEnabled = !busy;
            UninstallButton.IsEnabled = !busy;
            ExtensionList.IsEnabled = !busy;
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
    }
}
