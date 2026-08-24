using System.IO;
using System.Windows;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>崩溃报告窗口：左栏崩溃报告、右栏运行日志，支持复制与打开日志目录。</summary>
    public partial class CrashReportWindow : Window
    {
        private readonly string _reportText;
        private readonly string _logText;
        private readonly string _logsDir;

        public CrashReportWindow(string summary, string reportText, string logText, string logsDir)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            SummaryText.Text = summary;
            ReportText.Text = reportText;
            LogText.Text = logText;
            _reportText = reportText;
            _logText = logText;
            _logsDir = logsDir;
        }

        private void OnCopyReport(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(_reportText); } catch { }
        }

        private void OnCopyLog(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(_logText); } catch { }
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_logsDir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + _logsDir + "\"");
            }
            catch { }
        }

        private void OnRestart(object sender, RoutedEventArgs e)
        {
            try
            {
                // 启动一个全新实例（正常模式），再关闭当前报告进程
                var exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                System.Diagnostics.Process.Start(exe);
            }
            catch
            {
                // 启动失败则仅关闭
            }
            Application.Current.Shutdown();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
