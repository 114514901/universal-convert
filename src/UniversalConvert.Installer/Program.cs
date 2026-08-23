using System;
using System.IO;
using System.Security.Principal;
using SharpShell.ServerRegistration;
using UniversalConvert.ContextMenu;
using UniversalConvert.Core.Config;

namespace UniversalConvert.Installer
{
    /// <summary>
    /// 注册/卸载右键菜单的命令行工具。
    /// 用法：UniversalConvert.Installer.exe install | uninstall
    /// 需以管理员身份运行（注册 COM 服务器到 HKLM）。
    /// 日志写入 %AppData%\UniversalConvert\install.log，便于排查问题。
    /// </summary>
    internal static class Program
    {
        private static readonly string LogPath = Path.Combine(ConfigStore.ConfigDirectory, "install.log");

        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "install":
                    return Install();
                case "uninstall":
                    return Uninstall();
                default:
                    PrintUsage();
                    return 1;
            }
        }

        private static int Install()
        {
            if (!EnsureAdmin()) return 1;

            try
            {
                Log("=== 开始注册右键菜单 ===");

                // 1. 写入安装目录，供右键菜单/主程序定位插件
                var config = new ConfigStore().Load();
                config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;
                new ConfigStore().Save(config);
                Log("安装目录: " + config.InstallDirectory);

                // 2. 注册 SharpShell 扩展
                var server = new ConvertContextMenu();
                ServerRegistrationManager.RegisterServer(server, RegistrationType.OS64Bit);

                Log("注册成功。");
                Console.WriteLine("右键菜单注册成功。重启资源管理器（或注销重登）后生效。");
                return 0;
            }
            catch (Exception ex)
            {
                Log("注册失败: " + ex);
                Console.Error.WriteLine("注册失败：" + ex.Message + "（详见 " + LogPath + "）");
                return 1;
            }
        }

        private static int Uninstall()
        {
            if (!EnsureAdmin()) return 1;

            try
            {
                Log("=== 开始卸载右键菜单 ===");
                ServerRegistrationManager.UnregisterServer(new ConvertContextMenu(), RegistrationType.OS64Bit);
                Log("卸载成功。");
                Console.WriteLine("右键菜单已卸载。");
                return 0;
            }
            catch (Exception ex)
            {
                Log("卸载失败: " + ex);
                Console.Error.WriteLine("卸载失败：" + ex.Message + "（详见 " + LogPath + "）");
                return 1;
            }
        }

        private static bool EnsureAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            if (isAdmin) return true;

            Console.Error.WriteLine("需要以管理员身份运行此命令。");
            Log("需要管理员身份，已中止。");
            return false;
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.ConfigDirectory);
                File.AppendAllText(
                    LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法：UniversalConvert.Installer.exe install | uninstall");
        }
    }
}
