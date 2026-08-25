using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;

namespace UniversalConvert.App
{
    public partial class BatchConvertWindow : Window
    {
        private readonly CoreHost _host;
        private readonly SettingsManager _settingsManager;
        private readonly string[] _files;
        private readonly string _targetExt;
        private readonly int _workerThreads;
        private readonly string _outputDir;
        private readonly ObservableCollection<BatchItem> _items = new ObservableCollection<BatchItem>();
        private readonly List<string> _outputPaths = new List<string>();
        private readonly StringBuilder _errorLog = new StringBuilder();
        private readonly Dispatcher _dispatcher;
        private readonly object _outputLock = new object();
        private readonly Dictionary<string, ConversionEntry> _entryCache =
            new Dictionary<string, ConversionEntry>(StringComparer.OrdinalIgnoreCase);

        private double[] _progressByIndex;
        private int _doneCount;
        private int _failedCount;

        public BatchConvertWindow(CoreHost host, SettingsManager settingsManager, string[] files, string targetExt, int workerThreads,
            IDictionary<string, IDictionary<string, string>> perFileOptions = null, string outputDir = null)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _host = host;
            _settingsManager = settingsManager;
            _files = files;
            _targetExt = targetExt;
            _workerThreads = Math.Max(1, workerThreads);
            _outputDir = outputDir;
            _dispatcher = Dispatcher.CurrentDispatcher;

            TitleText.Text = Strings.BatchTitle + "  →  ." + targetExt;

            foreach (var f in files)
            {
                IDictionary<string, string> options = null;
                if (perFileOptions != null) perFileOptions.TryGetValue(f, out options);
                _items.Add(new BatchItem
                {
                    FileName = Path.GetFileName(f),
                    Status = Strings.StatusPending,
                    Options = options
                });
            }
            ItemList.ItemsSource = _items;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RunAsync();
        }

        private async Task RunAsync()
        {
            _progressByIndex = new double[_files.Length];
            _doneCount = 0;
            _failedCount = 0;

            var semaphore = new SemaphoreSlim(_workerThreads);
            var tasks = new List<Task>();
            for (int i = 0; i < _files.Length; i++)
            {
                int index = i;
                tasks.Add(ProcessOneAsync(index, semaphore));
            }

            await Task.WhenAll(tasks);

            OverallProgress.Value = 100;
            SummaryText.Text = string.Format(Strings.BatchSummary, _doneCount, _files.Length, _failedCount);
            CloseButton.IsEnabled = true;

            if (_outputPaths.Count > 0) OpenFolderButton.Visibility = Visibility.Visible;
            if (_failedCount > 0)
            {
                ErrorLogBox.Text = _errorLog.ToString();
                ErrorExpander.Visibility = Visibility.Visible;
                CopyErrorButton.Visibility = Visibility.Visible;
            }
        }

        private async Task ProcessOneAsync(int index, SemaphoreSlim semaphore)
        {
            var file = _files[index];
            var item = _items[index];

            var entry = ResolveEntry(Path.GetExtension(file));
            if (entry == null)
            {
                RunOnUi(() => item.Status = Strings.StatusSkipped);
                return;
            }

            await semaphore.WaitAsync();
            try
            {
                RunOnUi(() => item.Status = Strings.StatusConverting);

                var options = item.Options ?? PresetMerger.Merge(entry.Options, entry.Presets, null, null);
                var request = new ConversionRequest
                {
                    PluginId = entry.PluginId,
                    InputPath = file,
                    OutputPath = ResolveOutputPath(file),
                    InputExtension = Path.GetExtension(file),
                    OutputExtension = "." + _targetExt,
                    Options = options
                };

                var progress = new Progress<ConversionProgress>(p =>
                {
                    if (p.Percentage >= 0)
                    {
                        _progressByIndex[index] = p.Percentage;
                        UpdateOverallProgress();
                    }
                    if (!string.IsNullOrEmpty(p.Message))
                    {
                        SummaryText.Text = Path.GetFileName(file) + ": " + p.Message;
                    }
                });

                var result = await _host.Engine.ConvertAsync(request, progress, CancellationToken.None);

                if (result.Success)
                {
                    _progressByIndex[index] = 100;
                    Interlocked.Increment(ref _doneCount);
                    item.OutputPath = result.OutputPath;
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        lock (_outputLock) _outputPaths.Add(result.OutputPath);
                    }
                    RunOnUi(() =>
                    {
                        item.Status = Strings.StatusDone;
                        UpdateOverallProgress();
                    });
                }
                else
                {
                    var raw = result.FullError ?? result.ErrorMessage;
                    var analysis = ErrorParser.Parse(raw);
                    var message = ErrorMessages.GetMessage(analysis.Kind);
                    var suggestion = ErrorMessages.GetSuggestion(analysis.Kind);

                    Interlocked.Increment(ref _failedCount);
                    lock (_outputLock)
                    {
                        _errorLog.AppendLine("[" + Path.GetFileName(file) + "] " + message + " — " + suggestion);
                        _errorLog.AppendLine(raw ?? string.Empty);
                        _errorLog.AppendLine();
                    }

                    var statusText = Strings.StatusFailed + ": " + message;
                    RunOnUi(() => item.Status = statusText);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>解析该文件应使用的转换条目（同一方向多插件注册时由 FormatResolver 决定），带缓存避免批量中重复询问。</summary>
        private ConversionEntry ResolveEntry(string inputExt)
        {
            var key = FormatRegistry.Normalize(inputExt) + "." + FormatRegistry.Normalize(_targetExt);
            ConversionEntry entry;
            if (!_entryCache.TryGetValue(key, out entry))
            {
                entry = FormatResolver.Resolve(_host, _settingsManager, inputExt, _targetExt);
                _entryCache[key] = entry;
            }
            return entry;
        }

        /// <summary>指定输出目录则输出到该目录（文件名不变、换扩展名），否则返回 null 走插件默认（源目录）。</summary>
        private string ResolveOutputPath(string file)
        {
            if (string.IsNullOrEmpty(_outputDir)) return null;
            var name = Path.GetFileNameWithoutExtension(file);
            return Path.Combine(_outputDir, name + "." + _targetExt);
        }

        private void RunOnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }

        private void UpdateOverallProgress()
        {
            double sum = 0;
            foreach (var p in _progressByIndex) sum += p;
            OverallProgress.Value = Math.Min(100, _files.Length == 0 ? 0 : sum / _files.Length);
        }

        private void OnCopyError(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ErrorLogBox.Text);
            }
            catch
            {
                // 忽略剪贴板占用
            }
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            if (_outputPaths.Count == 0) return;
            var path = _outputPaths[0];
            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch
            {
                // 忽略打开失败
            }
        }

        private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemList.SelectedItem as BatchItem;
            if (item == null || string.IsNullOrEmpty(item.OutputPath)) return;
            if (!File.Exists(item.OutputPath)) return;

            var window = new AudioPlayerWindow(item.OutputPath, _host) { Owner = this };
            window.Show();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class BatchItem : INotifyPropertyChanged
    {
        private string _status;

        public string FileName { get; set; }

        /// <summary>转换成功后的输出文件路径；未完成时为 null。</summary>
        public string OutputPath { get; set; }

        /// <summary>该文件的自定义参数；null 表示用默认。</summary>
        public IDictionary<string, string> Options { get; set; }

        public string Status
        {
            get { return _status; }
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
