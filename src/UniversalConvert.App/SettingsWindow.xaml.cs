using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// 通用设置界面：根据 SettingDefinition 列表动态渲染控件，按分类（Category）生成选项卡。
    /// 标签/分组/候选项以 '@资源键' 形式经 Strings.L 本地化。新增设置项无需改这里。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsManager _manager;
        private readonly Dictionary<string, Func<string>> _getters = new Dictionary<string, Func<string>>();
        private readonly Dictionary<string, Action<string>> _setters = new Dictionary<string, Action<string>>();

        public SettingsWindow(SettingsManager manager)
        {
            InitializeComponent();
            _manager = manager;
            Icon = AppIcon.Get();

            BuildControls();
        }

        private void BuildControls()
        {
            var groups = _manager.Definitions.GroupBy(d => Strings.L(d.Category) ?? Strings.Settings);

            foreach (var group in groups)
            {
                var tab = new TabItem { Header = group.Key };
                var panel = new StackPanel { Margin = new Thickness(12) };

                foreach (var definition in group)
                {
                    panel.Children.Add(BuildRow(definition));
                }

                // 高级类别追加特殊操作按钮（崩溃测试 / 查看日志 / 清理日志）
                if (group.Any(d => d.Category == "@SettingsCategoryAdvanced"))
                {
                    panel.Children.Add(BuildAdvancedActions());
                }

                tab.Content = panel;
                SettingsTabs.Items.Add(tab);
            }
        }

        private FrameworkElement BuildRow(SettingDefinition definition)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

            var label = new TextBlock
            {
                Text = Strings.L(definition.Label),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            if (!string.IsNullOrEmpty(definition.Description))
            {
                label.ToolTip = Strings.L(definition.Description);
            }
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            FrameworkElement control;
            Action<string> setter;
            Func<string> getter;

            switch (definition.Type)
            {
                case OptionType.Bool:
                    var checkBox = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                    setter = v => checkBox.IsChecked = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    getter = () => checkBox.IsChecked == true ? "true" : "false";
                    control = checkBox;
                    break;

                case OptionType.Enum:
                    var choices = definition.Choices
                        .Select(c => new OptionChoice { Value = c.Value, Label = Strings.L(c.Label) })
                        .ToList();
                    var combo = new ComboBox { Width = 220, IsEditable = true, IsTextSearchEnabled = false };
                    combo.ItemsSource = choices;
                    combo.DisplayMemberPath = "Label";
                    setter = v =>
                    {
                        var match = choices.FirstOrDefault(c => c.Value == v);
                        if (match != null) combo.SelectedItem = match;
                        else combo.Text = v ?? string.Empty;
                    };
                    getter = () =>
                    {
                        var text = combo.Text ?? string.Empty;
                        var byLabel = choices.FirstOrDefault(c => c.Label == text);
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

            _setters[definition.Key] = setter;
            _getters[definition.Key] = getter;

            setter(_manager.Get(definition.Key));

            return row;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            foreach (var definition in _manager.Definitions)
            {
                if (_getters.TryGetValue(definition.Key, out var getter))
                {
                    _manager.Set(definition.Key, getter());
                }
            }
            _manager.Save();
            Close();
        }

        private FrameworkElement BuildAdvancedActions()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            var viewLog = new Button { Content = Strings.ViewLog, Padding = new Thickness(16, 6), HorizontalAlignment = HorizontalAlignment.Left };
            viewLog.Click += OnViewLog;

            var clearLog = new Button { Content = Strings.ClearLog, Padding = new Thickness(16, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            clearLog.Click += OnClearLog;

            var crashTest = new Button { Content = Strings.CrashTest, Padding = new Thickness(16, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            crashTest.Click += OnCrashTest;

            panel.Children.Add(viewLog);
            panel.Children.Add(clearLog);
            panel.Children.Add(crashTest);
            return panel;
        }

        private void OnViewLog(object sender, RoutedEventArgs e)
        {
            var window = new LogViewerWindow { Owner = this };
            window.ShowDialog();
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.Combine(ConfigStore.ConfigDirectory, "logs");
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "app-*.zip")) { try { File.Delete(f); } catch { } }
                    foreach (var f in Directory.GetFiles(dir, "crash-*")) { try { File.Delete(f); } catch { } }
                }
                MessageBox.Show(Strings.LogCleared, "UniversalConvert", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                // 忽略清理失败
            }
        }

        private void OnCrashTest(object sender, RoutedEventArgs e)
        {
            // 故意抛异常，触发崩溃报告器（会被 DispatcherUnhandledException 捕获并弹窗，App 不退出）
            throw new InvalidOperationException("崩溃测试：这是手动触发的异常，用于验证崩溃报告器。");
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
