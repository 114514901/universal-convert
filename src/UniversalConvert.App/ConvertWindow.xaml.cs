using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Diagnostics;
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
        private string _fullErrorText;
        private bool _paused;
        private readonly ManualResetEventSlim _pauseSignal = new ManualResetEventSlim(false);

        public ConvertWindow(ConversionEngine engine, ConversionRequest request)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _engine = engine;
            _request = request;

            var fileName = Path.GetFileName(request.InputPath);
            TitleText.Text = string.Format(Strings.ConvertingFormat, fileName, request.OutputExtension.TrimStart('.'));
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RunAsync();
        }

        private async void OnRetry(object sender, RoutedEventArgs e)
        {
            await RunAsync();
        }

        private async System.Threading.Tasks.Task RunAsync()
        {
            ResetUiForRun();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _request.PauseSignal = _pauseSignal;

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

            _result = await _engine.ConvertAsync(_request, progress, token);
            ShowResult();
        }

        private void ResetUiForRun()
        {
            StatusText.Text = Strings.Preparing;
            SuggestionText.Visibility = Visibility.Collapsed;
            DetailsExpander.Visibility = Visibility.Collapsed;
            DetailsBox.Text = string.Empty;
            RetryButton.Visibility = Visibility.Collapsed;
            CopyErrorButton.Visibility = Visibility.Collapsed;
            OpenFolderButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            PauseButton.Visibility = Visibility.Visible;
            PauseButton.IsEnabled = true;
            CloseButton.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = true;
            ProgressBar.Value = 0;

            // 重试时复位暂停状态
            _paused = false;
            _pauseSignal.Reset();
            PauseButton.Content = Strings.Pause;
        }

        private void ShowResult()
        {
            CancelButton.Visibility = Visibility.Collapsed;
            PauseButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;

            if (_result.Success)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
                StatusText.Text = string.Format(Strings.ConvertSucceeded, _result.Duration.TotalSeconds);
                OpenFolderButton.Visibility = Visibility.Visible;
                CloseButton.Content = Strings.Done;
            }
            else
            {
                var raw = _result.FullError ?? _result.ErrorMessage;
                var analysis = ErrorParser.Parse(raw, _result.ExitCode, _request.OutputExtension);

                _fullErrorText = BuildFullErrorText(analysis, raw);

                StatusText.Text = ErrorMessages.GetMessage(analysis.Kind);
                SuggestionText.Text = ErrorMessages.GetSuggestion(analysis.Kind, analysis.Detail);
                SuggestionText.Visibility = Visibility.Visible;

                DetailsBox.Text = raw ?? string.Empty;
                DetailsExpander.Visibility = Visibility.Visible;

                RetryButton.Visibility = Visibility.Visible;
                CopyErrorButton.Visibility = Visibility.Visible;
                CloseButton.Content = Strings.Close;
            }
        }

        private string BuildFullErrorText(ErrorAnalysis analysis, string raw)
        {
            var friendly = ErrorMessages.GetMessage(analysis.Kind);
            var suggestion = ErrorMessages.GetSuggestion(analysis.Kind, analysis.Detail);
            return friendly + "\n" + suggestion + "\n\n" + (raw ?? string.Empty);
        }

        private void OnCopyError(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_fullErrorText ?? string.Empty);
            }
            catch
            {
                // 剪贴板可能被占用
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
            // 取消时若处于暂停，恢复信号以便进程能被终止
            if (_paused)
            {
                _paused = false;
                _pauseSignal.Reset();
                PauseButton.Content = Strings.Pause;
            }
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            _paused = !_paused;
            if (_paused)
            {
                _pauseSignal.Set();
                PauseButton.Content = Strings.Resume;
            }
            else
            {
                _pauseSignal.Reset();
                PauseButton.Content = Strings.Pause;
            }
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
