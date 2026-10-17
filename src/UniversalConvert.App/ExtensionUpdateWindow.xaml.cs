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

        /// <summary>窗口已关闭（0 否 / 1 是）。关闭后不再更新界面、不再弹窗、不再提示重启。</summary>
        private int _closed;

        /// <summary>取消令牌与暂停信号已释放（0 否 / 1 是），保证只释放一次。</summary>
        private int _disposed;

        /// <summary>本批次的执行任务：关窗时据此判断下载是否仍在进行（仍在进行则等其结束再释放资源）。</summary>
        private Task _runTask;

        /// <summary>执行结果汇总（窗口关闭后供调用方展示）。</summary>
        public string Summary { get; private set; }

        /// <summary>窗口是否已关闭（关闭后一切界面更新与提示均须停止）。</summary>
        private bool IsClosed
        {
            get { return Volatile.Read(ref _closed) != 0; }
        }

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

            // 标题栏关闭（X）同样要取消下载：否则窗口关闭后下载继续，并回调已关闭的窗口
            Closing += OnWindowClosing;
            Closed += OnWindowClosed;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 关窗前已结束（窗口已关闭）：不再启动下载
            if (IsClosed) return;

            var run = RunAllAsync();
            _runTask = run;
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常流程（各下载项已按取消处理）
            }
            catch (Exception ex)
            {
                // async void 不能把异常交给调用方（会直接终止进程），此处兜底记录
                Log.Error("扩展安装/更新流程异常: " + ex);
            }
        }

        private async Task RunAllAsync()
        {
            int succeeded = 0;
            int failed = 0;
            var failedDetails = new List<string>();

            // 先取出令牌与暂停信号：各下载任务不再直接读字段，关窗释放后也不会访问已释放的对象
            var ct = _cts.Token;
            var pauseSignal = _pauseSignal;

            // 同时最多 3 个扩展下载（文件级并发上限）；全局连接预算（9）由共享 SemaphoreSlim 控制，
            // 各文件的 8MB 块竞争预算——文件完成自动把连接让给剩余文件
            var gate = new SemaphoreSlim(3);
            var budget = new SemaphoreSlim(9);
            try
            {
                var tasks = _items.Select(async item =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        // 已取消（含关窗取消）：未开始的项不再发起下载，直接按取消处理
                        if (ct.IsCancellationRequested)
                        {
                            if (!IsClosed) item.SetFailed(Strings.ExtensionCancelled);
                            Interlocked.Increment(ref failed);
                            return;
                        }

                        // Progress<T> 在 UI 线程构造，回调回到 UI 线程，可直接更新绑定属性；
                        // 窗口关闭后不再更新界面
                        var progress = new Progress<double>(p =>
                        {
                            if (!IsClosed) item.SetDownloadProgress(p);
                        });
                        // 字节级进度（已下载/总大小 MB 显示）
                        var byteProgress = new Progress<Tuple<long, long>>(t =>
                        {
                            if (!IsClosed) item.SetByteProgress(t.Item1, t.Item2);
                        });
                        var errorMsg = "";
                        Log.Info($"开始安装/更新扩展: {item.Info.Id} {item.Info.Version} (下载: {item.Info.DownloadUrl})");
                        var result = await ExtensionCenter.InstallAsync(item.Info, progress, ct, pauseSignal, budget, byteProgress, msg => errorMsg = msg);
                        Log.Info($"扩展 {item.Info.Id} 安装/更新结果: {result}");

                        if (result == ExtensionInstallResult.Installed)
                        {
                            if (!IsClosed) item.SetDone(Strings.ExtensionDone);
                            Interlocked.Increment(ref succeeded);
                        }
                        else if (result == ExtensionInstallResult.StagedForRestart)
                        {
                            if (!IsClosed) item.SetDone(Strings.ExtensionDoneRestart);
                            Interlocked.Increment(ref succeeded);
                        }
                        else if (ct.IsCancellationRequested)
                        {
                            if (!IsClosed) item.SetFailed(Strings.ExtensionCancelled);
                            Interlocked.Increment(ref failed);
                        }
                        else
                        {
                            if (!IsClosed) item.SetFailed(Strings.ExtensionUpdateFailed);
                            Interlocked.Increment(ref failed);
                            lock (failedDetails)
                            {
                                failedDetails.Add(item.Info.Name +
                                    (string.IsNullOrEmpty(errorMsg) ? "" : "：" + errorMsg));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"扩展 {item.Info.Id} 安装/更新异常: {ex}");
                        if (!IsClosed) item.SetFailed(string.Format(Strings.ExtensionFailedFormat, ex.Message));
                        Interlocked.Increment(ref failed);
                    }
                    finally
                    {
                        // Task.WhenAll 返回前不会释放并发门；此处仅防御异常路径（避免 finally 再抛异常）
                        try { gate.Release(); } catch (ObjectDisposedException) { }
                    }
                });

                await Task.WhenAll(tasks);

                // 用户点过取消（含关窗取消）：用于区分「全部取消」与「部分成功后取消」
                var cancelled = ct.IsCancellationRequested;

                // 汇总始终生成（窗口关闭后供调用方展示），但只有窗口仍打开时才写入界面
                Summary = failed > 0
                    ? string.Format(Strings.ExtensionSummaryFormat, succeeded, failed)
                    : string.Format(Strings.ExtensionAllSucceeded, succeeded);

                // 窗口已关闭：不再更新界面、不再弹窗、不再提示重启
                if (IsClosed) return;

                SummaryText.Text = Summary;
                CloseButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                CancelButton.IsEnabled = false;
                SetPauseSignal(false);

                // 有失败项时弹窗提示原因（用户取消导致的中断不算弹窗条件）
                if (failed > 0 && !cancelled)
                {
                    MessageBox.Show(
                        Summary + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failedDetails),
                        Strings.ExtensionUpdateFailed, MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 安装/更新都需要重启才生效（新装需加载、已加载的更新需暂存应用）：
                // 有成功项就提示——全部取消（无任何成功项）不提示，部分成功后取消仍提示
                if (succeeded > 0 && !IsClosed)
                {
                    AppRestart.PromptAndRestart();
                }
            }
            finally
            {
                // Task.WhenAll 返回后所有下载任务均已结束，不会再有代码访问并发门与连接预算
                gate.Dispose();
                budget.Dispose();
                // 关窗时下载可能仍在进行：延迟到此刻释放，避免仍在运行的代码访问已释放的令牌与暂停信号
                DisposeResources();
            }
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            // 窗口关闭后不再改变暂停状态
            if (IsClosed) return;
            _paused = !_paused;
            if (_paused)
            {
                SetPauseSignal(true);
                PauseButton.Content = Strings.Resume;
            }
            else
            {
                SetPauseSignal(false);
                PauseButton.Content = Strings.Pause;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (IsClosed) return;
            CancelDownloads();
            CancelButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            // 取消时若处于暂停，复位暂停信号让下载任务能响应取消退出
            if (_paused)
            {
                _paused = false;
                SetPauseSignal(false);
                PauseButton.Content = Strings.Pause;
            }
        }

        /// <summary>置位或复位共享暂停信号；信号已释放（窗口已关闭）时静默忽略。</summary>
        private void SetPauseSignal(bool paused)
        {
            try
            {
                if (paused) _pauseSignal.Set();
                else _pauseSignal.Reset();
            }
            catch (ObjectDisposedException)
            {
                // 窗口已关闭并释放了暂停信号：忽略
            }
        }

        /// <summary>取消所有下载；取消令牌已释放（窗口已关闭）时静默忽略。</summary>
        private void CancelDownloads()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 窗口已关闭并释放了取消令牌：忽略
            }
            catch (AggregateException)
            {
                // 某个取消回调抛出异常：取消已生效，忽略
            }
        }

        /// <summary>标记窗口已关闭：取消下载并禁用按钮，此后不再更新界面、不再弹窗、不再提示重启。</summary>
        private void MarkClosed()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            // 暂停中直接关窗会让下载任务一直等待恢复，先复位暂停信号再取消
            SetPauseSignal(false);
            CancelDownloads();
            CancelButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
        }

        /// <summary>标题栏关闭（X）时取消下载：避免窗口关闭后下载继续并回调已关闭的窗口。</summary>
        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            MarkClosed();
        }

        /// <summary>关窗后释放异步资源；下载仍在进行时等本批次结束（RunAllAsync 的 finally）再释放，避免其访问已释放对象。</summary>
        private void OnWindowClosed(object sender, EventArgs e)
        {
            MarkClosed();
            var run = _runTask;
            if (run == null || run.IsCompleted)
            {
                DisposeResources();
            }
        }

        /// <summary>释放取消令牌与暂停信号（保证只释放一次）；调用前须确认没有下载任务仍在使用它们。</summary>
        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Dispose(); } catch (ObjectDisposedException) { }
            try { _pauseSignal.Dispose(); } catch (ObjectDisposedException) { }
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

        /// <summary>下载进度回调（0-100）——仅更新进度条（文本由字节级进度显示）。</summary>
        public void SetDownloadProgress(double percent)
        {
            Progress = percent;
            IsIndeterminate = false;
            if (percent >= 100)
            {
                StatusText = Strings.ExtensionExtracting;
            }
        }

        /// <summary>字节级进度：显示 "已下载/总大小 MB"（保留一位小数）。</summary>
        public void SetByteProgress(long doneBytes, long totalBytes)
        {
            if (totalBytes <= 0) return;
            Progress = Math.Min(100.0, (double)doneBytes / totalBytes * 100.0);
            IsIndeterminate = false;
            StatusText = string.Format("{0:0.0}/{1:0.0} MB",
                doneBytes / 1048576.0, totalBytes / 1048576.0);
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
