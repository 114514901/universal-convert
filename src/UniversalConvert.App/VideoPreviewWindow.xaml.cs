using System;
using System.Diagnostics;
using System.IO;
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
                Video.Play();
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            Video.Stop();
            _playing = false;
            PlayPauseButton.Content = Strings.Play;
            ProgressSlider.Value = 0;
            TimeText.Text = string.Empty;
        }

        private void OnProgressPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = true;
        }

        private void OnProgressPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = false;
            if (Video.NaturalDuration.HasTimeSpan)
            {
                Video.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
            }
            UpdateTimeText();
        }

        private void OnProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 拖动中实时显示时间（不 seek，避免拖动卡顿）
            if (_seeking)
            {
                UpdateTimeText();
            }
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // XAML 加载期间 Value 初始化可能先于 VolumeText 字段赋值，需判空
            if (Video != null)
            {
                Video.Volume = VolumeSlider.Value;
            }
            if (VolumeText != null)
            {
                VolumeText.Text = string.Format("{0:0}%", VolumeSlider.Value * 100);
            }
        }

        private void UpdateTimeText()
        {
            if (!Video.NaturalDuration.HasTimeSpan) return;
            var pos = _seeking ? TimeSpan.FromSeconds(ProgressSlider.Value) : Video.Position;
            TimeText.Text = string.Format("{0:hh\\:mm\\:ss} / {1:hh\\:mm\\:ss}",
                pos, Video.NaturalDuration.TimeSpan);
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