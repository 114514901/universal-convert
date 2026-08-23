using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;

namespace UniversalConvert.App
{
    public partial class MainWindow : Window
    {
        private readonly CoreHost _host;
        private string _selectedFile;
        private ConversionEntry[] _availableConversions = new ConversionEntry[0];

        public MainWindow(CoreHost host, string[] initialFiles = null)
        {
            InitializeComponent();
            _host = host;

            if (initialFiles != null && initialFiles.Length > 0 && File.Exists(initialFiles[0]))
            {
                SelectFile(initialFiles[0]);
            }
        }

        private void OnSelectFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Strings.SelectFileDialogTitle,
                Filter = Strings.AllFilesFilter
            };
            if (dialog.ShowDialog() == true)
            {
                SelectFile(dialog.FileName);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                SelectFile(files[0]);
            }
        }

        private void SelectFile(string path)
        {
            _selectedFile = path;
            SelectedFileText.Text = path;

            var ext = Path.GetExtension(path);
            _availableConversions = _host.Registry.GetConversionsFor(ext).ToArray();

            OutputList.ItemsSource = _availableConversions.Select(c =>
                $"{c.OutputDisplayName}  (.{c.OutputExtension})" +
                (c.HasCustomizableOptions ? "  ▸" : "") +
                (c.IsAvailable ? "" : Strings.ToolNotInstalled)).ToList();

            OutputList.SelectedIndex = -1;
            CustomizeButton.IsEnabled = false;
            ConvertButton.IsEnabled = _availableConversions.Length > 0;
        }

        private void OnOutputSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CustomizeButton.IsEnabled = OutputList.SelectedIndex >= 0
                && _availableConversions[OutputList.SelectedIndex].HasCustomizableOptions;
        }

        private void OnCustomize(object sender, RoutedEventArgs e)
        {
            if (OutputList.SelectedIndex < 0 || _selectedFile == null)
            {
                MessageBox.Show(Strings.PleaseSelectFormat, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entry = _availableConversions[OutputList.SelectedIndex];
            if (!entry.HasCustomizableOptions)
            {
                MessageBox.Show(Strings.NoParamsMessage,
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new CustomizeWindow(_host.Engine, _selectedFile, entry) { Owner = this };
            window.ShowDialog();
        }

        private void OnConvert(object sender, RoutedEventArgs e)
        {
            if (OutputList.SelectedIndex < 0 || _selectedFile == null)
            {
                MessageBox.Show(Strings.PleaseSelectFormat, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entry = _availableConversions[OutputList.SelectedIndex];
            var options = PresetMerger.Merge(entry.Options, entry.Presets, null, null);

            var request = new ConversionRequest
            {
                PluginId = entry.PluginId,
                InputPath = _selectedFile,
                InputExtension = Path.GetExtension(_selectedFile),
                OutputExtension = "." + entry.OutputExtension,
                Options = options
            };

            var window = new ConvertWindow(_host.Engine, request) { Owner = this };
            window.ShowDialog();
        }
    }
}
