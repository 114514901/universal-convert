using System;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;
using SharpShell.Helpers;
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

        /// <summary>主程序集固定 AssemblyVersion（与 Directory.Build.props 保持一致）。</summary>
        private const string CurrentAssemblyVersion = "1.0.0.0";

        private static readonly string ClsidRoot =
            @"Software\Classes\CLSID\{C1000000-0000-0000-0000-000000000001}";

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

                // 0. 清理历史版本注册残留：geek/手动卸载常留 InprocServer32\<旧AssemblyVersion> 子键，
                //    其 CodeBase 指向早已不存在的旧安装路径，可能导致 explorer 加载到错误程序集而菜单失效。
                CleanupStaleClsidVersions();

                // 1. 写入安装目录，供右键菜单/主程序定位插件
                var config = new ConfigStore().Load();
                config.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory;
                new ConfigStore().Save(config);
                Log("安装目录: " + config.InstallDirectory);

                // 2. 注册 COM 服务器（CLSID + InprocServer32）。
                //    注意：SharpShell 的 RegisterServer 只写右键菜单"关联"，CLSID 必须单独用 regasm /codebase 注册。
                var contextMenuPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UniversalConvert.ContextMenu.dll");
                var regAsm = new RegAsm();
                bool regOk = regAsm.Register64(contextMenuPath, true);
                Log("regasm 注册" + (regOk ? "成功" : "失败") + ": " + regAsm.StandardError);
                if (!regOk)
                {
                    throw new InvalidOperationException("regasm 注册 COM 服务器失败：" + regAsm.StandardError);
                }

                // 3. 注册 SharpShell 关联
                ServerRegistrationManager.RegisterServer(new ConvertContextMenu(), RegistrationType.OS64Bit);

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

        /// <summary>清理 CLSID\InprocServer32 下非当前程序集版本的历史子键（保留当前版本）。</summary>
        private static void CleanupStaleClsidVersions()
        {
            try
            {
                var inprocPath = ClsidRoot + @"\InprocServer32";
                using (var inproc = Registry.LocalMachine.OpenSubKey(inprocPath, writable: true))
                {
                    if (inproc == null) return;
                    foreach (var name in inproc.GetSubKeyNames())
                    {
                        if (string.Equals(name, CurrentAssemblyVersion, StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            inproc.DeleteSubKeyTree(name);
                            Log("已清理历史版本注册残留: InprocServer32\\" + name);
                        }
                        catch (Exception ex)
                        {
                            Log("清理残留 " + name + " 失败: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("清理历史版本注册残留失败: " + ex.Message);
            }
        }

        private static int Uninstall()
        {
            if (!EnsureAdmin()) return 1;

            try
            {
                Log("=== 开始卸载右键菜单 ===");
                ServerRegistrationManager.UnregisterServer(new ConvertContextMenu(), RegistrationType.OS64Bit);

                var contextMenuPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UniversalConvert.ContextMenu.dll");
                new RegAsm().Unregister64(contextMenuPath);

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
