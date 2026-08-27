using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>
    /// 简易图片预览窗口：适应窗口/100%/缩放手动或滚轮控制。
    /// WPF 无法直接解码的格式（webp/heic/avif/psd 等）自动用随包 ffmpeg 转成 PNG 临时文件显示，关闭时清理。
    /// </summary>
    public partial class ImagePreviewWindow : Window
    {
        private const double ZoomStep = 1.25;

        private readonly string _filePath;
        private string _tempPngPath;
        private bool _fitMode = true;

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
            ZoomOutButton.IsEnabled = true;
            ZoomInButton.IsEnabled = true;
            ZoomActualButton.IsEnabled = true;
            FitButton.IsEnabled = true;

            ApplyFit();
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

        // ---------- 缩放 ----------

        /// <summary>适应窗口：图片尺寸锁定为视口大小（ScrollViewer 中 Stretch 无效，必须显式尺寸）。</summary>
        private void ApplyFit()
        {
            _fitMode = true;
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            SetImageToViewport();
            UpdateZoomText();
        }

        private void SetImageToViewport()
        {
            var viewport = new Size(
                Math.Max(64, ImageScroll.ViewportWidth - 24),
                Math.Max(64, ImageScroll.ViewportHeight - 24));
            PreviewImage.Width = viewport.Width;
            PreviewImage.Height = viewport.Height;
        }

        private void SetImageNatural()
        {
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
        }

        private void OnZoomFit(object sender, RoutedEventArgs e)
        {
            ApplyFit();
        }

        private void OnZoomActual(object sender, RoutedEventArgs e)
        {
            _fitMode = false;
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            SetImageNatural();
            UpdateZoomText();
        }

        private void OnZoomIn(object sender, RoutedEventArgs e)
        {
            _fitMode = false;
            SetImageNatural();
            ImageScale.ScaleX *= ZoomStep;
            ImageScale.ScaleY *= ZoomStep;
            UpdateZoomText();
        }

        private void OnZoomOut(object sender, RoutedEventArgs e)
        {
            var next = ImageScale.ScaleX / ZoomStep;
            if (next < 0.05)
            {
                next = 0.05;
            }
            _fitMode = false;
            SetImageNatural();
            var ratio = next / ImageScale.ScaleX;
            ImageScale.ScaleX = next;
            ImageScale.ScaleY *= ratio;
            UpdateZoomText();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var src = PreviewImage.Source as BitmapImage;
            if (src == null) return;

            _fitMode = false;
            SetImageNatural();

            var factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
            var next = Math.Max(0.05, Math.Min(64.0, ImageScale.ScaleX * factor));
            var ratio = next / ImageScale.ScaleX;
            ImageScale.ScaleX = next;
            ImageScale.ScaleY *= ratio;
            UpdateZoomText();

            e.Handled = true;
        }

        private void OnScrollSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitMode && PreviewImage.Source != null)
            {
                SetImageToViewport();
            }
        }

        // ---------- 拖拽平移（放缩后的图片直接拖动，滚动条同步） ----------

        private bool _dragging;
        private Point _dragStart;
        private double _scrollStartX;
        private double _scrollStartY;

        private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (PreviewImage.Source == null || _fitMode) return;

            _dragging = true;
            _dragStart = e.GetPosition(ImageScroll);
            _scrollStartX = ImageScroll.HorizontalOffset;
            _scrollStartY = ImageScroll.VerticalOffset;
            PreviewImage.Cursor = Cursors.Hand;
            PreviewImage.CaptureMouse();
            e.Handled = true;
        }

        private void OnImageMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;

            var pos = e.GetPosition(ImageScroll);
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            ImageScroll.ScrollToHorizontalOffset(_scrollStartX - dx);
            ImageScroll.ScrollToVerticalOffset(_scrollStartY - dy);
            e.Handled = true;
        }

        private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || !_dragging) return;
            _dragging = false;
            PreviewImage.Cursor = Cursors.Arrow;
            PreviewImage.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnImageMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            PreviewImage.Cursor = Cursors.Arrow;
            PreviewImage.ReleaseMouseCapture();
        }

        private void UpdateZoomText()
        {
            if (PreviewImage.Source == null) return;
            ZoomText.Text = _fitMode ? "适应" : string.Format("{0:0}%", ImageScale.ScaleX * 100);
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