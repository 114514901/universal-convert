using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;

namespace UniversalConvert.App
{
    public partial class ConvertWindow : Window
    {
        private readonly ConversionEngine _engine;
        private readonly ConversionRequest _request;
        private CancellationTokenSource _cts;
        private ConversionResult _result;

        public ConvertWindow(ConversionEngine engine, ConversionRequest request)
        {
            InitializeComponent();
            _engine = engine;
            _request = request;

            var fileName = Path.GetFileName(request.InputPath);
            TitleText.Text = $"正在转换：{fileName}  →  .{request.OutputExtension.TrimStart('.')}";
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            var progress = new Progress<ConversionProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.Message))
                {
                    StatusText.Text = p.Message;
                }
                if (p.Percentage >= 0)
                {
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Value = p.Percentage;
                }
            });

            _result = await _engine.ConvertAsync(_request, progress, _cts.Token);
            ShowResult();
        }

        private void ShowResult()
        {
            CancelButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;

            if (_result.Success)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
                StatusText.Text = $"转换完成（用时 {_result.Duration.TotalSeconds:F1} 秒）";
                OpenFolderButton.Visibility = Visibility.Visible;
                CloseButton.Content = "完成";
            }
            else
            {
                StatusText.Text = "转换失败：" + _result.ErrorMessage;
                OpenFolderButton.Visibility = Visibility.Collapsed;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            var path = _result?.OutputPath;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch
            {
                // 忽略打开失败
            }
        }
    }
}
