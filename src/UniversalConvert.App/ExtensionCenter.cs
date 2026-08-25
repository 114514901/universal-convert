using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>扩展包体积（字节）；未知为 null。</summary>
        public long? Size { get; set; }
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
                var sizeToken = item["size"];
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
                    DownloadUrl = (string)item["downloadUrl"],
                    Size = sizeToken != null && sizeToken.Type != JTokenType.Null
                        ? (long?)sizeToken.ToObject<long>()
                        : null
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
            Log.Info($"ExtractOrStage 开始: zip={zipPath}, 目标={targetDir}");
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Log.Info($"  删除旧目录（若 DLL 已加载此处会失败）: {targetDir}");
                    Directory.Delete(targetDir, true);
                    Log.Info($"  旧目录删除成功（说明插件当前未被进程锁定）: {targetDir}");
                }
                Directory.CreateDirectory(targetDir);
                PluginPackage.Extract(zipPath, targetDir);
                Log.Info($"直接安装成功（未暂存）: {targetDir}");
                return ExtensionInstallResult.Installed;
            }
            catch (Exception ex)
            {
                // 被占用（IOException，已加载 DLL）或权限问题等都暂存到 pending，重启后应用
                Log.Warn($"目标目录被占用/失败，暂存更新待重启: {targetDir} ({ex.GetType().Name}: {ex.Message})");
                try
                {
                    var pendingDir = Path.Combine(PendingUpdatesDirectory, name);
                    if (Directory.Exists(pendingDir)) Directory.Delete(pendingDir, true);
                    Directory.CreateDirectory(pendingDir);
                    PluginPackage.Extract(zipPath, pendingDir);
                    // 记录暂存进程 PID：重启后新进程先等旧进程完全退出（释放 DLL 句柄）再应用
                    try
                    {
                        var pid = Process.GetCurrentProcess().Id;
                        File.WriteAllText(
                            Path.Combine(PendingUpdatesDirectory, name + ".pid"),
                            pid.ToString());
                        Log.Info($"已写入暂存 PID 文件: {name}.pid = {pid}");
                    }
                    catch (Exception pidEx)
                    {
                        Log.Warn("写入暂存 PID 文件失败: " + pidEx.Message);
                    }
                    Log.Info($"扩展 {name} 更新已暂存（重启后生效）: {pendingDir}");
                    return ExtensionInstallResult.StagedForRestart;
                }
                catch (Exception ex2)
                {
                    Log.Error($"扩展暂存失败（解压到 pending 也失败）: {ex2.GetType().Name}: {ex2.Message}");
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
                    // 第一行记录暂存进程 PID，第二行是要删除的路径；重启后先等旧进程退出再删
                    File.WriteAllText(marker,
                        Process.GetCurrentProcess().Id.ToString() + Environment.NewLine + path);
                    return ExtensionInstallResult.StagedForRestart;
                }
                catch
                {
                    return ExtensionInstallResult.Failed;
                }
            }
        }

        /// <summary>启动时应用暂存的扩展更新（把 pending 下的目录搬进用户插件目录，须在插件加载前调用）。
        /// 先等待暂存时的旧进程完全退出（释放 DLL 句柄）再执行；单个失败不影响其它；返回是否全部应用成功。</summary>
        public static bool ApplyPendingUpdates()
        {
            try
            {
                if (!Directory.Exists(PendingUpdatesDirectory))
                {
                    Log.Info("启动：无暂存扩展更新");
                    return true;
                }

                var pendingDirs = Directory.GetDirectories(PendingUpdatesDirectory);
                Log.Info($"启动：发现 {pendingDirs.Length} 个暂存扩展更新: {string.Join(", ", pendingDirs)}");

                bool allApplied = true;
                foreach (var dir in pendingDirs)
                {
                    var name = Path.GetFileName(dir);
                    var pidFile = Path.Combine(PendingUpdatesDirectory, name + ".pid");
                    string pidText = null;
                    try { if (File.Exists(pidFile)) pidText = File.ReadAllText(pidFile).Trim(); } catch { }
                    Log.Info($"  处理暂存更新 '{name}'，暂存进程 PID: {pidText ?? "(无)"}");
                    WaitForStagingProcessExit(pidText);

                    if (!ApplyOnePendingUpdate(dir, name)) allApplied = false;
                    TryDelete(pidFile);
                }

                if (allApplied)
                {
                    try { Directory.Delete(PendingUpdatesDirectory, true); } catch { }
                    Log.Info("启动：全部暂存扩展更新已应用");
                }
                else
                {
                    Log.Warn("启动：部分暂存扩展更新未应用（文件被占用），将在下次启动重试");
                }
                return allApplied;
            }
            catch (Exception ex)
            {
                Log.Warn("应用扩展更新失败: " + ex.Message);
                return false;
            }
        }

        private static bool ApplyOnePendingUpdate(string pendingDir, string name)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var target = Path.Combine(ConfigStore.UserPluginsDirectory, name);
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, true);
                        Log.Info($"  已删除旧插件目录: {target}");
                    }
                    Directory.CreateDirectory(ConfigStore.UserPluginsDirectory);
                    Directory.Move(pendingDir, target);
                    Log.Info($"已应用扩展更新: {name} -> {target}");
                    return true;
                }
                catch (IOException ex)
                {
                    // 旧进程可能还持有 DLL 句柄，稍等重试
                    Log.Warn($"  应用 '{name}' 第 {attempt + 1} 次尝试被占用: {ex.Message}");
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Log.Warn($"应用扩展更新失败 '{name}': {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
            Log.Warn($"应用扩展更新失败 '{name}'（多次重试后仍被占用）");
            return false;
        }

        /// <summary>启动时删除标记为待卸载的扩展目录（须在插件加载前调用）。
        /// 先等待暂存时的旧进程完全退出再执行；单个失败不影响其它；返回是否全部应用成功。</summary>
        public static bool ApplyPendingUninstalls()
        {
            try
            {
                var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, "pending-uninstall");
                if (!Directory.Exists(markerRoot)) return true;

                bool allApplied = true;
                foreach (var marker in Directory.GetFiles(markerRoot, "*.txt"))
                {
                    string pidText = null;
                    string target = null;
                    try
                    {
                        var lines = File.ReadAllLines(marker);
                        int pid;
                        if (lines.Length >= 2 && int.TryParse(lines[0].Trim(), out pid) && pid > 0)
                        {
                            pidText = lines[0].Trim();
                            target = string.Join(Environment.NewLine, lines, 1, lines.Length - 1);
                        }
                        else
                        {
                            target = string.Join(Environment.NewLine, lines);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("读取卸载标记失败: " + ex.Message);
                        allApplied = false;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(pidText)) WaitForStagingProcessExit(pidText);

                    if (!DeletePendingTarget(target))
                    {
                        allApplied = false;
                        continue;
                    }

                    try { File.Delete(marker); } catch { }
                    Log.Info($"已应用扩展卸载: {target}");
                }

                if (allApplied)
                {
                    try { Directory.Delete(markerRoot, true); } catch { }
                }
                return allApplied;
            }
            catch (Exception ex)
            {
                Log.Warn("应用扩展卸载失败: " + ex.Message);
                return false;
            }
        }

        private static bool DeletePendingTarget(string target)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    else if (File.Exists(target)) File.Delete(target);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Log.Warn("删除待卸载目标失败: " + ex.Message);
                    return false;
                }
            }
            return false;
        }

        /// <summary>等待暂存时的旧进程退出（释放插件 DLL 句柄），最多 10 秒。</summary>
        private static void WaitForStagingProcessExit(string pidText)
        {
            int pid;
            if (string.IsNullOrEmpty(pidText) || !int.TryParse(pidText.Trim(), out pid) || pid <= 0) return;
            if (pid == Process.GetCurrentProcess().Id) return;

            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (!process.WaitForExit(10000))
                    {
                        Log.Warn($"等待旧进程退出超时（PID {pid}），继续尝试应用");
                    }
                    else
                    {
                        Log.Info($"旧进程（PID {pid}）已退出");
                    }
                }
            }
            catch (ArgumentException)
            {
                // 进程已不存在
            }
            catch (Exception ex)
            {
                Log.Warn("等待旧进程退出失败: " + ex.Message);
            }
        }

        /// <summary>是否仍有暂存未应用的扩展更新（供启动后提示）。</summary>
        public static bool HasPendingUpdates()
        {
            try
            {
                return Directory.Exists(PendingUpdatesDirectory)
                    && Directory.GetDirectories(PendingUpdatesDirectory).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>是否仍有暂存未应用的扩展卸载（供启动后提示）。</summary>
        public static bool HasPendingUninstalls()
        {
            try
            {
                var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, "pending-uninstall");
                return Directory.Exists(markerRoot) && Directory.GetFiles(markerRoot, "*.txt").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
