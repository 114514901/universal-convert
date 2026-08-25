using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly SettingsManager _settingsManager;
        private readonly ObservableCollection<FileRow> _files = new ObservableCollection<FileRow>();
        private ConversionEntry[] _commonTargets = new ConversionEntry[0];
        private UpdateInfo _updateInfo;

        public MainWindow(CoreHost host, SettingsManager settingsManager, string[] initialFiles = null)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _host = host;
            _settingsManager = settingsManager;

            FileList.ItemsSource = _files;
            OutputDirText.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

            if (initialFiles != null)
            {
                AddFiles(initialFiles);
            }

            RefreshFileList();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (AppVersion.Current?.IsPrerelease == true)
            {
                DevBanner.Visibility = Visibility.Visible;
            }

            if (IsAdministrator())
            {
                AdminBanner.Visibility = Visibility.Visible;
            }

            // 后台异步检查更新，不阻塞启动（网络/代理问题不影响界面加载）
            _ = CheckUpdateAsync();
        }

        private async Task CheckUpdateAsync()
        {
            _updateInfo = await UpdateChecker.CheckAsync(_settingsManager.Get("updateChannel"));
            if (_updateInfo != null)
            {
                UpdateBannerText.Text = string.Format(Strings.UpdateAvailable, _updateInfo.Version);
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }

        private void OnOpenPlugins(object sender, RoutedEventArgs e)
        {
            var window = new PluginManagerWindow(_host) { Owner = this };
            window.ShowDialog();
        }

        private void OnOpenAbout(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow(_host) { Owner = this };
            window.ShowDialog();
        }

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow(_settingsManager) { Owner = this };
            window.ShowDialog();
        }

        private void OnViewReleaseNotes(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null) return;
            var window = new ReleaseNotesWindow(_updateInfo.Version, _updateInfo.Body) { Owner = this };
            window.ShowDialog();
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
                    LaunchInstallerSilent(dest);
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

        private void LaunchInstallerSilent(string installerPath)
        {
            var installDir = (_host.Config.InstallDirectory ?? AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd('\\');

            var psi = new ProcessStartInfo(installerPath)
            {
                Arguments = "/SILENT /NORESTART /MERGETASKS=runapp /DIR=\"" + installDir + "\"",
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
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
                if (!File.Exists(f)) continue;
                if (_files.Any(r => string.Equals(r.Path, f, StringComparison.OrdinalIgnoreCase))) continue;

                _files.Add(new FileRow
                {
                    Path = f,
                    FileName = Path.GetFileName(f),
                    Format = Path.GetExtension(f),
                    CustomizeLabel = Strings.Original
                });
            }
            RefreshFileList();
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            var rows = FileList.SelectedItems.Cast<FileRow>().ToList();
            foreach (var row in rows)
            {
                _files.Remove(row);
            }
            RefreshFileList();
        }

        private static readonly string[] PlayableAudioExtensions =
            { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma" };

        private static bool IsPlayableAudio(string path)
        {
            var ext = Path.GetExtension(path);
            return PlayableAudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = FileList.SelectedItem as FileRow;
            if (row == null || !IsPlayableAudio(row.Path)) return;

            var window = new AudioPlayerWindow(row.Path) { Owner = this };
            window.Show();
        }

        private void OnFileListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                FileList.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                OnRemoveSelected(null, null);
                e.Handled = true;
            }
        }

        private void OnCustomizeContext(object sender, RoutedEventArgs e)
        {
            CustomizeSelected();
        }

        private void OnRemoveContext(object sender, RoutedEventArgs e)
        {
            OnRemoveSelected(sender, e);
        }

        private void OnPreviewContext(object sender, RoutedEventArgs e)
        {
            var row = FileList.SelectedItem as FileRow;
            if (row == null || !File.Exists(row.Path)) return;

            if (IsPlayableAudio(row.Path))
            {
                var window = new AudioPlayerWindow(row.Path) { Owner = this };
                window.Show();
            }
            else
            {
                try { Process.Start(row.Path); } catch { }
            }
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            _files.Clear();
            RefreshFileList();
        }

        private void RefreshFileList()
        {
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

            var baseEntries = _host.Registry.GetConversionsFor(_files[0].Format).ToList();
            var result = new List<ConversionEntry>();

            foreach (var entry in baseEntries)
            {
                bool supportedByAll = true;
                for (int i = 1; i < _files.Count; i++)
                {
                    if (_host.Registry.GetEntry(_files[i].Format, entry.OutputExtension) == null)
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
            bool hasFileSelected = FileList.SelectedItem != null;

            ConvertButton.IsEnabled = hasSelection;

            CustomizeButton.IsEnabled = hasSelection && hasFileSelected
                && _commonTargets[OutputCombo.SelectedIndex].HasCustomizableOptions;

            bool isUntested = hasSelection && _commonTargets[OutputCombo.SelectedIndex].IsUntested;
            UntestedWarningText.Visibility = isUntested ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void CustomizeSelected()
        {
            var row = FileList.SelectedItem as FileRow;
            if (row == null) return;
            if (OutputCombo.SelectedIndex < 0)
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

            var result = CustomizeWindow.Collect(this, entry, row.Path, row.Options);
            if (result != null)
            {
                row.Options = result.Options;
                row.CustomizeLabel = result.Label;
            }
        }

        private void OnCustomize(object sender, RoutedEventArgs e)
        {
            CustomizeSelected();
        }

        private void OnBrowseOutput(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = Strings.OutputLocation;
                if (!string.IsNullOrEmpty(OutputDirText.Text))
                {
                    dialog.SelectedPath = OutputDirText.Text;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    OutputDirText.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnConvert(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0 || OutputCombo.SelectedIndex < 0)
            {
                MessageBox.Show(Strings.PleaseSelectFormat, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetExt = _commonTargets[OutputCombo.SelectedIndex].OutputExtension;
            int workerThreads;
            if (!int.TryParse(_settingsManager.Get("workerThreads"), out workerThreads))
            {
                workerThreads = 2;
            }

            var perFileOptions = new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _files)
            {
                if (row.Options != null) perFileOptions[row.Path] = row.Options;
            }

            var files = _files.Select(r => r.Path).ToArray();
            var outputDir = OutputDirText.Text?.Trim();
            var window = new BatchConvertWindow(_host, files, targetExt, workerThreads, perFileOptions, outputDir) { Owner = this };
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

    /// <summary>主界面文件列表的一行。</summary>
    public class FileRow : INotifyPropertyChanged
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public string Format { get; set; }

        private string _customizeLabel;
        public string CustomizeLabel
        {
            get { return _customizeLabel; }
            set
            {
                _customizeLabel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomizeLabel)));
            }
        }

        /// <summary>该文件的自定义参数；null 表示未自定义。</summary>
        public IDictionary<string, string> Options { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
