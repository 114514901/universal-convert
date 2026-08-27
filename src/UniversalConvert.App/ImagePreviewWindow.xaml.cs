using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>
    /// 简易图片预览窗口：适应窗口显示，支持基本缩放。
    /// WPF 无法直接解码的格式（webp/heic/avif/psd 等）自动用随包 ffmpeg 转成 PNG 临时文件显示，关闭时清理。
    /// </summary>
    public partial class ImagePreviewWindow : Window
    {
        private readonly string _filePath;
        private string _tempPngPath;

        public ImagePreviewWindow(string filePath)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _filePath = filePath;
            Title = Strings.Preview;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = Path.GetFileName(_filePath);
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            StatusText.Text = Strings.PreviewLoading;

            BitmapImage bitmap = TryLoadDirect(_filePath);
            if (bitmap == null)
            {
                bitmap = await TryLoadViaFfmpegAsync();
            }

            if (bitmap == null)
            {
                StatusText.Text = Strings.ImagePreviewFailed;
                return;
            }

            PreviewImage.Source = bitmap;
            StatusText.Text = string.Format(Strings.ImagePreviewSizeFormat,
                bitmap.PixelWidth, bitmap.PixelHeight, FormatBytes(new FileInfo(_filePath).Length));
            ZoomActualButton.IsEnabled = true;
            FitButton.IsEnabled = true;
        }

        /// <summary>WPF 直接解码（jpg/png/bmp/gif/tiff/ico 等）；失败返回 null。</summary>
        private static BitmapImage TryLoadDirect(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>ffmpeg 转 PNG 兜底（webp/heic/avif/psd 等 WPF 不能解码的格式）。</summary>
        private async Task<BitmapImage> TryLoadViaFfmpegAsync()
        {
            var ffmpeg = AudioMetadataReader.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg)) return null;

            var png = Path.Combine(Path.GetTempPath(), "uc-img-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-y -hide_banner -loglevel error -i " + Quote(_filePath) + " -frames:v 1 " + Quote(png),
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

                if (exitCode == 0 && File.Exists(png))
                {
                    _tempPngPath = png;
                    return TryLoadDirect(png);
                }
            }
            catch
            {
                // 转码失败走下方清理
            }

            try { if (File.Exists(png)) File.Delete(png); } catch { }
            return null;
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "\\\"") + "\"";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        private void OnZoomActual(object sender, RoutedEventArgs e)
        {
            PreviewImage.Stretch = System.Windows.Media.Stretch.None;
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
            PreviewImage.StretchDirection = System.Windows.Media.StretchDirection.Both;
        }

        private void OnZoomFit(object sender, RoutedEventArgs e)
        {
            PreviewImage.Stretch = System.Windows.Media.Stretch.Uniform;
            PreviewImage.StretchDirection = System.Windows.Media.StretchDirection.DownOnly;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempPngPath) && File.Exists(_tempPngPath)) File.Delete(_tempPngPath);
            }
            catch { }
        }
    }
}