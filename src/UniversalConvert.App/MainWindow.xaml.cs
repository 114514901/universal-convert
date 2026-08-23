using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;

namespace UniversalConvert.App
{
    public partial class MainWindow : Window
    {
        private readonly CoreHost _host;
        private readonly List<string> _files = new List<string>();
        private ConversionEntry[] _commonTargets = new ConversionEntry[0];
        private UpdateInfo _updateInfo;

        public MainWindow(CoreHost host, string[] initialFiles = null)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _host = host;

            if (initialFiles != null)
            {
                foreach (var f in initialFiles)
                {
                    if (File.Exists(f)) _files.Add(f);
                }
            }

            RefreshFileList();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (IsAdministrator())
            {
                AdminBanner.Visibility = Visibility.Visible;
            }

            _updateInfo = await UpdateChecker.CheckAsync();
            if (_updateInfo != null)
            {
                UpdateBannerText.Text = string.Format(Strings.UpdateAvailable, _updateInfo.Version);
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }

        private void OnViewReleaseNotes(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.Url)) return;
            try
            {
                Process.Start(_updateInfo.Url);
            }
            catch
            {
                // 忽略打开失败
            }
        }

        private async void OnDownloadUpdate(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.DownloadUrl)) return;

            UpdateButton.IsEnabled = false;
            ViewNotesButton.IsEnabled = false;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateStatusText.Visibility = Visibility.Visible;

            var dest = Path.Combine(
                Path.GetTempPath(),
                "UniversalConvert-Setup-" + _updateInfo.Version.TrimStart('v', 'V') + ".exe");

            var progress = new Progress<double>(p =>
            {
                UpdateProgressBar.Value = p;
                UpdateStatusText.Text = string.Format(Strings.Downloading, (int)p);
            });

            try
            {
                await UpdateChecker.DownloadAsync(_updateInfo.DownloadUrl, dest, progress, CancellationToken.None);

                UpdateProgressBar.Value = 100;
                UpdateStatusText.Text = Strings.DownloadComplete;
                try
                {
                    Process.Start(dest);
                }
                catch
                {
                    // 启动安装程序失败
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = string.Format(Strings.DownloadFailed, ex.Message);
                UpdateButton.IsEnabled = true;
                ViewNotesButton.IsEnabled = true;
            }
        }

        private void OnAddFiles(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Strings.SelectFileDialogTitle,
                Filter = Strings.AllFilesFilter,
                Multiselect = true
            };
            if (dialog.ShowDialog() == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                AddFiles(files);
            }
        }

        private void AddFiles(IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                if (File.Exists(f) && !_files.Contains(f, StringComparer.OrdinalIgnoreCase))
                {
                    _files.Add(f);
                }
            }
            RefreshFileList();
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            var indices = FileList.SelectedItems.Cast<string>().ToList();
            foreach (var name in indices)
            {
                var idx = FindIndexByName(name);
                if (idx >= 0) _files.RemoveAt(idx);
            }
            RefreshFileList();
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            _files.Clear();
            RefreshFileList();
        }

        private int FindIndexByName(string fileName)
        {
            for (int i = 0; i < _files.Count; i++)
            {
                if (Path.GetFileName(_files[i]) == fileName) return i;
            }
            return -1;
        }

        private void RefreshFileList()
        {
            FileList.ItemsSource = _files.Select(Path.GetFileName).ToList();
            RefreshTargetFormats();
        }

        private void RefreshTargetFormats()
        {
            _commonTargets = ComputeCommonTargets();

            OutputCombo.ItemsSource = _commonTargets.Select(c =>
                $"{c.OutputDisplayName}  (.{c.OutputExtension})").ToList();

            OutputCombo.SelectedIndex = _commonTargets.Length > 0 ? 0 : -1;

            NoCommonFormatText.Visibility =
                (_files.Count > 0 && _commonTargets.Length == 0)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            UpdateButtons();
        }

        private ConversionEntry[] ComputeCommonTargets()
        {
            if (_files.Count == 0) return new ConversionEntry[0];

            var baseEntries = _host.Registry.GetConversionsFor(Path.GetExtension(_files[0])).ToList();
            var result = new List<ConversionEntry>();

            foreach (var entry in baseEntries)
            {
                bool supportedByAll = true;
                for (int i = 1; i < _files.Count; i++)
                {
                    if (_host.Registry.GetEntry(Path.GetExtension(_files[i]), entry.OutputExtension) == null)
                    {
                        supportedByAll = false;
                        break;
                    }
                }
                if (supportedByAll) result.Add(entry);
            }

            return result.ToArray();
        }

        private void OnOutputSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasSelection = OutputCombo.SelectedIndex >= 0 && _commonTargets.Length > 0;
            bool singleFile = _files.Count == 1;

            ConvertButton.IsEnabled = hasSelection;

            CustomizeButton.IsEnabled = hasSelection && singleFile
                && _commonTargets[OutputCombo.SelectedIndex].HasCustomizableOptions;
        }

        private void OnCustomize(object sender, RoutedEventArgs e)
        {
            if (_files.Count != 1 || OutputCombo.SelectedIndex < 0)
            {
                MessageBox.Show(Strings.PleaseSelectFormat, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entry = _commonTargets[OutputCombo.SelectedIndex];
            if (!entry.HasCustomizableOptions)
            {
                MessageBox.Show(Strings.NoParamsMessage,
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new CustomizeWindow(_host.Engine, _files[0], entry) { Owner = this };
            window.ShowDialog();
        }

        private void OnConvert(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0 || OutputCombo.SelectedIndex < 0)
            {
                MessageBox.Show(Strings.PleaseSelectFormat, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetExt = _commonTargets[OutputCombo.SelectedIndex].OutputExtension;
            var window = new BatchConvertWindow(_host, _files.ToArray(), targetExt) { Owner = this };
            window.ShowDialog();
        }

        private static bool IsAdministrator()
        {
            try
            {
                var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
