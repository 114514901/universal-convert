using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        private TextBlock _updateStatusText;
        private UpdateInfo _updateInfo;
        private Button _viewNotesButton;
        private Button _downloadButton;
        private ProgressBar _updateProgressBar;

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

                // 更新类别追加手动检查更新
                if (group.Any(d => d.Category == "@SettingsCategoryUpdate"))
                {
                    panel.Children.Add(BuildUpdateActions());
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
            // 检测需要重启的设置项是否被修改
            var needsRestart = false;
            foreach (var definition in _manager.Definitions)
            {
                if (!definition.RequiresRestart) continue;
                if (!_getters.TryGetValue(definition.Key, out var getter)) continue;
                if (!string.Equals(_manager.Get(definition.Key), getter(), StringComparison.Ordinal))
                {
                    needsRestart = true;
                    break;
                }
            }

            // 保存
            foreach (var definition in _manager.Definitions)
            {
                if (_getters.TryGetValue(definition.Key, out var getter))
                {
                    _manager.Set(definition.Key, getter());
                }
            }
            _manager.Save();

            // 主题色立即生效（无需重启）
            if (_getters.TryGetValue("themeAccent", out var accentGetter))
            {
                App.ApplyAccentColor(accentGetter());
            }

            // 需要重启则询问
            if (needsRestart)
            {
                var result = MessageBox.Show(
                    Strings.RestartRequiredMessage,
                    "UniversalConvert",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    AppRestart.Restart();
                }
            }

            Close();
        }

        private FrameworkElement BuildUpdateActions()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            var checkButton = new Button { Content = Strings.CheckUpdate, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left };
            checkButton.Click += OnCheckUpdate;

            _updateStatusText = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray) };

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            _viewNotesButton = new Button { Content = Strings.ViewReleaseNotes, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
            _viewNotesButton.Click += OnViewReleaseNotes;
            _downloadButton = new Button { Content = Strings.DownloadUpdate, Padding = new Thickness(16, 6, 16, 6), Visibility = Visibility.Collapsed };
            _downloadButton.Click += OnDownloadUpdate;
            actionRow.Children.Add(_viewNotesButton);
            actionRow.Children.Add(_downloadButton);

            _updateProgressBar = new ProgressBar { Height = 6, Margin = new Thickness(0, 8, 0, 0), Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };

            panel.Children.Add(checkButton);
            panel.Children.Add(_updateStatusText);
            panel.Children.Add(actionRow);
            panel.Children.Add(_updateProgressBar);
            return panel;
        }

        private async void OnCheckUpdate(object sender, RoutedEventArgs e)
        {
            _updateStatusText.Text = Strings.CheckingUpdate;
            _viewNotesButton.Visibility = Visibility.Collapsed;
            _downloadButton.Visibility = Visibility.Collapsed;
            _updateProgressBar.Visibility = Visibility.Collapsed;

            var info = await UpdateChecker.CheckAsync(_manager.Get("updateChannel"));
            if (info != null)
            {
                _updateInfo = info;
                _updateStatusText.Text = string.Format(Strings.UpdateAvailable, info.Version);
                _viewNotesButton.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(info.DownloadUrl))
                {
                    _downloadButton.Visibility = Visibility.Visible;
                }
            }
            else
            {
                _updateInfo = null;
                _updateStatusText.Text = Strings.UpToDate;
            }
        }

        private void OnViewReleaseNotes(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null) return;
            var window = new ReleaseNotesWindow(_updateInfo.Version, _updateInfo.Body) { Owner = this };
            window.ShowDialog();
        }

        private async void OnDownloadUpdate(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.DownloadUrl)) return;

            _downloadButton.IsEnabled = false;
            _viewNotesButton.IsEnabled = false;
            _updateProgressBar.Visibility = Visibility.Visible;

            var dest = Path.Combine(Path.GetTempPath(), "UniversalConvert-Setup-" + _updateInfo.Version.TrimStart('v', 'V') + ".exe");

            var progress = new Progress<double>(p =>
            {
                _updateProgressBar.Value = p;
                _updateStatusText.Text = string.Format(Strings.Downloading, (int)p);
            });

            try
            {
                await UpdateChecker.DownloadAsync(_updateInfo.DownloadUrl, dest, progress, CancellationToken.None);

                _updateProgressBar.Value = 100;
                _updateStatusText.Text = Strings.DownloadComplete;
                try { LaunchInstallerSilent(dest); } catch { /* 启动安装程序失败 */ }
            }
            catch (Exception ex)
            {
                _updateStatusText.Text = string.Format(Strings.DownloadFailed, ex.Message);
                _downloadButton.IsEnabled = true;
                _viewNotesButton.IsEnabled = true;
            }
        }

        private void LaunchInstallerSilent(string installerPath)
        {
            var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var psi = new System.Diagnostics.ProcessStartInfo(installerPath)
            {
                // 自动更新必须保持右键菜单：/MERGETASKS 额外勾选 contextmenu 强制注册，
                // 避免受安装器任务「记忆状态」（用户曾在手动安装中取消过勾选）影响而静默丢失右键菜单
                Arguments = "/SILENT /NORESTART /MERGETASKS=runapp,contextmenu /DIR=\"" + installDir + "\"",
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(psi);
        }

        private FrameworkElement BuildAdvancedActions()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            var viewLog = new Button { Content = Strings.ViewLog, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left };
            viewLog.Click += OnViewLog;

            var clearLog = new Button { Content = Strings.ClearLog, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            clearLog.Click += OnClearLog;

            var crashTest = new Button { Content = Strings.CrashTest, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            crashTest.Click += OnCrashTest;

            var testHang = new Button { Content = Strings.TestHang, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            testHang.Click += OnTestHang;

            var clearChoices = new Button { Content = Strings.ClearFormatChoices, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            clearChoices.Click += OnClearFormatChoices;

            panel.Children.Add(viewLog);
            panel.Children.Add(clearLog);
            panel.Children.Add(crashTest);
            panel.Children.Add(testHang);
            panel.Children.Add(clearChoices);
            return panel;
        }

        private void OnClearFormatChoices(object sender, RoutedEventArgs e)
        {
            FormatResolver.ClearAllChoices(_manager);
            _manager.Save();
            MessageBox.Show(Strings.FormatChoicesCleared, "UniversalConvert",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            // 后台线程抛未处理异常，走 AppDomain.UnhandledException，App 会真的退出。
            // 用原始 Thread 而非 Task.Run：Task.Run 的异常会变成"未观察的 Task 异常"被 .NET 静默吞掉。
            var thread = new Thread(() =>
            {
                throw new InvalidOperationException("崩溃测试：后台线程未处理异常");
            })
            {
                IsBackground = true
            };
            thread.Start();
        }

        private void OnTestHang(object sender, RoutedEventArgs e)
        {
            // 阻塞 UI 线程 60 秒模拟真实卡死：看护进程应在约 15~20 秒内检测到并结束进程。
            // 若看护进程未生效，60 秒后自动恢复，避免永久卡死。
            System.Threading.Thread.Sleep(60000);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
