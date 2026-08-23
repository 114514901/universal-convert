using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>简易音频播放器（基于 WPF MediaPlayer），支持音量、进度与时长/剩余时间显示。</summary>
    public partial class AudioPlayerWindow : Window
    {
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _playing;
        private bool _updatingSlider;

        public AudioPlayerWindow(string filePath)
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            TitleText.Text = Path.GetFileName(filePath);

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
            _timer.Start();

            _player.MediaOpened += OnMediaOpened;
            _player.MediaEnded += OnMediaEnded;
            _player.MediaFailed += OnMediaFailed;

            _player.Open(new Uri(filePath));
            _player.Play();
        }

        private void OnMediaOpened(object sender, EventArgs e)
        {
            _playing = true;
            PlayPauseButton.IsEnabled = true;
            PlayPauseButton.Content = Strings.Pause;
            ProgressSlider.IsEnabled = true;
            UpdateTimeDisplay();
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            _playing = false;
            PlayPauseButton.Content = Strings.Play;
            UpdateTimeDisplay();
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {
            _playing = false;
            PlayPauseButton.IsEnabled = false;
            TimeText.Text = Strings.CannotPlay;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_playing) return;
            if (!_player.NaturalDuration.HasTimeSpan) return;

            var duration = _player.NaturalDuration.TimeSpan;
            var position = _player.Position;
            if (duration.TotalSeconds <= 0) return;

            _updatingSlider = true;
            ProgressSlider.Value = position.TotalSeconds / duration.TotalSeconds * 100.0;
            _updatingSlider = false;

            UpdateTimeText(position, duration);
        }

        private void OnProgressValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSlider) return;
            if (!_player.NaturalDuration.HasTimeSpan) return;

            var duration = _player.NaturalDuration.TimeSpan;
            var target = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * ProgressSlider.Value / 100.0);
            _player.Position = target;
            UpdateTimeText(target, duration);
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _player.Volume = VolumeSlider.Value;
        }

        private void UpdateTimeDisplay()
        {
            if (!_player.NaturalDuration.HasTimeSpan) return;
            UpdateTimeText(_player.Position, _player.NaturalDuration.TimeSpan);
        }

        private void UpdateTimeText(TimeSpan position, TimeSpan duration)
        {
            var remaining = duration - position;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            TimeText.Text = string.Format("{0} / {1}   -{2}",
                FormatTime(position), FormatTime(duration), FormatTime(remaining));
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1) return t.ToString(@"h\:mm\:ss");
            return t.ToString(@"mm\:ss");
        }

        private void OnPlayPause(object sender, RoutedEventArgs e)
        {
            if (_playing)
            {
                _player.Pause();
                _playing = false;
                PlayPauseButton.Content = Strings.Play;
            }
            else
            {
                _player.Play();
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _playing = false;
            PlayPauseButton.Content = Strings.Play;

            _updatingSlider = true;
            ProgressSlider.Value = 0;
            _updatingSlider = false;
            TimeText.Text = string.Empty;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            // 无论通过按钮还是窗口 X 关闭，都停止播放，避免后台继续出声
            _timer.Stop();
            _player.Stop();
        }
    }
}
