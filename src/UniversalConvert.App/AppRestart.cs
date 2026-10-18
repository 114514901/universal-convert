using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Diagnostics;

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

        /// <summary>
        /// 启动一个新实例并关闭当前进程。存在暂存扩展更新时优先交给独立更新器，
        /// 由其在主程序退出后应用更新并重启，避免主程序自己覆盖已加载的插件 DLL。
        /// </summary>
        public static void Restart()
        {
            var exePath = Assembly.GetEntryAssembly().Location;
            try
            {
                if (ExtensionCenter.HasPendingUpdates() || ExtensionCenter.HasPendingUninstalls())
                {
                    var updater = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "UniversalConvert.Updater.exe");
                    if (File.Exists(updater))
                    {
                        var arguments = "--parent " + Process.GetCurrentProcess().Id
                            + " --app \"" + exePath + "\" --apply-extensions";
                        Process.Start(new ProcessStartInfo(updater, arguments)
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                        });
                        Log.Info("已启动独立更新器，等待主程序退出后应用暂存扩展更新。");
                        Application.Current.Shutdown();
                        return;
                    }
                }

                Process.Start(exePath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Warn("重启失败，尝试直接重启：" + ex.Message);
                try
                {
                    Process.Start(exePath);
                    Application.Current.Shutdown();
                }
                catch
                {
                    // 重启失败则仅关闭
                }
            }
        }
    }
}
