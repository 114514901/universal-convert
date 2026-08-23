using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>简易音频播放器（基于 WPF MediaPlayer），支持音量、进度、时长与实时码率/采样率等元数据显示。</summary>
    public partial class AudioPlayerWindow : Window
    {
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly string _filePath;
        private bool _playing;
        private bool _updatingSlider;

        private AudioStreamInfo _streamInfo;
        private BitrateTimeline _timeline;
        private int _averageBitrate;

        public AudioPlayerWindow(string filePath)
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            _filePath = filePath;
            TitleText.Text = Path.GetFileName(filePath);

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
            _timer.Start();

            _player.MediaOpened += OnMediaOpened;
            _player.MediaEnded += OnMediaEnded;
            _player.MediaFailed += OnMediaFailed;

            _player.Open(new Uri(filePath));
            _player.Play();

            LoadMetadataAsync();
        }

        private async void LoadMetadataAsync()
        {
            var ffprobe = AudioMetadataReader.FindFfprobe();
            if (string.IsNullOrEmpty(ffprobe)) return;

            try
            {
                var streamTask = Task.Run(() => AudioMetadataReader.ReadStreamInfo(ffprobe, _filePath));
                var timelineTask = Task.Run(() => AudioMetadataReader.ReadBitrateTimeline(ffprobe, _filePath));
                await Task.WhenAll(streamTask, timelineTask);

                _streamInfo = streamTask.Result;
                _timeline = timelineTask.Result;
                UpdateTimeDisplay();
            }
            catch
            {
                // 元数据读取失败则静默跳过
            }
        }

        private void OnMediaOpened(object sender, EventArgs e)
        {
            _playing = true;
            PlayPauseButton.IsEnabled = true;
            PlayPauseButton.Content = Strings.Pause;
            ProgressSlider.IsEnabled = true;

            if (_player.NaturalDuration.HasTimeSpan)
            {
                var duration = _player.NaturalDuration.TimeSpan;
                try
                {
                    var fileSize = new FileInfo(_filePath).Length;
                    if (duration.TotalSeconds > 0)
                    {
                        _averageBitrate = (int)(fileSize * 8.0 / duration.TotalSeconds / 1000.0);
                    }
                }
                catch
                {
                    // 忽略
                }
            }

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

            var time = string.Format("{0} / {1}   -{2}",
                FormatTime(position), FormatTime(duration), FormatTime(remaining));

            var metadata = ComposeMetadata(position.TotalSeconds);
            TimeText.Text = string.IsNullOrEmpty(metadata) ? time : time + "    " + metadata;
        }

        private string ComposeMetadata(double seconds)
        {
            var parts = new List<string>();

            if (_streamInfo != null && _streamInfo.SampleRate > 0)
                parts.Add(string.Format("{0:0.#} kHz", _streamInfo.SampleRate / 1000.0));

            int bitrate = _timeline != null && _timeline.IsValid
                ? _timeline.GetBitrateKbps(seconds)
                : _averageBitrate;
            if (bitrate > 0)
                parts.Add(bitrate + " kbps");

            if (_streamInfo != null && _streamInfo.BitDepth > 0)
                parts.Add(_streamInfo.BitDepth + " bit");

            if (_streamInfo != null && _streamInfo.Channels > 0)
                parts.Add(GetChannelText(_streamInfo.Channels));

            return string.Join(" / ", parts);
        }

        private static string GetChannelText(int channels)
        {
            if (channels == 1) return Strings.Mono;
            if (channels == 2) return Strings.Stereo;
            return string.Format(Strings.ChannelsFormat, channels);
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
            _timer.Stop();
            _player.Stop();
        }
    }
}
