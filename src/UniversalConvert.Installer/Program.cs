using System;
using System.Security.Principal;
using SharpShell;
using UniversalConvert.ContextMenu;
using UniversalConvert.Core.Config;

namespace UniversalConvert.Installer
{
    /// <summary>
    /// 注册/卸载右键菜单的命令行工具。
    /// 用法：UniversalConvert.Installer.exe install | uninstall
    /// 需以管理员身份运行（注册 COM 服务器到 HKLM）。
    /// </summary>
    internal static class Program
    {
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
                // 1. 写入安装目录，供右键菜单/主程序定位插件
                var config = new ConfigStore().Load();
                config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;
                new ConfigStore().Save(config);

                // 2. 注册 SharpShell 扩展
                ServerRegistrationManager.RegisterServer(
                    typeof(ConvertContextMenu),
                    ServerRegistrationManager.RegistrationType.OS64Bit);

                Console.WriteLine("右键菜单注册成功。请在资源管理器中刷新（或重启 explorer）后生效。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("注册失败：" + ex.Message);
                return 1;
            }
        }

        private static int Uninstall()
        {
            if (!EnsureAdmin()) return 1;

            try
            {
                ServerRegistrationManager.UnregisterServer(
                    typeof(ConvertContextMenu),
                    ServerRegistrationManager.RegistrationType.OS64Bit);

                Console.WriteLine("右键菜单已卸载。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("卸载失败：" + ex.Message);
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
            return false;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法：UniversalConvert.Installer.exe install | uninstall");
        }
    }
}
