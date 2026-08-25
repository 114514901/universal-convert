using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>扩展仓库里的一个可用扩展。</summary>
    public sealed class ExtensionInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string MinAppVersion { get; set; }
        public string MaxAppVersion { get; set; }
        public string Homepage { get; set; }
        public string DownloadUrl { get; set; }
    }

    /// <summary>安装/卸载结果：Installed 已直接生效；StagedForRestart 已暂存、重启后生效；Failed 失败。</summary>
    public enum ExtensionInstallResult
    {
        Installed,
        StagedForRestart,
        Failed
    }

    /// <summary>扩展中心：从扩展仓库拉取 index.json，安装/卸载扩展（到用户插件目录）。</summary>
    public static class ExtensionCenter
    {
        private const string IndexUrl =
            "https://raw.githubusercontent.com/114514901/universal-convert-extensions/main/index.json";

        public static async Task<IList<ExtensionInfo>> GetAvailableAsync()
        {
            Log.Info("拉取扩展仓库列表...");
            using (var client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                var json = await client.DownloadStringTaskAsync(IndexUrl).ConfigureAwait(false);
                var list = Parse(json);
                Log.Info($"扩展仓库共 {list.Count} 个可用扩展");
                return list;
            }
        }

        private static IList<ExtensionInfo> Parse(string json)
        {
            var result = new List<ExtensionInfo>();
            var obj = JObject.Parse(json);
            var array = obj["plugins"] as JArray;
            if (array == null) return result;

            foreach (var item in array)
            {
                result.Add(new ExtensionInfo
                {
                    Id = (string)item["id"],
                    Name = (string)item["name"],
                    Description = (string)item["description"],
                    Version = (string)item["version"],
                    Author = (string)item["author"],
                    MinAppVersion = (string)item["minAppVersion"],
                    MaxAppVersion = (string)item["maxAppVersion"],
                    Homepage = (string)item["homepage"],
                    DownloadUrl = (string)item["downloadUrl"]
                });
            }
            return result;
        }

        public static string GetInstallDirectory(ExtensionInfo info)
        {
            return Path.Combine(ConfigStore.UserPluginsDirectory, info.Name);
        }

        public static bool IsInstalled(ExtensionInfo info)
        {
            return File.Exists(Path.Combine(GetInstallDirectory(info), PluginPackage.ManifestFileName));
        }

        public static string GetInstalledVersion(ExtensionInfo info)
        {
            var manifest = PluginPackage.ReadManifest(GetInstallDirectory(info));
            return manifest?.Version;
        }

        /// <summary>待应用更新目录：%AppData%\UniversalConvert\pending。</summary>
        public static string PendingUpdatesDirectory => Path.Combine(ConfigStore.ConfigDirectory, "pending");

        /// <summary>安装扩展；目标目录被锁定（插件已加载、更新场景）时自动暂存到 pending，重启后应用。</summary>
        public static async Task<ExtensionInstallResult> InstallAsync(ExtensionInfo info, IProgress<double> progress, CancellationToken ct)
        {
            var temp = Path.Combine(Path.GetTempPath(), "uc_ext_" + Guid.NewGuid().ToString("N") + ".zip");

            Log.Info($"安装扩展 {info.Id} {info.Version}...");
            try
            {
                await UpdateChecker.DownloadAsync(info.DownloadUrl, temp, progress, ct).ConfigureAwait(false);
                return ExtractOrStage(temp, GetInstallDirectory(info), info.Name);
            }
            catch (Exception ex)
            {
                Log.Error("扩展安装失败: " + ex.Message);
                return ExtensionInstallResult.Failed;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>解压到目标目录；目标被占用/失败时暂存到 pending 目录并返回 StagedForRestart。</summary>
        private static ExtensionInstallResult ExtractOrStage(string zipPath, string targetDir, string name)
        {
            try
            {
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                Directory.CreateDirectory(targetDir);
                PluginPackage.Extract(zipPath, targetDir);
                return ExtensionInstallResult.Installed;
            }
            catch (Exception ex)
            {
                // 被占用（IOException，已加载 DLL）或权限问题等都暂存到 pending，重启后应用
                Log.Warn($"目标目录被占用/失败，暂存更新待重启: {targetDir} ({ex.Message})");
                try
                {
                    var pendingDir = Path.Combine(PendingUpdatesDirectory, name);
                    if (Directory.Exists(pendingDir)) Directory.Delete(pendingDir, true);
                    Directory.CreateDirectory(pendingDir);
                    PluginPackage.Extract(zipPath, pendingDir);
                    Log.Info($"扩展 {name} 更新已暂存（重启后生效）: {pendingDir}");
                    return ExtensionInstallResult.StagedForRestart;
                }
                catch (Exception ex2)
                {
                    Log.Error("扩展暂存失败: " + ex2.Message);
                    return ExtensionInstallResult.Failed;
                }
            }
        }

        /// <summary>卸载扩展；目录被锁定（插件已加载）时记录到 pending-uninstall，重启时删除。</summary>
        public static ExtensionInstallResult Uninstall(ExtensionInfo info)
        {
            var dir = GetInstallDirectory(info);
            Log.Info($"卸载扩展 {info.Id}: {dir}");
            return DeleteOrStage(dir);
        }

        /// <summary>删除目录（或顶层文件）；被占用/失败时记录待重启删除。</summary>
        public static ExtensionInstallResult DeleteOrStage(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
                return ExtensionInstallResult.Installed;
            }
            catch (Exception ex)
            {
                // 被占用（IOException，已加载 DLL 锁定）或权限问题（UnauthorizedAccess）都按「暂存待重启」处理
                Log.Warn($"删除被占用/失败，暂存待重启: {path} ({ex.Message})");
                try
                {
                    var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, "pending-uninstall");
                    Directory.CreateDirectory(markerRoot);
                    var marker = Path.Combine(markerRoot, Guid.NewGuid().ToString("N") + ".txt");
                    File.WriteAllText(marker, path);
                    return ExtensionInstallResult.StagedForRestart;
                }
                catch
                {
                    return ExtensionInstallResult.Failed;
                }
            }
        }

        /// <summary>启动时应用暂存的扩展更新（把 pending 下的目录搬进用户插件目录，须在插件加载前调用）。
        /// 旧进程可能尚未释放 DLL 句柄，失败时短暂重试。</summary>
        public static void ApplyPendingUpdates()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Directory.Exists(PendingUpdatesDirectory)) return;

                    foreach (var dir in Directory.GetDirectories(PendingUpdatesDirectory))
                    {
                        var name = Path.GetFileName(dir);
                        var target = Path.Combine(ConfigStore.UserPluginsDirectory, name);
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        Directory.CreateDirectory(ConfigStore.UserPluginsDirectory);
                        Directory.Move(dir, target);
                        Log.Info($"已应用扩展更新: {name}");
                    }
                    Directory.Delete(PendingUpdatesDirectory, true);
                    return;
                }
                catch (IOException)
                {
                    // 旧进程可能还持有 DLL 句柄，稍等重试
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Log.Warn("应用扩展更新失败: " + ex.Message);
                    return;
                }
            }
        }

        /// <summary>启动时删除标记为待卸载的扩展目录（须在插件加载前调用）。旧进程可能尚未释放句柄，失败时短暂重试。</summary>
        public static void ApplyPendingUninstalls()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, "pending-uninstall");
                    if (!Directory.Exists(markerRoot)) return;

                    foreach (var marker in Directory.GetFiles(markerRoot, "*.txt"))
                    {
                        var target = File.ReadAllText(marker);
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        else if (File.Exists(target)) File.Delete(target);
                        File.Delete(marker);
                        Log.Info($"已应用扩展卸载: {target}");
                    }
                    Directory.Delete(markerRoot, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
                catch
                {
                    return;
                }
            }
        }
    }
}
