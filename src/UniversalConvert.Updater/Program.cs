using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using UniversalConvert.App;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.Updater
{
    /// <summary>
    /// 独立更新器：不加载任何扩展插件，等待主程序退出后应用暂存扩展更新，
    /// 或启动主程序安装包。它只接受本地路径，不执行任意命令。
    /// </summary>
    internal static class Program
    {
        private const int WaitTimeoutMs = 60000;

        [STAThread]
        private static int Main(string[] args)
        {
            var options = UpdaterOptions.Parse(args);
            ConfigureLog();

            if (options.Errors.Count > 0)
            {
                foreach (var error in options.Errors) Log.Error(error);
                ShowError(string.Join(Environment.NewLine, options.Errors.ToArray()));
                return 2;
            }

            Log.Info("独立更新器启动；父进程 " + options.ParentPid
                + "；应用 " + options.AppPath
                + "；扩展更新 " + options.ApplyExtensions
                + "；安装包 " + (options.InstallerPath ?? "无"));

            if (!WaitForParent(options.ParentPid))
            {
                return 3;
            }

            if (!string.IsNullOrEmpty(options.InstallerPath))
            {
                return LaunchInstaller(options);
            }

            if (options.ApplyExtensions)
            {
                ApplyExtensions();
            }

            if (options.Restart)
            {
                return RestartApp(options);
            }

            Log.Info("更新器完成，未要求重启应用。");
            return 0;
        }

        private static bool WaitForParent(int pid)
        {
            if (pid <= 0) return true;
            try
            {
                using (var parent = Process.GetProcessById(pid))
                {
                    Log.Info("等待主程序退出：" + pid);
                    if (!parent.WaitForExit(WaitTimeoutMs))
                    {
                        Log.Error("等待主程序退出超时，更新器放弃本次更新。");
                        ShowError("主程序尚未退出，更新未执行。请关闭主程序后重试。");
                        return false;
                    }
                }
                Log.Info("主程序已退出。");
                return true;
            }
            catch (ArgumentException)
            {
                // 父进程已退出
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("等待主程序退出失败：" + ex);
                ShowError("无法确认主程序已退出，更新未执行：" + ex.Message);
                return false;
            }
        }

        private static int LaunchInstaller(UpdaterOptions options)
        {
            var installer = options.InstallerPath;
            if (!File.Exists(installer))
            {
                Log.Error("安装包不存在：" + installer);
                ShowError("安装包不存在：" + installer);
                return 4;
            }

            var appDirectory = Path.GetDirectoryName(options.AppPath) ?? string.Empty;
            if (string.IsNullOrEmpty(appDirectory))
            {
                ShowError("无法确定应用安装目录。");
                return 4;
            }

            try
            {
                var arguments = "/SILENT /NORESTART /FORCECLOSEAPPLICATIONS "
                    + "/MERGETASKS=runapp,contextmenu /DIR=\"" + appDirectory.TrimEnd('\\') + "\"";
                Log.Info("启动主程序安装包：" + installer + " " + arguments);
                Process.Start(new ProcessStartInfo(installer)
                {
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("启动安装包失败：" + ex);
                ShowError("无法启动安装包：" + ex.Message);
                return 4;
            }
        }

        private static void ApplyExtensions()
        {
            var updates = false;
            var uninstalls = false;
            try
            {
                Log.Info("开始应用暂存扩展更新。");
                updates = ExtensionCenter.ApplyPendingUpdates();
                Log.Info("暂存扩展更新应用结果：" + updates);
            }
            catch (Exception ex)
            {
                Log.Error("应用暂存扩展更新失败：" + ex);
            }

            try
            {
                Log.Info("开始应用暂存扩展卸载。");
                uninstalls = ExtensionCenter.ApplyPendingUninstalls();
                Log.Info("暂存扩展卸载应用结果：" + uninstalls);
            }
            catch (Exception ex)
            {
                Log.Error("应用暂存扩展卸载失败：" + ex);
            }

            if (!updates || !uninstalls)
            {
                // 不阻塞重启：主程序启动阶段仍会重试暂存应用。
                var message = "部分扩展更新未能应用，将在主程序下次启动时重试。";
                Log.Warn(message);
                ShowInfo(message);
            }
        }

        private static int RestartApp(UpdaterOptions options)
        {
            if (!File.Exists(options.AppPath))
            {
                Log.Error("应用不存在，无法重启：" + options.AppPath);
                ShowError("应用不存在，无法重启：" + options.AppPath);
                return 4;
            }

            try
            {
                Log.Info("重启主程序：" + options.AppPath);
                Process.Start(new ProcessStartInfo(options.AppPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(options.AppPath) ?? string.Empty
                });
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("重启主程序失败：" + ex);
                ShowError("扩展更新已完成，但重启主程序失败：" + ex.Message);
                return 4;
            }
        }

        private static void ConfigureLog()
        {
            try
            {
                var directory = Path.Combine(ConfigStore.ConfigDirectory, "logs");
                Directory.CreateDirectory(directory);
                Log.Configure(Path.Combine(directory, "updater.log"), LogLevel.Info);
            }
            catch
            {
                // 日志不可用时仍继续更新，错误通过 MessageBox 报告
            }
        }

        private static void ShowError(string message)
        {
            try
            {
                MessageBox.Show(message, "UniversalConvert 更新器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private static void ShowInfo(string message)
        {
            try
            {
                MessageBox.Show(message, "UniversalConvert 更新器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
