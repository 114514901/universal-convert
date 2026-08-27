using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>
    /// 简易图片预览窗口：适应窗口/100%/缩放（滚轮、按钮）与拖拽平移。
    /// 显示尺寸用 Image 的 Width/Height 直接控制（ScaleTransform 只影响渲染、不影响布局，
    /// 会导致滚动条/拖拽失效），保证缩放后滚动范围正确。
    /// WPF 无法直接解码的格式（webp/heic/avif/psd 等）自动用随包 ffmpeg 转成 PNG 临时文件显示，关闭时清理。
    /// </summary>
    public partial class ImagePreviewWindow : Window
    {
        private const double ZoomStep = 1.25;
        private const double MinScale = 0.05;
        private const double MaxScale = 64.0;

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

        // ---------- 缩放（用 Width/Height 控制显示尺寸，滚动/拖拽随布局尺寸生效） ----------

        private BitmapImage Source
        {
            get { return PreviewImage.Source as BitmapImage; }
        }

        /// <summary>当前显示比例：fit 模式按"图片显示尺寸/原始尺寸"估算，缩放模式为显式比例。</summary>
        private double CurrentScale()
        {
            var src = Source;
            if (src == null) return 1.0;
            if (_fitMode)
            {
                if (PreviewImage.ActualWidth > 0 && src.PixelWidth > 0)
                {
                    return PreviewImage.ActualWidth / src.PixelWidth;
                }
                return 1.0;
            }
            return PreviewImage.ActualWidth > 0 && src.PixelWidth > 0
                ? PreviewImage.ActualWidth / src.PixelWidth
                : 1.0;
        }

        private void ApplyFit()
        {
            _fitMode = true;
            SetImageToViewport();
            UpdateZoomText();
        }

        private void SetImageToViewport()
        {
            var src = Source;
            if (src == null) return;

            var vw = Math.Max(64, ImageScroll.ViewportWidth - 24);
            var vh = Math.Max(64, ImageScroll.ViewportHeight - 24);
            var scale = Math.Min(vw / (double)src.PixelWidth, vh / (double)src.PixelHeight);
            PreviewImage.Width = src.PixelWidth * scale;
            PreviewImage.Height = src.PixelHeight * scale;
        }

        /// <summary>按给定比例设置显示尺寸，并保持视口中心对应的内容点不动。</summary>
        private void SetScaleKeepCenter(double newScale)
        {
            var src = Source;
            if (src == null) return;

            var oldScale = CurrentScale();
            // 缩放前视口中心对应的内容坐标（相对图片原点）
            var contentX = (ImageScroll.HorizontalOffset + ImageScroll.ViewportWidth / 2) / oldScale;
            var contentY = (ImageScroll.VerticalOffset + ImageScroll.ViewportHeight / 2) / oldScale;

            _fitMode = false;
            PreviewImage.Width = src.PixelWidth * newScale;
            PreviewImage.Height = src.PixelHeight * newScale;

            // 缩放后让同一内容坐标回到视口中心
            ImageScroll.ScrollToHorizontalOffset(contentX * newScale - ImageScroll.ViewportWidth / 2);
            ImageScroll.ScrollToVerticalOffset(contentY * newScale - ImageScroll.ViewportHeight / 2);
            UpdateZoomText();
        }

        private void ZoomBy(double factor)
        {
            var next = Math.Max(MinScale, Math.Min(MaxScale, CurrentScale() * factor));
            SetScaleKeepCenter(next);
        }

        private void OnZoomFit(object sender, RoutedEventArgs e)
        {
            ApplyFit();
        }

        private void OnZoomActual(object sender, RoutedEventArgs e)
        {
            SetScaleKeepCenter(1.0);
        }

        private void OnZoomIn(object sender, RoutedEventArgs e)
        {
            ZoomBy(ZoomStep);
        }

        private void OnZoomOut(object sender, RoutedEventArgs e)
        {
            ZoomBy(1.0 / ZoomStep);
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Source == null) return;
            ZoomBy(e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep);
            e.Handled = true;
        }

        private void OnScrollSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitMode)
            {
                SetImageToViewport();
            }
        }

        // ---------- 拖拽平移（直接移动滚动偏移，滚动条同步） ----------

        private bool _dragging;
        private Point _dragStart;
        private double _scrollStartX;
        private double _scrollStartY;

        private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (PreviewImage.Source == null) return;
            // 内容大于视口时才需要拖拽；否则忽略
            var canScrollHoriz = ImageScroll.ExtentWidth > ImageScroll.ViewportWidth;
            var canScrollVert = ImageScroll.ExtentHeight > ImageScroll.ViewportHeight;
            if (!canScrollHoriz && !canScrollVert) return;

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
            ImageScroll.ScrollToHorizontalOffset(_scrollStartX - (pos.X - _dragStart.X));
            ImageScroll.ScrollToVerticalOffset(_scrollStartY - (pos.Y - _dragStart.Y));
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
            ZoomText.Text = _fitMode ? "适应" : string.Format("{0:0}%", CurrentScale() * 100);
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