using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>
    /// 简易视频预览窗口：MediaElement 直接播放（依赖系统解码器，mp4(H.264)/wmv/avi/mpeg 等）。
    /// 解码失败时提示并回退系统默认播放器。
    /// </summary>
    public partial class VideoPreviewWindow : Window
    {
        private readonly string _filePath;
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _playing;
        private bool _seeking;
        private bool _stopped;
        private readonly DispatcherTimer _clickTimer = new DispatcherTimer();
        private bool _pendingClick;
        private int _pendingSeekSeconds;

        public VideoPreviewWindow(string filePath)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _filePath = filePath;
            Title = Strings.Preview;

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = Path.GetFileName(_filePath);

            // MediaElement 音量默认非满音量，显式对齐滑块 100%
            Video.Volume = 1.0;
            VolumeText.Text = "100%";

            Video.Source = new Uri(_filePath);
            Video.Play();
            _playing = true;
            PlayPauseButton.Content = Strings.Pause;
            _timer.Start();
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            PlayPauseButton.IsEnabled = true;
            if (Video.NaturalDuration.HasTimeSpan)
            {
                ProgressSlider.Maximum = Video.NaturalDuration.TimeSpan.TotalSeconds;
                ProgressSlider.IsEnabled = true;
            }
        }

        private void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            _playing = false;
            PlayPauseButton.Content = Strings.Play;
        }

        private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _timer.Stop();
            StatusFail();
        }

        private void StatusFail()
        {
            // 系统解码器不支持：提示并交给系统默认播放器
            TitleText.Text = TitleText.Text + "（" + Strings.VideoPreviewFallback + "）";
            try
            {
                Process.Start(_filePath);
            }
            catch { }
            Close();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_seeking || !_playing) return;
            if (Video.NaturalDuration.HasTimeSpan)
            {
                ProgressSlider.Value = Video.Position.TotalSeconds;
            }
            UpdateTimeText();
        }

        private void OnPlayPause(object sender, RoutedEventArgs e)
        {
            if (_playing)
            {
                Video.Pause();
                _playing = false;
                PlayPauseButton.Content = Strings.Play;
            }
            else
            {
                // WPF MediaElement 已知问题：Stop() 后直接 Play() 可能无效（画面冻结）
                // ——重新赋值 Source 确保可播
                if (_stopped)
                {
                    _stopped = false;
                    Video.Source = null;
                    Video.Source = new Uri(_filePath);
                }
                Video.Play();
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            Video.Stop();
            _playing = false;
            _stopped = true;
            PlayPauseButton.Content = Strings.Play;
            ProgressSlider.Value = 0;
            TimeText.Text = string.Empty;
        }

        // ---------- 画面快捷操作：左右 1/3 单击 ±5 秒，任意位置双击播放/暂停 ----------
        // MediaElement 无 MouseDoubleClick 事件（非 Control），用点击计时自行判定：
        // 每次单击延迟 300ms 执行（等待可能的双击），300ms 内再次点击 → 播放/暂停

        private void OnVideoMouseLeftDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Video.ActualWidth <= 0 || !Video.NaturalDuration.HasTimeSpan) return;

            // 双击（任意位置）→ 播放/暂停
            if (_pendingClick && _clickTimer.IsEnabled)
            {
                _clickTimer.Stop();
                _pendingClick = false;
                OnPlayPause(sender, e);
                e.Handled = true;
                return;
            }

            // 单击：记录区域，延迟执行（中央为 0 秒 = 无动作）
            var x = e.GetPosition(Video).X;
            var region = x / Video.ActualWidth;
            _pendingClick = false;
            _pendingSeekSeconds = region < 1.0 / 3.0 ? -5 : region > 2.0 / 3.0 ? 5 : 0;
            StartClickTimer();
            e.Handled = true;
        }

        private void StartClickTimer()
        {
            _pendingClick = true;
            _clickTimer.Interval = TimeSpan.FromMilliseconds(300);
            _clickTimer.Tick -= OnClickTimerTick;
            _clickTimer.Tick += OnClickTimerTick;
            _clickTimer.Stop();
            _clickTimer.Start();
        }

        private void OnClickTimerTick(object sender, EventArgs e)
        {
            _clickTimer.Stop();
            if (!_pendingClick) return;
            _pendingClick = false;
            if (_pendingSeekSeconds != 0)
            {
                SeekRelative(_pendingSeekSeconds);
            }
        }

        private void SeekRelative(int seconds)
        {
            if (!Video.NaturalDuration.HasTimeSpan) return;
            var target = Video.Position + TimeSpan.FromSeconds(seconds);
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > Video.NaturalDuration.TimeSpan) target = Video.NaturalDuration.TimeSpan;
            Video.Position = target;
            ProgressSlider.Value = target.TotalSeconds;
            UpdateTimeText();
        }

        private bool _wasPlayingBeforeSeek;

        // 拖拽进度条期间临时暂停（避免反复 seek 产生噪声/杂音），松手恢复原播放状态
        private void OnProgressPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = true;
            _wasPlayingBeforeSeek = _playing;
            if (_playing)
            {
                Video.Pause();
                _playing = false;
                PlayPauseButton.Content = Strings.Play;
            }
        }

        private void OnProgressPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = false;
            _previewRequestId++;                 // 使进行中的抽帧请求作废
            PreviewFrameImage.Visibility = Visibility.Collapsed;
            if (Video.NaturalDuration.HasTimeSpan)
            {
                Video.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
            }
            if (_wasPlayingBeforeSeek)
            {
                Video.Play();
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
            UpdateTimeText();
        }

        private DateTime _lastPreviewRender;
        private int _previewRequestId;

        private void OnProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 拖动中实时 seek + ffmpeg 抽帧预览（浮层显示该位置画面）
            if (_seeking)
            {
                if (Video.NaturalDuration.HasTimeSpan)
                {
                    Video.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
                    // 抽帧较重，150ms 节流
                    var now = DateTime.Now;
                    if ((now - _lastPreviewRender).TotalMilliseconds >= 150)
                    {
                        _lastPreviewRender = now;
                        RenderPreviewFrame(ProgressSlider.Value);
                    }
                }
                UpdateTimeText();
            }
        }

        /// <summary>
        /// ffmpeg 抽帧预览：把拖动位置的画面抽成一帧 PNG 显示在浮层（MediaElement 暂停态
        /// 不渲染 seek 帧，Play/Pause hack 也不可靠——直接抽帧最稳）。
        /// 只有最新一次请求的结果会被显示（过期请求丢弃）。
        /// </summary>
        private async void RenderPreviewFrame(double seconds)
        {
            var id = ++_previewRequestId;
            PreviewFrameImage.Visibility = Visibility.Visible;

            var ffmpeg = AudioMetadataReader.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg)) return;

            var tmp = Path.Combine(Path.GetTempPath(), "uc-vprev-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var args = string.Format(
                    "-y -hide_banner -loglevel error -ss {0} -i {1} -frames:v 1 -vf \"scale='min(640,iw)':-2\" {2}",
                    seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    Quote(_filePath), Quote(tmp));

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var exitCode = await Task.Run(() =>
                {
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) return -1;
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                });

                if (exitCode == 0 && File.Exists(tmp) && id == _previewRequestId)
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(tmp);
                    bmp.EndInit();
                    bmp.Freeze();
                    PreviewFrameImage.Source = bmp;
                }
            }
            catch
            {
                // 抽帧失败：忽略（保持无预览，不影响拖动）
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 指数音量曲线（与音频预览一致）：人耳对数感知，滑块 100% → 满音量
            // XAML 加载期间 Value 初始化可能先于 VolumeText 字段赋值，需判空
            if (Video != null)
            {
                Video.Volume = VolumeToAmplitude(VolumeSlider.Value);
            }
            if (VolumeText != null)
            {
                VolumeText.Text = string.Format("{0:0}%", VolumeSlider.Value * 100);
            }
        }

        /// <summary>滑块值（0-1 线性）→ 实际振幅（指数映射，2 次方）。</summary>
        private static double VolumeToAmplitude(double sliderValue)
        {
            if (sliderValue <= 0) return 0;
            if (sliderValue >= 1) return 1;
            return Math.Pow(sliderValue, 2.0);
        }

        private void UpdateTimeText()
        {
            if (!Video.NaturalDuration.HasTimeSpan) return;
            var pos = _seeking ? TimeSpan.FromSeconds(ProgressSlider.Value) : Video.Position;
            TimeText.Text = string.Format("{0:hh\\:mm\\:ss} / {1:hh\\:mm\\:ss}",
                pos, Video.NaturalDuration.TimeSpan);
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "\\\"") + "\"";
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
            try { Video.Close(); } catch { }
        }
    }
}