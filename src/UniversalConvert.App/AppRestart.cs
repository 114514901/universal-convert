using System.Windows;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>应用重启辅助：用于设置/扩展等需要重启才能生效的操作。</summary>
    public static class AppRestart
    {
        /// <summary>弹出「需要重启才能生效，是否立即重启？」并按其选择执行。</summary>
        public static void PromptAndRestart()
        {
            var result = MessageBox.Show(
                Strings.ExtensionRestartPrompt,
                "UniversalConvert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                Restart();
            }
        }

        /// <summary>启动一个新实例并关闭当前进程。</summary>
        public static void Restart()
        {
            try
            {
                var exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
                System.Diagnostics.Process.Start(exePath);
                Application.Current.Shutdown();
            }
            catch
            {
                // 重启失败则仅关闭
            }
        }
    }
}
