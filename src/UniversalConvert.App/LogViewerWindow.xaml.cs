using System.IO;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.App
{
    /// <summary>日志查看器：只读展示当前日志文件内容，支持刷新与打开日志目录。</summary>
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow()
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            PathText.Text = Log.FilePath ?? string.Empty;
            LoadLog();
        }

        private void LoadLog()
        {
            try
            {
                var path = Log.FilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    LogText.Text = Strings.EmptyLog;
                }
                else
                {
                    LogText.Text = File.ReadAllText(path);
                }
                LogText.ScrollToEnd();
            }
            catch
            {
                LogText.Text = Strings.EmptyLog;
            }
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            LoadLog();
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            var dir = Path.GetDirectoryName(Log.FilePath);
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(UniversalConvert.Core.Config.ConfigStore.ConfigDirectory, "logs");
            }
            try { Directory.CreateDirectory(dir); } catch { }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }
    }
}
