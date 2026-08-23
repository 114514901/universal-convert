using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private static string BaseDirectory
        {
            get
            {
                return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            }
        }

        protected override bool CanShowMenu()
        {
            var host = Host.Value;
            if (host == null) return false;

            var files = GetFiles();
            if (files.Length == 0) return false;

            return files.Any(f => host.Registry.Supports(Path.GetExtension(f)));
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
            var conversions = host.Registry.GetConversionsFor(ext).ToList();

            var root = new ToolStripMenuItem("转换为");
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
            var open = new ToolStripMenuItem("打开 UniversalConvert...");
            open.Click += (s, e) => LaunchOpen(new[] { file });
            menu.Items.Add(open);
        }

        private void BuildMultiFileMenu(ContextMenuStrip menu, IList<string> files)
        {
            var open = new ToolStripMenuItem("用 UniversalConvert 打开");
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
            try
            {
                var config = new ConfigStore().Load();
                if (string.IsNullOrEmpty(config.InstallDirectory))
                {
                    config.InstallDirectory = BaseDirectory;
                }

                return new CoreHost(config, config.ResolvePluginsDirectory(), LogMessage);
            }
            catch (Exception ex)
            {
                LogMessage("ContextMenu host load failed: " + ex.Message);
                return null;
            }
        }

        private static void LogMessage(string message)
        {
            Debug.WriteLine("[UniversalConvert.ContextMenu] " + message);
        }
    }
}
