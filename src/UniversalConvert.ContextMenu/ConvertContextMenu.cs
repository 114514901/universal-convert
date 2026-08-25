using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpShell.Attributes;
using SharpShell.SharpContextMenu;
using UniversalConvert.Core;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Engine;

namespace UniversalConvert.ContextMenu
{
    /// <summary>
    /// 动态右键菜单：根据被右键文件/目录的扩展名，生成"转换为"级联菜单。
    /// 依赖 Core 的 FormatRegistry 自动感知所有插件能力，新增插件无需改这里。
    /// 点击菜单项后调用 App.exe --convert 完成实际转换（转换在独立进程，不污染 explorer）。
    /// </summary>
    [ComVisible(true)]
    [Guid("C1000000-0000-0000-0000-000000000001")]
    [COMServerAssociation(AssociationType.AllFiles)]
    public class ConvertContextMenu : SharpContextMenu
    {
        private static readonly Lazy<CoreHost> Host = new Lazy<CoreHost>(LoadHost, true);

        private static readonly string LogFile = Path.Combine(ConfigStore.ConfigDirectory, "contextmenu.log");

        private static readonly Lazy<Bitmap> MenuIcon = new Lazy<Bitmap>(LoadMenuIcon);

        static ConvertContextMenu()
        {
            WriteLog("ContextMenu DLL 已加载，BaseDirectory=" + BaseDirectory);
        }

        private static string BaseDirectory
        {
            get
            {
                return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            }
        }

        private static Bitmap LoadMenuIcon()
        {
            try
            {
                var exePath = Path.Combine(BaseDirectory, "UniversalConvert.App.exe");
                using (var icon = Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon == null) return null;

                    var size = SystemInformation.SmallIconSize;
                    using (var small = new Icon(icon, size.Width, size.Height))
                    {
                        return small.ToBitmap();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        protected override bool CanShowMenu()
        {
            var files = GetFiles();
            if (files.Length == 0) return false;

            var host = Host.Value;
            if (host == null)
            {
                WriteLog("CanShowMenu: 宿主加载失败，返回 false");
                return false;
            }

            var result = files.Any(f => host.Registry.Supports(Path.GetExtension(f)));
            WriteLog("CanShowMenu: files=" + string.Join(", ", files.Select(Path.GetFileName)) + " result=" + result);
            return result;
        }

        protected override ContextMenuStrip CreateMenu()
        {
            var menu = new ContextMenuStrip();
            var host = Host.Value;
            if (host == null) return menu;

            var files = GetFiles();
            if (files.Length == 0) return menu;

            if (files.Length == 1)
            {
                BuildSingleFileMenu(menu, host, files[0]);
            }
            else
            {
                BuildMultiFileMenu(menu, files);
            }

            return menu;
        }

        private void BuildSingleFileMenu(ContextMenuStrip menu, CoreHost host, string file)
        {
            var ext = Path.GetExtension(file);
            // 同一「输入→输出」方向被多个插件注册时只显示一次（具体用哪个在 App 端由 FormatResolver 决定）
            var conversions = host.Registry.GetConversionsFor(ext)
                .GroupBy(e => e.OutputExtension, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var root = new ToolStripMenuItem("使用 UniversalConvert 转换为");
            root.Image = MenuIcon.Value;
            if (conversions.Count == 0)
            {
                root.Enabled = false;
                menu.Items.Add(root);
                return;
            }

            foreach (var conversion in conversions)
            {
                var targetExt = conversion.OutputExtension;
                var label = $"{conversion.OutputDisplayName}  (.{targetExt})";
                if (conversion.IsUntested) label += "（未经测试）";

                if (conversion.HasCustomizableOptions)
                {
                    // 有预设/参数 → 子菜单：默认（推荐）+ 各预设 + 更多设置
                    var sub = new ToolStripMenuItem(label);
                    sub.Enabled = conversion.IsAvailable;

                    var defaultItem = new ToolStripMenuItem("默认（推荐）");
                    defaultItem.Click += (s, e) => LaunchConvert(file, targetExt, null);
                    sub.DropDownItems.Add(defaultItem);

                    foreach (var preset in conversion.Presets)
                    {
                        var presetName = preset.Name;
                        var presetItem = new ToolStripMenuItem(presetName);
                        presetItem.Click += (s, e) => LaunchConvert(file, targetExt, presetName);
                        sub.DropDownItems.Add(presetItem);
                    }

                    sub.DropDownItems.Add(new ToolStripSeparator());
                    var more = new ToolStripMenuItem("更多设置...");
                    more.Click += (s, e) => LaunchCustomize(file, targetExt);
                    sub.DropDownItems.Add(more);

                    root.DropDownItems.Add(sub);
                }
                else
                {
                    // 无参数 → 直接点击即转
                    var item = new ToolStripMenuItem(label);
                    item.Enabled = conversion.IsAvailable;
                    item.Click += (s, e) => LaunchConvert(file, targetExt, null);
                    root.DropDownItems.Add(item);
                }
            }

            menu.Items.Add(root);

            menu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("使用 UniversalConvert 打开");
            open.Image = MenuIcon.Value;
            open.Click += (s, e) => LaunchOpen(new[] { file });
            menu.Items.Add(open);
        }

        private void BuildMultiFileMenu(ContextMenuStrip menu, IList<string> files)
        {
            var open = new ToolStripMenuItem("使用 UniversalConvert 打开");
            open.Image = MenuIcon.Value;
            open.Click += (s, e) => LaunchOpen(files);
            menu.Items.Add(open);
        }

        private void LaunchConvert(string file, string outputExtension, string presetName)
        {
            var appExe = Path.Combine(BaseDirectory, "UniversalConvert.App.exe");
            if (!File.Exists(appExe)) return;

            var args = CommandLineContract.BuildConvertCommand(file, outputExtension, presetName: presetName);
            Process.Start(appExe, args);
        }

        private void LaunchCustomize(string file, string outputExtension)
        {
            var appExe = Path.Combine(BaseDirectory, "UniversalConvert.App.exe");
            if (!File.Exists(appExe)) return;

            var args = CommandLineContract.BuildCustomizeCommand(file, outputExtension);
            Process.Start(appExe, args);
        }

        private void LaunchOpen(IList<string> files)
        {
            var appExe = Path.Combine(BaseDirectory, "UniversalConvert.App.exe");
            if (!File.Exists(appExe)) return;

            var args = string.Join(" ", files.Select(f => "\"" + f + "\""));
            Process.Start(appExe, args);
        }

        private string[] GetFiles()
        {
            return SelectedItemPaths
                .Where(File.Exists)
                .ToArray();
        }

        private static CoreHost LoadHost()
        {
            WriteLog("LoadHost: 开始加载");
            try
            {
                var config = new ConfigStore().Load();
                if (string.IsNullOrEmpty(config.InstallDirectory))
                {
                    config.InstallDirectory = BaseDirectory;
                }
                var pluginsDir = config.ResolvePluginsDirectory();
                WriteLog("LoadHost: InstallDirectory=" + config.InstallDirectory + ", pluginsDir=" + pluginsDir);

                var host = new CoreHost(config, pluginsDir, WriteLog);
                WriteLog("LoadHost: 成功，插件数=" + host.Plugins.Count + "，转换条目数=" + host.Registry.Entries.Count);
                return host;
            }
            catch (Exception ex)
            {
                WriteLog("LoadHost: 失败 " + ex);
                return null;
            }
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.ConfigDirectory);
                File.AppendAllText(
                    LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响主流程
            }

            Debug.WriteLine("[UniversalConvert.ContextMenu] " + message);
        }
    }
}
