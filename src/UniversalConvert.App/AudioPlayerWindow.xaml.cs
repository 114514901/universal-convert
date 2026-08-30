using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>简易音频播放器（基于 WPF MediaPlayer），支持音量、进度、时长与实时码率/采样率等元数据显示。</summary>
    public partial class AudioPlayerWindow : Window
    {
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly string _filePath;
        private string _playbackPath;
        private bool _playing;
        private bool _updatingSlider;
        private bool _ended;
        private string _tempWavPath;

        private AudioStreamInfo _streamInfo;
        private BitrateTimeline _timeline;
        private int _averageBitrate;

        /// <param name="displayName">标题显示名（渲染/解密预览时传原文件名，让标题可读）；null 用文件名。</param>
        public AudioPlayerWindow(string filePath, CoreHost host, string displayName = null)
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            _filePath = filePath;
            _playbackPath = filePath;
            TitleText.Text = displayName ?? Path.GetFileName(filePath);

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
            _timer.Start();

            _player.MediaOpened += OnMediaOpened;
            _player.MediaEnded += OnMediaEnded;
            _player.MediaFailed += OnMediaFailed;

            // 音量：恢复上次记录（0-1）；无记录默认满音量
            var savedVolume = VolumeMemory.Load();
            if (savedVolume.HasValue)
            {
                VolumeSlider.Value = savedVolume.Value; // 触发 OnVolumeChanged 同步 _player.Volume + 文案
            }
            else
            {
                // MediaPlayer.Volume 默认是 0.5（50%）而音量条初始在 100%：
                // 显式对齐为满音量，避免「显示满格但实际只有一半、拉一下才变大」
                _player.Volume = 1.0;
                VolumeText.Text = "100%";
            }

            // 提供者优先：若某插件声明支持该格式的预览（如 MIDI 合成），先渲染再播放
            var provider = FindPreviewProvider(host, filePath);
            if (provider != null)
            {
                RenderAndPlayAsync(provider);
            }
            else
            {
                _player.Open(new Uri(filePath));
                _player.Play();
            }

            LoadMetadataAsync();
        }

        /// <summary>按插件顺序查找第一个支持该扩展名的预览提供者。</summary>
        private static IPreviewProvider FindPreviewProvider(CoreHost host, string filePath)
        {
            if (host == null || host.Plugins == null) return null;

            string ext;
            try { ext = Path.GetExtension(filePath); }
            catch { return null; }

            return host.Plugins.OfType<IPreviewProvider>()
                .FirstOrDefault(p => p.SupportedPreviewExtensions != null
                    && p.SupportedPreviewExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
        }

        private async void RenderAndPlayAsync(IPreviewProvider provider)
        {
            TimeText.Text = Strings.PreviewRendering;

            string rendered = null;
            try
            {
                rendered = await Task.Run(() => provider.RenderPreviewAsync(_filePath, CancellationToken.None));
            }
            catch
            {
                rendered = null; // 渲染失败回退直接打开
            }

            if (!string.IsNullOrEmpty(rendered) && File.Exists(rendered))
            {
                _playbackPath = rendered;
                _tempWavPath = rendered; // 复用关闭时清理逻辑
                _player.Open(new Uri(rendered));
                _player.Play();
                LoadMetadataAsync(); // 重新读取渲染产物的元数据
                return;
            }

            // 渲染失败：清掉提示，回退到播放器直接打开（仍可走 ffmpeg 兜底）
            TimeText.Text = string.Empty;
            try { if (!string.IsNullOrEmpty(rendered) && File.Exists(rendered)) File.Delete(rendered); } catch { }
            _player.Open(new Uri(_filePath));
            _player.Play();
        }

        private async void LoadMetadataAsync()
        {
            var ffprobe = AudioMetadataReader.FindFfprobe();
            if (string.IsNullOrEmpty(ffprobe)) return;

            try
            {
                var path = _playbackPath;
                var streamTask = Task.Run(() => AudioMetadataReader.ReadStreamInfo(ffprobe, path));
                var timelineTask = Task.Run(() => AudioMetadataReader.ReadBitrateTimeline(ffprobe, path));
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
            _ended = false;
            _playing = true;
            PlayPauseButton.IsEnabled = true;
            PlayPauseButton.Content = Strings.Pause;
            ProgressSlider.IsEnabled = true;

            if (_player.NaturalDuration.HasTimeSpan)
            {
                var duration = _player.NaturalDuration.TimeSpan;
                try
                {
                    var fileSize = new FileInfo(_playbackPath).Length;
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
            _ended = true;
            _playing = false;
            PlayPauseButton.Content = Strings.Play;
            UpdateTimeDisplay();
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {
            // Windows Media Foundation 可能不支持该格式（如 Opus）：尝试用随包的 ffmpeg 转成 wav 再播放
            TryPlayViaFfmpegAsync();
        }

        private async void TryPlayViaFfmpegAsync()
        {
            var ffmpeg = AudioMetadataReader.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                ShowCannotPlay();
                return;
            }

            var wavPath = Path.Combine(Path.GetTempPath(), "UniversalConvert-play-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-y -hide_banner -loglevel error -i \"" + _playbackPath + "\" -vn -acodec pcm_s16le \"" + wavPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var exitCode = await Task.Run(() =>
                {
                    using (var process = Process.Start(psi))
                    {
                        if (process == null) return -1;
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                });

                if (exitCode == 0 && File.Exists(wavPath))
                {
                    _tempWavPath = wavPath;
                    _player.Open(new Uri(wavPath));
                    _player.Play();
                    return;
                }
            }
            catch
            {
                // 转码失败走下方清理
            }

            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
            ShowCannotPlay();
        }

        private void ShowCannotPlay()
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

        private bool _wasPlayingBeforeSeek;

        // 拖拽进度条期间临时暂停（避免反复 seek 产生噪声/杂音），松手恢复原播放状态
        private void OnProgressPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _wasPlayingBeforeSeek = _playing;
            if (_playing)
            {
                _player.Pause();
                _playing = false;
                PlayPauseButton.Content = Strings.Play;
            }
        }

        private void OnProgressPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_wasPlayingBeforeSeek)
            {
                _player.Play();
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
        }

        private void OnProgressValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSlider) return;
            if (!_player.NaturalDuration.HasTimeSpan) return;

            var duration = _player.NaturalDuration.TimeSpan;
            var target = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * ProgressSlider.Value / 100.0);
            _player.Position = target;
            UpdateTimeText(target, duration);

            // 媒体已播完后拖动进度条会恢复播放（WPF 行为），需同步状态与按钮，
            // 否则定时器不再更新进度条、按钮仍显示「播放」
            if (_ended)
            {
                _ended = false;
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 指数音量曲线（人耳对数感知，Windows 风格）：滑块 100% → 满音量，低区敏感、高区平滑
            _player.Volume = VolumeToAmplitude(VolumeSlider.Value);
            // XAML 加载期间 Value 初始化可能先于 VolumeText 字段赋值，需判空（视频预览窗口同款崩溃）
            if (VolumeText != null)
            {
                VolumeText.Text = string.Format("{0:0}%", VolumeSlider.Value * 100);
            }
            // 记忆音量（构造期恢复也会回写相同值，无害）
            VolumeMemory.Save(VolumeSlider.Value);
        }

        /// <summary>滑块值（0-1 线性）→ 实际振幅（指数映射，2 次方）。</summary>
        private static double VolumeToAmplitude(double sliderValue)
        {
            if (sliderValue <= 0) return 0;
            if (sliderValue >= 1) return 1;
            return Math.Pow(sliderValue, 2.0);
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
                _ended = false;
                _player.Play();
                _playing = true;
                PlayPauseButton.Content = Strings.Pause;
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            _ended = false;
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
            try { if (!string.IsNullOrEmpty(_tempWavPath) && File.Exists(_tempWavPath)) File.Delete(_tempWavPath); } catch { }
        }
    }
}
