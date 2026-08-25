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
    /// "更多设置"动态表单：根据 ConversionEntry 的参数 schema 通用渲染控件。
    /// 预设只是"填充表单"的快捷方式；用户手改参数后预设栏自动切到「自定义」；
    /// 预设「默认」等价于全部参数为「原始」（不重编码）。
    /// </summary>
    public partial class CustomizeWindow : Window
    {
        private readonly ConversionEngine _engine;
        private readonly string _inputPath;
        private readonly ConversionEntry _entry;

        private readonly Dictionary<string, Func<string>> _getters = new Dictionary<string, Func<string>>();
        private readonly Dictionary<string, Action<string>> _setters = new Dictionary<string, Action<string>>();
        private bool _suppressPresetChanged;
        private bool _suppressOptionChanged;
        private bool _collectMode;
        private CustomizeResult _collected;

        public CustomizeWindow(ConversionEngine engine, string inputPath, ConversionEntry entry,
            IDictionary<string, string> previousOptions = null)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _engine = engine;
            _inputPath = inputPath;
            _entry = entry;

            FileText.Text = Path.GetFileName(inputPath);
            FormatText.Text = string.Format(Strings.TargetFormat, entry.OutputDisplayName, entry.OutputExtension);

            BuildPresetCombo();
            BuildOptionControls();
            if (previousOptions != null)
            {
                ApplySavedOptions(previousOptions);
            }
            else
            {
                ApplyPresetValues(null);
            }
        }

        /// <summary>弹出参数表单收集结果（不执行转换）；取消返回 null。previousOptions 为上次保存的参数，用于回填。</summary>
        public static CustomizeResult Collect(Window owner, ConversionEntry entry, string inputPath,
            IDictionary<string, string> previousOptions = null)
        {
            var window = new CustomizeWindow(null, inputPath, entry, previousOptions) { Owner = owner };
            window._collectMode = true;
            window.ConvertButton.Content = Strings.Save;
            var ok = window.ShowDialog() == true;
            return ok ? window._collected : null;
        }

        private int CustomIndex => PresetCombo.Items.Count - 1;

        private void BuildPresetCombo()
        {
            PresetCombo.Items.Add(Strings.DefaultRecommended);
            foreach (var preset in _entry.Presets)
            {
                PresetCombo.Items.Add(preset.Name);
            }
            PresetCombo.Items.Add(Strings.ManualCustom);
            PresetCombo.SelectedIndex = 0;
        }

        private void BuildOptionControls()
        {
            foreach (var option in _entry.Options)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

                var label = new TextBlock
                {
                    Text = Strings.L(option.Label),
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
                        checkBox.Checked += (s, e) => OnOptionManuallyChanged();
                        checkBox.Unchecked += (s, e) => OnOptionManuallyChanged();
                        control = checkBox;
                        break;

                    case OptionType.Enum:
                        var combo = new ComboBox { Width = 220, IsEditable = true, IsTextSearchEnabled = false };
                        var choices = option.Choices
                            .Select(c => new OptionChoice { Value = c.Value, Label = Strings.L(c.Label) })
                            .ToList();
                        combo.ItemsSource = choices;
                        combo.DisplayMemberPath = "Label";
                        setter = v =>
                        {
                            var match = choices.FirstOrDefault(c => c.Value == v);
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
                            var text = combo.Text ?? string.Empty;
                            var byLabel = choices.FirstOrDefault(c => c.Label == text);
                            return byLabel != null ? byLabel.Value : text;
                        };
                        combo.SelectionChanged += (s, e) => OnOptionManuallyChanged();
                        control = combo;
                        break;

                    case OptionType.Int:
                    case OptionType.String:
                    default:
                        var textBox = new TextBox { Width = 220 };
                        setter = v => textBox.Text = v ?? string.Empty;
                        getter = () => textBox.Text;
                        textBox.TextChanged += (s, e) => OnOptionManuallyChanged();
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

        private void OnOptionManuallyChanged()
        {
            if (_suppressPresetChanged || _suppressOptionChanged) return;
            if (PresetCombo.SelectedIndex != CustomIndex)
            {
                PresetCombo.SelectedIndex = CustomIndex;
            }
        }

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetChanged) return;

            var idx = PresetCombo.SelectedIndex;
            if (idx == CustomIndex)
            {
                // 选中「自定义」：保留手改值，不改参数
                return;
            }

            var presetName = idx <= 0 ? null : (string)PresetCombo.SelectedItem;
            ApplyPresetValues(presetName);
        }

        private void ApplyPresetValues(string presetName)
        {
            _suppressPresetChanged = true;
            _suppressOptionChanged = true;
            try
            {
                var preset = presetName == null ? null : FindPreset(presetName);

                foreach (var option in _entry.Options)
                {
                    // 默认（无预设）= 用插件声明的 DefaultValue（多数为空 = 原始不重编码；gif 帧率等为具体默认值）
                    string value = option.DefaultValue ?? string.Empty;
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
                _suppressOptionChanged = false;
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

        /// <summary>
        /// 用上次保存的参数回填表单，并恢复预设选择（默认 / 命中命名预设 / 自定义）。
        /// saved 是合并后的最终参数字典（空值已被剔除）：键存在 → 控件取该值；键不存在 → 空（等价「原始」）。
        /// </summary>
        private void ApplySavedOptions(IDictionary<string, string> saved)
        {
            _suppressPresetChanged = true;
            _suppressOptionChanged = true;
            try
            {
                foreach (var option in _entry.Options)
                {
                    string value;
                    var hasValue = saved.TryGetValue(option.Key, out value);
                    if (_setters.TryGetValue(option.Key, out var setter))
                    {
                        setter(hasValue ? value : string.Empty);
                    }
                }

                // 恢复预设选择（抑制期内设置，避免 OnPresetChanged 用默认值覆盖回填值）
                if (MatchesDefault(saved))
                {
                    PresetCombo.SelectedIndex = 0;
                }
                else
                {
                    int idx = FindMatchingPresetIndex(saved);
                    PresetCombo.SelectedIndex = idx >= 0 ? idx + 1 : CustomIndex;
                }
            }
            finally
            {
                _suppressPresetChanged = false;
                _suppressOptionChanged = false;
            }
        }

        /// <summary>保存的参数是否等于「默认（推荐）」语义：空 DefaultValue 的键应不存在，非空的应等于 DefaultValue。</summary>
        private bool MatchesDefault(IDictionary<string, string> saved)
        {
            foreach (var option in _entry.Options)
            {
                var defaultValue = option.DefaultValue ?? string.Empty;
                string savedValue;
                var hasSaved = saved.TryGetValue(option.Key, out savedValue);

                if (string.IsNullOrEmpty(defaultValue))
                {
                    if (hasSaved) return false;
                }
                else
                {
                    if (!hasSaved || !string.Equals(savedValue, defaultValue, StringComparison.Ordinal)) return false;
                }
            }
            return true;
        }

        /// <summary>保存的参数是否完全匹配某个命名预设（预设覆盖的键值一致、未覆盖的键保持默认语义）；不匹配返回 -1。</summary>
        private int FindMatchingPresetIndex(IDictionary<string, string> saved)
        {
            for (int i = 0; i < _entry.Presets.Count; i++)
            {
                var preset = _entry.Presets[i];
                bool match = true;

                foreach (var kv in preset.Options)
                {
                    string savedValue;
                    if (!saved.TryGetValue(kv.Key, out savedValue)
                        || !string.Equals(savedValue, kv.Value, StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;

                foreach (var option in _entry.Options)
                {
                    if (preset.Options.ContainsKey(option.Key)) continue;
                    var defaultValue = option.DefaultValue ?? string.Empty;
                    string savedValue;
                    var hasSaved = saved.TryGetValue(option.Key, out savedValue);
                    if (string.IsNullOrEmpty(defaultValue))
                    {
                        if (hasSaved) { match = false; break; }
                    }
                    else
                    {
                        if (!hasSaved || !string.Equals(savedValue, defaultValue, StringComparison.Ordinal)) { match = false; break; }
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private IDictionary<string, string> BuildOptions()
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
            return PresetMerger.Merge(_entry.Options, null, null, overrides);
        }

        private CustomizeResult BuildResult()
        {
            return new CustomizeResult
            {
                Options = BuildOptions(),
                Label = ComputeLabel()
            };
        }

        private string ComputeLabel()
        {
            var idx = PresetCombo.SelectedIndex;
            if (idx == CustomIndex) return Strings.ManualCustom;
            if (idx <= 0) return Strings.Original;
            return (string)PresetCombo.SelectedItem;
        }

        private void OnConvert(object sender, RoutedEventArgs e)
        {
            var result = BuildResult();

            if (_collectMode)
            {
                _collected = result;
                DialogResult = true;
                return;
            }

            var request = new ConversionRequest
            {
                PluginId = _entry.PluginId,
                InputPath = _inputPath,
                InputExtension = Path.GetExtension(_inputPath),
                OutputExtension = "." + _entry.OutputExtension,
                Options = result.Options
            };

            var window = new ConvertWindow(_engine, request) { Owner = this };
            window.ShowDialog();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>自定义表单的收集结果。</summary>
    public class CustomizeResult
    {
        /// <summary>合并后的参数（空值表示原始，不重编码）。</summary>
        public IDictionary<string, string> Options { get; set; }

        /// <summary>展示标签：原始 / 预设名 / 自定义。</summary>
        public string Label { get; set; }
    }
}
