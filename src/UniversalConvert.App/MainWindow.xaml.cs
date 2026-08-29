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
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;

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
            OutputDirText.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

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

            // 手动检查发现比横幅更新的版本时，同步刷新横幅
            var found = window.LatestFound;
            if (found != null && IsNewerThan(found.Version, _updateInfo?.Version))
            {
                _updateInfo = found;
                UpdateBannerText.Text = string.Format(Strings.UpdateAvailable, found.Version);
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }

        private static bool IsNewerThan(string candidate, string current)
        {
            if (string.IsNullOrEmpty(current)) return true;
            var c = SemVersion.Parse(candidate);
            var cur = SemVersion.Parse(current);
            return c != null && cur != null && c.CompareTo(cur) > 0;
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
                await UpdateChecker.DownloadAsync(_updateInfo.DownloadUrl, dest, progress, CancellationToken.None, _updateInfo.Sha256);

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
            { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".opus" };

        /// <summary>
        /// 是否可用内置播放器预览：原生可播格式，或某插件（IPreviewProvider）声明支持该扩展名
        /// （如 .mid/.midi 由 MIDI 扩展渲染预览）。
        /// </summary>
        private bool CanPreviewAudio(string path)
        {
            var ext = Path.GetExtension(path);
            if (PlayableAudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return true;

            if (_host?.Plugins != null)
            {
                return _host.Plugins.OfType<IPreviewProvider>().Any(p =>
                    p.SupportedPreviewExtensions != null
                    && p.SupportedPreviewExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
            }
            return false;
        }

        /// <summary>统一预览入口：按设置「预览播放器」分流（系统 = 直接系统默认，内置 = 内置预览器）。</summary>
        private void PreviewFile(string path)
        {
            if (!File.Exists(path)) return;

            var mode = _settingsManager.Get("previewPlayer");
            if (string.Equals(mode, "system", StringComparison.OrdinalIgnoreCase))
            {
                try { Process.Start(path); } catch { }
                return;
            }

            if (CanPreviewAudio(path))
            {
                // 媒体预览提供者（如 VLC 扩展）优先接管音频预览；没有/拒绝则用内置音频播放器
                if (!TryAudioPreviewProvider(path))
                {
                    var window = new AudioPlayerWindow(path, _host) { Owner = this };
                    window.Show();
                }
            }
            else if (CanPreviewImage(path))
            {
                var window = new ImagePreviewWindow(path) { Owner = this };
                window.Show();
            }
            else if (CanPreviewVideo(path))
            {
                // 优先交给扩展的视频预览提供者（如 VLC 扩展）；没有或拒绝则用内置预览
                if (!TryVideoPreviewProvider(path))
                {
                    var window = new VideoPreviewWindow(path) { Owner = this };
                    window.Show();
                }
            }
            else if (CanPreviewText(path))
            {
                var window = new TextPreviewWindow(path) { Owner = this };
                window.Show();
            }
            else
            {
                try { Process.Start(path); } catch { }
            }
        }

        /// <summary>尝试让媒体预览提供者扩展接管音频预览；无提供者/拒绝/异常时返回 false 回退内置。</summary>
        private bool TryAudioPreviewProvider(string path)
        {
            var ext = Path.GetExtension(path);
            foreach (var provider in _host.Plugins.OfType<IMediaPreviewProvider>())
            {
                try
                {
                    if (provider.CanPreviewAudio(ext))
                    {
                        Log.Info($"音频预览提供者接管: {provider.GetType().FullName} ({path})");
                        if (provider.ShowPreview(path))
                        {
                            return true;
                        }
                        Log.Info($"音频预览提供者拒绝接管: {provider.GetType().FullName}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"音频预览提供者异常: {provider.GetType().FullName}: {ex}");
                }
            }
            return false;
        }

        /// <summary>尝试让视频预览提供者扩展接管预览；无提供者/拒绝/异常时返回 false 回退内置。</summary>
        private bool TryVideoPreviewProvider(string path)
        {
            var ext = Path.GetExtension(path);
            foreach (var provider in _host.Plugins.OfType<IVideoPreviewProvider>())
            {
                try
                {
                    if (provider.CanPreviewVideo(ext))
                    {
                        Log.Info($"视频预览提供者接管: {provider.GetType().FullName} ({path})");
                        if (provider.ShowPreview(path))
                        {
                            return true;
                        }
                        Log.Info($"视频预览提供者拒绝接管: {provider.GetType().FullName}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"视频预览提供者异常: {provider.GetType().FullName}: {ex}");
                }
            }
            return false;
        }

        private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = FileList.SelectedItem as FileRow;
            if (row == null) return;
            PreviewFile(row.Path);
        }

        /// <summary>内置预览支持的图片扩展名（WPF 直接解码或 ffmpeg 可转码）。</summary>
        private static readonly string[] PreviewableImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
            ".ico", ".webp", ".heic", ".heif", ".avif", ".psd", ".tga"
        };

        private static bool CanPreviewImage(string path)
        {
            var ext = Path.GetExtension(path);
            return PreviewableImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>内置预览支持的视频扩展名（MediaElement 播放；解码失败的回落系统播放器）。</summary>
        private static readonly string[] PreviewableVideoExtensions =
        {
            ".mp4", ".m4v", ".avi", ".wmv", ".mov", ".mpg", ".mpeg", ".ts",
            ".mkv", ".webm", ".flv", ".3gp", ".ogv", ".rmvb"
        };

        private static bool CanPreviewVideo(string path)
        {
            var ext = Path.GetExtension(path);
            return PreviewableVideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>内置预览支持的文本扩展名（JSON 带语法高亮）。</summary>
        private static readonly string[] PreviewableTextExtensions =
        {
            ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".csv", ".ini", ".cfg",
            ".srt", ".lrc", ".yaml", ".yml", ".css", ".js", ".py", ".bat", ".cmd", ".ps1"
        };

        private static bool CanPreviewText(string path)
        {
            var ext = Path.GetExtension(path);
            return PreviewableTextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
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
            if (row == null) return;
            PreviewFile(row.Path);
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
            var seenOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in baseEntries)
            {
                // 同一方向被多个插件注册时只显示一次（具体用哪个在转换时由 FormatResolver 决定）
                if (!seenOutputs.Add(entry.OutputExtension)) continue;

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
            var workerThreads = ResolveWorkerThreads(_settingsManager.Get("workerThreads"));

            var perFileOptions = new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _files)
            {
                if (row.Options != null) perFileOptions[row.Path] = row.Options;
            }

            var files = _files.Select(r => r.Path).ToArray();
            var outputDir = OutputDirText.Text?.Trim();
            var window = new BatchConvertWindow(_host, _settingsManager, files, targetExt, workerThreads, perFileOptions, outputDir) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>解析处理线程数设置；"auto" 按逻辑核心数 × 75%（四舍五入），低于 4 核用 1。</summary>
        private static int ResolveWorkerThreads(string setting)
        {
            if (string.Equals(setting, "auto", StringComparison.OrdinalIgnoreCase))
            {
                int cores = Environment.ProcessorCount;
                if (cores < 4) return 1;
                return Math.Max(1, (int)Math.Round(cores * 0.75, MidpointRounding.AwayFromZero));
            }
            int parsed;
            return int.TryParse(setting, out parsed) ? Math.Max(1, parsed) : 2;
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
