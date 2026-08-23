using System;
using System.IO;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;

namespace UniversalConvert.App
{
    public partial class App : Application
    {
        private CoreHost _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var config = new ConfigStore().Load();
            config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _host = new CoreHost(config, config.ResolvePluginsDirectory(), Log);

            var parsed = CommandLineContract.Parse(e.Args);

            if (parsed.IsConvertMode && !string.IsNullOrEmpty(parsed.InputPath))
            {
                RunHeadlessConversion(parsed);
            }
            else if (parsed.IsCustomizeMode && !string.IsNullOrEmpty(parsed.InputPath))
            {
                RunCustomize(parsed);
            }
            else
            {
                var main = new MainWindow(_host, parsed.ExtraFiles);
                MainWindow = main;
                main.Show();
            }
        }

        private void RunHeadlessConversion(CommandLineContract.ParsedArguments parsed)
        {
            var inputPath = parsed.InputPath;
            if (!File.Exists(inputPath))
            {
                MessageBox.Show(Strings.InputFileMissing + inputPath, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var inputExt = Path.GetExtension(inputPath);
            var entry = _host.Registry.GetEntry(inputExt, parsed.OutputExtension);
            if (entry == null)
            {
                MessageBox.Show(
                    string.Format(Strings.UnsupportedConversion, inputExt, parsed.OutputExtension),
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            var options = PresetMerger.Merge(entry.Options, entry.Presets, parsed.PresetName, null);

            var request = new ConversionRequest
            {
                PluginId = entry.PluginId,
                InputPath = inputPath,
                OutputPath = parsed.OutputPath,
                InputExtension = inputExt,
                OutputExtension = "." + parsed.OutputExtension,
                Options = options
            };

            var window = new ConvertWindow(_host.Engine, request);
            MainWindow = window;
            window.Show();
        }

        private void RunCustomize(CommandLineContract.ParsedArguments parsed)
        {
            var inputPath = parsed.InputPath;
            if (!File.Exists(inputPath))
            {
                MessageBox.Show(Strings.InputFileMissing + inputPath, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var inputExt = Path.GetExtension(inputPath);
            var entry = _host.Registry.GetEntry(inputExt, parsed.OutputExtension);
            if (entry == null)
            {
                MessageBox.Show(
                    string.Format(Strings.UnsupportedConversion, inputExt, parsed.OutputExtension),
                    "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            var window = new CustomizeWindow(_host.Engine, inputPath, entry);
            MainWindow = window;
            window.Show();
        }

        private static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine("[UniversalConvert] " + message);
        }
    }
}
