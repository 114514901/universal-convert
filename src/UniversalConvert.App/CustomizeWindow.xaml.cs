using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// "更多设置"动态表单：根据 ConversionEntry 的参数 schema 通用渲染控件，
    /// 不针对任何具体格式硬编码。预设只是"填充表单"的快捷方式。
    /// </summary>
    public partial class CustomizeWindow : Window
    {
        private readonly ConversionEngine _engine;
        private readonly string _inputPath;
        private readonly ConversionEntry _entry;

        private readonly Dictionary<string, Func<string>> _getters = new Dictionary<string, Func<string>>();
        private readonly Dictionary<string, Action<string>> _setters = new Dictionary<string, Action<string>>();
        private bool _suppressPresetChanged;

        public CustomizeWindow(ConversionEngine engine, string inputPath, ConversionEntry entry)
        {
            InitializeComponent();
            _engine = engine;
            _inputPath = inputPath;
            _entry = entry;

            FileText.Text = Path.GetFileName(inputPath);
            FormatText.Text = string.Format(Strings.TargetFormat, entry.OutputDisplayName, entry.OutputExtension);

            BuildPresetCombo();
            BuildOptionControls();
            ApplyPresetValues(null);
        }

        private void BuildPresetCombo()
        {
            PresetCombo.Items.Add(Strings.DefaultRecommended);
            foreach (var preset in _entry.Presets)
            {
                PresetCombo.Items.Add(preset.Name);
            }
            PresetCombo.SelectedIndex = 0;
        }

        private void BuildOptionControls()
        {
            foreach (var option in _entry.Options)
            {
                // 文字靠左、控件靠右对齐
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

                var label = new TextBlock
                {
                    Text = option.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);

                FrameworkElement control;
                Action<string> setter;
                Func<string> getter;

                switch (option.Type)
                {
                    case OptionType.Bool:
                        var checkBox = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                        setter = v => checkBox.IsChecked = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                        getter = () => checkBox.IsChecked == true ? "true" : "false";
                        control = checkBox;
                        break;

                    case OptionType.Enum:
                        var combo = new ComboBox { Width = 220, IsEditable = true, IsTextSearchEnabled = false };
                        combo.ItemsSource = option.Choices;
                        combo.DisplayMemberPath = "Label";
                        setter = v =>
                        {
                            var match = option.Choices.FirstOrDefault(c => c.Value == v);
                            if (match != null)
                            {
                                combo.SelectedItem = match;
                            }
                            else
                            {
                                combo.Text = v ?? string.Empty;
                            }
                        };
                        getter = () =>
                        {
                            // 若文本与某候选项的标签一致，取其值；否则当作手动输入返回
                            var text = combo.Text ?? string.Empty;
                            var byLabel = option.Choices.FirstOrDefault(c => c.Label == text);
                            return byLabel != null ? byLabel.Value : text;
                        };
                        control = combo;
                        break;

                    case OptionType.Int:
                    case OptionType.String:
                    default:
                        var textBox = new TextBox { Width = 220 };
                        setter = v => textBox.Text = v ?? string.Empty;
                        getter = () => textBox.Text;
                        control = textBox;
                        break;
                }

                DockPanel.SetDock(control, Dock.Right);
                row.Children.Add(control);
                OptionsPanel.Children.Add(row);

                _setters[option.Key] = setter;
                _getters[option.Key] = getter;
            }
        }

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetChanged) return;
            var presetName = PresetCombo.SelectedIndex <= 0 ? null : (string)PresetCombo.SelectedItem;
            ApplyPresetValues(presetName);
        }

        private void ApplyPresetValues(string presetName)
        {
            _suppressPresetChanged = true;
            try
            {
                var preset = presetName == null ? null : FindPreset(presetName);

                foreach (var option in _entry.Options)
                {
                    string value = option.DefaultValue;
                    if (preset != null && preset.Options != null && preset.Options.TryGetValue(option.Key, out var presetValue))
                    {
                        value = presetValue;
                    }

                    if (_setters.TryGetValue(option.Key, out var setter))
                    {
                        setter(value);
                    }
                }
            }
            finally
            {
                _suppressPresetChanged = false;
            }
        }

        private ConversionPreset FindPreset(string name)
        {
            foreach (var preset in _entry.Presets)
            {
                if (preset.Name == name) return preset;
            }
            return null;
        }

        private void OnConvert(object sender, RoutedEventArgs e)
        {
            var overrides = new Dictionary<string, string>();
            foreach (var kv in _getters)
            {
                var value = kv.Value();
                if (value != null)
                {
                    overrides[kv.Key] = value;
                }
            }

            // 表单已是最终值的唯一来源：预设仅用于填充，最终以表单 + 默认合并
            var options = PresetMerger.Merge(_entry.Options, null, null, overrides);

            var request = new ConversionRequest
            {
                PluginId = _entry.PluginId,
                InputPath = _inputPath,
                InputExtension = Path.GetExtension(_inputPath),
                OutputExtension = "." + _entry.OutputExtension,
                Options = options
            };

            var window = new ConvertWindow(_engine, request) { Owner = this };
            window.ShowDialog();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
