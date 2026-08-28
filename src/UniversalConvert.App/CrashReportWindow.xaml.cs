using System;
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

        /// <summary>继续运行（有风险）：关闭报告窗口，应用继续。</summary>
        private void OnContinue(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>结束进程：正常退出流程（触发看护进程退出信号、保存设置等）。</summary>
        private void OnClose(object sender, RoutedEventArgs e)
        {
            try
            {
                Application.Current.Shutdown();
            }
            catch
            {
                // Shutdown 失败则强制退出，避免窗口关不掉
                Environment.Exit(1);
            }
            Close();
        }
    }
}
