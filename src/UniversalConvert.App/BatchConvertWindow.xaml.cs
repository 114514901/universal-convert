using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;

namespace UniversalConvert.App
{
    public partial class BatchConvertWindow : Window
    {
        private readonly CoreHost _host;
        private readonly string[] _files;
        private readonly string _targetExt;
        private readonly ObservableCollection<BatchItem> _items = new ObservableCollection<BatchItem>();
        private readonly List<string> _outputPaths = new List<string>();

        public BatchConvertWindow(CoreHost host, string[] files, string targetExt)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _host = host;
            _files = files;
            _targetExt = targetExt;

            TitleText.Text = Strings.BatchTitle + "  →  ." + targetExt;

            foreach (var f in files)
            {
                _items.Add(new BatchItem { FileName = Path.GetFileName(f), Status = Strings.StatusPending });
            }
            ItemList.ItemsSource = _items;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RunAsync();
        }

        private async System.Threading.Tasks.Task RunAsync()
        {
            int done = 0, failed = 0;

            for (int i = 0; i < _files.Length; i++)
            {
                var file = _files[i];
                var item = _items[i];
                item.Status = Strings.StatusConverting;

                var entry = _host.Registry.GetEntry(Path.GetExtension(file), _targetExt);
                if (entry == null)
                {
                    item.Status = Strings.StatusSkipped;
                    continue;
                }

                var options = PresetMerger.Merge(entry.Options, entry.Presets, null, null);
                var request = new ConversionRequest
                {
                    PluginId = entry.PluginId,
                    InputPath = file,
                    InputExtension = Path.GetExtension(file),
                    OutputExtension = "." + _targetExt,
                    Options = options
                };

                int index = i;
                var progress = new Progress<ConversionProgress>(p =>
                {
                    var frac = (index + (p.Percentage >= 0 ? p.Percentage / 100.0 : 0)) / _files.Length;
                    OverallProgress.Value = Math.Min(100, frac * 100);
                    if (!string.IsNullOrEmpty(p.Message))
                    {
                        SummaryText.Text = Path.GetFileName(file) + ": " + p.Message;
                    }
                });

                var result = await _host.Engine.ConvertAsync(request, progress, CancellationToken.None);

                if (result.Success)
                {
                    item.Status = Strings.StatusDone;
                    done++;
                    if (!string.IsNullOrEmpty(result.OutputPath)) _outputPaths.Add(result.OutputPath);
                }
                else
                {
                    var raw = result.FullError ?? result.ErrorMessage;
                    var analysis = ErrorParser.Parse(raw);
                    item.Status = Strings.StatusFailed + ": " + ErrorMessages.GetMessage(analysis.Kind);
                    failed++;
                    AppendError(file, analysis, raw);
                }
            }

            OverallProgress.Value = 100;
            SummaryText.Text = string.Format(Strings.BatchSummary, done, _files.Length, failed);
            CloseButton.IsEnabled = true;

            if (_outputPaths.Count > 0) OpenFolderButton.Visibility = Visibility.Visible;
            if (failed > 0)
            {
                ErrorExpander.Visibility = Visibility.Visible;
                CopyErrorButton.Visibility = Visibility.Visible;
            }
        }

        private void AppendError(string file, ErrorAnalysis analysis, string raw)
        {
            ErrorLogBox.AppendText(
                "[" + Path.GetFileName(file) + "] "
                + ErrorMessages.GetMessage(analysis.Kind) + " — "
                + ErrorMessages.GetSuggestion(analysis.Kind)
                + Environment.NewLine + (raw ?? string.Empty) + Environment.NewLine + Environment.NewLine);
        }

        private void OnCopyError(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ErrorLogBox.Text);
            }
            catch
            {
                // 忽略剪贴板占用
            }
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            if (_outputPaths.Count == 0) return;
            var path = _outputPaths[0];
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

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class BatchItem : INotifyPropertyChanged
    {
        private string _status;

        public string FileName { get; set; }

        public string Status
        {
            get { return _status; }
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
