using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>简易音频播放器（基于 WPF MediaPlayer）。双击文件时打开并自动播放。</summary>
    public partial class AudioPlayerWindow : Window
    {
        private readonly MediaPlayer _player = new MediaPlayer();
        private bool _playing;

        public AudioPlayerWindow(string filePath)
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            TitleText.Text = Path.GetFileName(filePath);

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

            var duration = _player.NaturalDuration;
            StatusText.Text = duration.HasTimeSpan
                ? string.Format(Strings.Playing, duration.TimeSpan.ToString(@"m\:ss"))
                : Strings.PlayingSimple;
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            _playing = false;
            PlayPauseButton.Content = Strings.Play;
            StatusText.Text = Strings.PlaybackDone;
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {
            _playing = false;
            PlayPauseButton.IsEnabled = false;
            StatusText.Text = Strings.CannotPlay;
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
            StatusText.Text = string.Empty;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            // 无论通过按钮还是窗口 X 关闭，都停止播放，避免后台继续出声
            _player.Stop();
        }
    }
}
