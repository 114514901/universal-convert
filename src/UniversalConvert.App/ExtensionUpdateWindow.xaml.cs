using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.App
{
    /// <summary>
    /// 扩展安装/更新进度弹窗：列表显示每个扩展的下载进度与状态，多个扩展并行执行。
    /// 用于插件管理器的批量/单插件更新、扩展中心的安装。
    /// </summary>
    public partial class ExtensionUpdateWindow : Window
    {
        private readonly IList<ExtensionInfo> _extensions;
        private readonly List<ExtensionUpdateItem> _items = new List<ExtensionUpdateItem>();
        private bool _paused;
        private readonly ManualResetEventSlim _pauseSignal = new ManualResetEventSlim(false);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>执行结果汇总（窗口关闭后供调用方展示）。</summary>
        public string Summary { get; private set; }

        public ExtensionUpdateWindow(string title, IEnumerable<ExtensionInfo> extensions)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            Title = title;

            _extensions = (extensions ?? new ExtensionInfo[0]).ToList();
            foreach (var ext in _extensions)
            {
                _items.Add(new ExtensionUpdateItem(ext));
            }
            ExtensionList.ItemsSource = _items;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RunAllAsync();
        }

        private async Task RunAllAsync()
        {
            int succeeded = 0;
            int failed = 0;

            // 同时最多 3 个扩展下载（文件级并发上限）；全局连接预算（9）由共享 SemaphoreSlim 控制，
            // 各文件的 8MB 块竞争预算——文件完成自动把连接让给剩余文件
            var gate = new SemaphoreSlim(3);
            var budget = new SemaphoreSlim(9);
            var tasks = _items.Select(async item =>
            {
                await gate.WaitAsync();
                try
                {
                    // Progress<T> 在 UI 线程构造，回调回到 UI 线程，可直接更新绑定属性
                    var progress = new Progress<double>(p => item.SetDownloadProgress(p));
                    Log.Info($"开始安装/更新扩展: {item.Info.Id} {item.Info.Version} (下载: {item.Info.DownloadUrl})");
                    var result = await ExtensionCenter.InstallAsync(item.Info, progress, _cts.Token, _pauseSignal, budget);
                    Log.Info($"扩展 {item.Info.Id} 安装/更新结果: {result}");

                    if (result == ExtensionInstallResult.Installed)
                    {
                        item.SetDone(Strings.ExtensionDone);
                        Interlocked.Increment(ref succeeded);
                    }
                    else if (result == ExtensionInstallResult.StagedForRestart)
                    {
                        item.SetDone(Strings.ExtensionDoneRestart);
                        Interlocked.Increment(ref succeeded);
                    }
                    else if (_cts.IsCancellationRequested)
                    {
                        item.SetFailed(Strings.ExtensionCancelled);
                        Interlocked.Increment(ref failed);
                    }
                    else
                    {
                        item.SetFailed(Strings.ExtensionUpdateFailed);
                        Interlocked.Increment(ref failed);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"扩展 {item.Info.Id} 安装/更新异常: {ex}");
                    item.SetFailed(string.Format(Strings.ExtensionFailedFormat, ex.Message));
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);

            // 全部取消时不弹重启提示
            var cancelledAll = _cts.IsCancellationRequested;
            Summary = failed > 0
                ? string.Format(Strings.ExtensionSummaryFormat, succeeded, failed)
                : string.Format(Strings.ExtensionAllSucceeded, succeeded);
            SummaryText.Text = Summary;
            CloseButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            _pauseSignal.Reset();

            // 安装/更新都需要重启才生效（新装需加载、已加载的更新需暂存应用），有成功就提示；全部取消则不提示
            if (succeeded > 0 && !cancelledAll)
            {
                AppRestart.PromptAndRestart();
            }
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            _paused = !_paused;
            if (_paused)
            {
                _pauseSignal.Set();
                PauseButton.Content = Strings.Resume;
            }
            else
            {
                _pauseSignal.Reset();
                PauseButton.Content = Strings.Pause;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            CancelButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            // 取消时若处于暂停，复位暂停信号让下载任务能响应取消退出
            if (_paused)
            {
                _paused = false;
                _pauseSignal.Reset();
                PauseButton.Content = Strings.Pause;
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>一行扩展的安装/更新状态（绑定到 ListView）。</summary>
    public sealed class ExtensionUpdateItem : INotifyPropertyChanged
    {
        public ExtensionInfo Info { get; }

        public ExtensionUpdateItem(ExtensionInfo info)
        {
            Info = info;
            _statusText = Strings.ExtensionWaiting;
        }

        public string Name => Info.Name;

        private double _progress;
        public double Progress
        {
            get { return _progress; }
            private set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get { return _isIndeterminate; }
            private set { _isIndeterminate = value; OnPropertyChanged(nameof(IsIndeterminate)); }
        }

        private string _statusText;
        public string StatusText
        {
            get { return _statusText; }
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        /// <summary>下载进度回调（0-100）。</summary>
        public void SetDownloadProgress(double percent)
        {
            Progress = percent;
            IsIndeterminate = false;
            StatusText = percent >= 100
                ? Strings.ExtensionExtracting
                : string.Format(Strings.Downloading, (int)percent);
        }

        public void SetDone(string status)
        {
            Progress = 100;
            IsIndeterminate = false;
            StatusText = status;
        }

        public void SetFailed(string status)
        {
            IsIndeterminate = false;
            StatusText = status;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
