using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>扩展仓库里的一个可用扩展（索引记录）。</summary>
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

        /// <summary>扩展包 SHA256（十六进制）；未知为 null（下载时跳过校验）。</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>安装/卸载结果：Installed 已直接生效；StagedForRestart 已暂存、重启后生效；Failed 失败。</summary>
    public enum ExtensionInstallResult
    {
        Installed,
        StagedForRestart,
        Failed
    }

    /// <summary>
    /// 扩展中心：从扩展仓库拉取索引，安装/卸载/更新扩展到用户插件目录。
    /// 安装目录以稳定标识（info.Id）命名；解压、校验、备份与移动均在插件目录之外的临时目录中完成，
    /// 目标目录被已加载插件占用时暂存到 pending 目录，待重启后再应用。
    /// </summary>
    public static class ExtensionCenter
    {
        private const string IndexUrl =
            "https://raw.githubusercontent.com/114514901/universal-convert-extensions/main/index.json";

        /// <summary>安装临时目录名（位于配置目录下、用户插件目录之外，避免被插件加载器扫描为插件）。</summary>
        private const string InstallTempDirectoryName = "install-temp";

        private const string PendingUninstallDirectoryName = "pending-uninstall";

        // 临时目录使用短名称：避免大型扩展（如 LibreOffice）解压/备份路径叠加后超过 MAX_PATH
        private const string StagingDirectoryPrefix = "s-";
        private const string BackupDirectoryPrefix = "b-";
        private const string BackupMarkerSuffix = ".marker";
        private const string PendingDoneSuffix = ".done";
        private const string PendingPidSuffix = ".pid";
        private const int MaxIdLength = 100;

        private static readonly Regex SafeIdPattern = new Regex(
            @"^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Windows 保留设备名（即使带扩展名也不允许用作目录名）。</summary>
        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>安装临时目录：%AppData%\UniversalConvert\install-temp（与插件目录同卷，可原子移动）。</summary>
        private static string InstallTempDirectory
        {
            get { return Path.Combine(ConfigStore.ConfigDirectory, InstallTempDirectoryName); }
        }

        /// <summary>待应用更新目录：%AppData%\UniversalConvert\pending。</summary>
        public static string PendingUpdatesDirectory
        {
            get { return Path.Combine(ConfigStore.ConfigDirectory, "pending"); }
        }

        public static async Task<IList<ExtensionInfo>> GetAvailableAsync(
            CancellationToken ct = default(CancellationToken))
        {
            Log.Info("拉取扩展仓库列表...");
            // raw.githubusercontent 有 CDN 缓存：加时间戳参数强制回源，
            // 避免 index.json 更新后客户端长时间拿到旧列表（下载旧包/版本不符）
            var url = IndexUrl + "?v=" + DateTime.UtcNow.Ticks;
            var json = await UpdateChecker.DownloadStringAsync(url, ct, "application/json").ConfigureAwait(false);
            var list = Parse(json);
            Log.Info("扩展仓库共 " + list.Count + " 个可用扩展");
            return list;
        }

        /// <summary>解析扩展索引：逐条校验必填字段、标识安全字符与 HTTPS 下载地址，非法条目跳过并记录日志。</summary>
        private static IList<ExtensionInfo> Parse(string json)
        {
            var result = new List<ExtensionInfo>();

            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("扩展索引不是合法的 JSON：" + ex.Message, ex);
            }

            var array = obj["plugins"] as JArray;
            if (array == null)
            {
                Log.Warn("扩展索引缺少 plugins 数组，按空列表处理。");
                return result;
            }

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < array.Count; i++)
            {
                try
                {
                    var item = array[i] as JObject;
                    if (item == null)
                    {
                        Log.Warn("扩展索引第 " + (i + 1) + " 条记录不是对象，已跳过。");
                        continue;
                    }

                    var info = new ExtensionInfo
                    {
                        Id = ReadString(item, "id"),
                        Name = ReadString(item, "name"),
                        Description = ReadString(item, "description"),
                        Version = ReadString(item, "version"),
                        Author = ReadString(item, "author"),
                        MinAppVersion = ReadString(item, "minAppVersion"),
                        MaxAppVersion = ReadString(item, "maxAppVersion"),
                        Homepage = ReadString(item, "homepage"),
                        DownloadUrl = ReadString(item, "downloadUrl"),
                        Size = ParseSize(item["size"]),
                        Sha256 = NormalizeSha256(ReadString(item, "sha256"))
                    };

                    string error;
                    if (!ValidateIndexEntry(info, out error))
                    {
                        Log.Warn("扩展索引第 " + (i + 1) + " 条记录无效（标识：“"
                            + (string.IsNullOrEmpty(info.Id) ? "(空)" : info.Id) + "”），已跳过：" + error);
                        continue;
                    }

                    if (!seenIds.Add(info.Id))
                    {
                        Log.Warn("扩展索引存在重复标识，已跳过后续记录：" + info.Id);
                        continue;
                    }

                    result.Add(info);
                }
                catch (Exception ex)
                {
                    Log.Warn("扩展索引第 " + (i + 1) + " 条记录解析失败，已跳过：" + ex.Message);
                }
            }

            return result;
        }

        /// <summary>读取索引字段为字符串；非字符串值按文本处理，缺失时返回 null。</summary>
        private static string ReadString(JObject item, string name)
        {
            var token = item == null ? null : item[name];
            if (token == null || token.Type == JTokenType.Null) return null;

            var value = token as JValue;
            if (value == null) return null;

            var text = value.Type == JTokenType.String ? (string)value : value.ToString();
            return text == null ? null : text.Trim();
        }

        /// <summary>解析体积字段；缺失、非正数或格式非法时按未知（null）处理。</summary>
        private static long? ParseSize(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            try
            {
                var value = token.ToObject<long>();
                return value > 0 ? (long?)value : null;
            }
            catch (Exception ex)
            {
                Log.Warn("扩展索引中的体积字段无效，已按未知处理：" + ex.Message);
                return null;
            }
        }

        /// <summary>校验索引条目；不通过时通过 error 返回正式中文原因。</summary>
        private static bool ValidateIndexEntry(ExtensionInfo info, out string error)
        {
            if (!IsSafeId(info.Id))
            {
                error = "标识缺失或包含不允许的字符（仅允许字母、数字、点、下划线、连字符，且不能以点开头或以点结尾）。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(info.Name))
            {
                error = "缺少显示名称。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(info.Version))
            {
                error = "缺少版本号。";
                return false;
            }

            Uri uri;
            if (string.IsNullOrWhiteSpace(info.DownloadUrl)
                || !Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "下载地址缺失或不是 HTTPS 地址。";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>把 "sha256:xxxx" 或裸十六进制规范化为 64 位小写十六进制；无效返回 null。</summary>
        private static string NormalizeSha256(string digest)
        {
            if (string.IsNullOrEmpty(digest)) return null;
            var d = digest.Trim();
            if (d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) d = d.Substring(7).Trim();
            if (d.Length != 64) return null;
            for (int i = 0; i < d.Length; i++)
            {
                if (!IsHexDigit(d[i])) return null;
            }
            return d.ToLowerInvariant();
        }

        /// <summary>扩展的规范安装目录（以稳定标识命名）；目录可能尚不存在。</summary>
        public static string GetInstallDirectory(ExtensionInfo info)
        {
            return Path.Combine(ConfigStore.UserPluginsDirectory, GetCanonicalDirectoryName(info));
        }

        private static string GetCanonicalDirectoryName(ExtensionInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            return GetCanonicalDirectoryName((info.Id ?? string.Empty).Trim());
        }

        /// <summary>
        /// 规范安装目录名：短标识直接使用；较长标识使用稳定哈希缩短，
        /// 避免与 LibreOffice 等体积大、层级深的扩展叠加后超过 MAX_PATH。
        /// </summary>
        private static string GetCanonicalDirectoryName(string id)
        {
            if (!IsSafeId(id))
            {
                throw new ArgumentException("扩展标识缺失或不合法，无法确定安装目录。", nameof(id));
            }
            if (id.Length <= 24) return id;

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(id.ToLowerInvariant()));
                var text = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                return "uc-" + text.Substring(0, 16);
            }
        }

        /// <summary>
        /// 查找扩展当前的安装目录：优先使用按稳定标识命名的目录；
        /// 其次按 manifest.json 的 Id 识别旧版按显示名安装的目录。未安装时返回 null。
        /// </summary>
        public static string FindExistingDirectory(ExtensionInfo info)
        {
            if (info == null) return null;
            var id = (info.Id ?? string.Empty).Trim();
            if (id.Length == 0) return null;

            try
            {
                if (IsSafeId(id))
                {
                    var canonical = Path.Combine(
                        ConfigStore.UserPluginsDirectory, GetCanonicalDirectoryName(id));
                    if (Directory.Exists(canonical) && PluginPackage.ReadManifest(canonical) != null)
                    {
                        return canonical;
                    }
                }

                foreach (var dir in GetInstallDirectories())
                {
                    var manifest = PluginPackage.ReadManifest(dir);
                    if (manifest != null
                        && string.Equals((manifest.Id ?? string.Empty).Trim(), id, StringComparison.OrdinalIgnoreCase))
                    {
                        return dir;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("查找扩展安装目录失败：" + ex.Message);
            }
            return null;
        }

        public static bool IsInstalled(ExtensionInfo info)
        {
            var dir = FindExistingDirectory(info);
            return dir != null && PluginPackage.ReadManifest(dir) != null;
        }

        public static string GetInstalledVersion(ExtensionInfo info)
        {
            var dir = FindExistingDirectory(info);
            return dir == null ? null : PluginPackage.ReadManifest(dir)?.Version;
        }

        /// <summary>
        /// 迁移旧版按显示名安装的扩展目录：读取目录内 manifest.json 的 Id，
        /// 将其重命名为按稳定标识命名的规范目录（须在插件加载前调用）。
        /// 迁移失败时保留原目录，不影响“已安装”判定；返回是否全部迁移成功。
        /// </summary>
        public static bool MigrateLegacyInstalls()
        {
            try
            {
                CleanupInstallTempDirectory();

                var root = ConfigStore.UserPluginsDirectory;
                if (!Directory.Exists(root))
                {
                    Log.Info("用户插件目录不存在，无需迁移扩展目录。");
                    return true;
                }

                bool allMigrated = true;
                foreach (var dir in GetInstallDirectories())
                {
                    var name = Path.GetFileName(dir);
                    var manifest = PluginPackage.ReadManifest(dir);
                    if (manifest == null)
                    {
                        Log.Debug("跳过不含清单的用户插件目录：" + dir);
                        continue;
                    }

                    var id = (manifest.Id ?? string.Empty).Trim();
                    if (!IsSafeId(id))
                    {
                        Log.Warn("扩展目录“" + name + "”的清单标识不合法，未迁移："
                            + (id.Length == 0 ? "(空)" : id));
                        allMigrated = false;
                        continue;
                    }

                    var canonicalName = GetCanonicalDirectoryName(id);
                    if (string.Equals(name, canonicalName, StringComparison.Ordinal)) continue;

                    var target = Path.Combine(root, canonicalName);
                    if (string.Equals(name, canonicalName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(dir, target, StringComparison.OrdinalIgnoreCase))
                    {
                        // 仅目录名大小写不同：在 Windows 上指向同一目录，无需迁移
                        continue;
                    }

                    if (Directory.Exists(target))
                    {
                        var targetManifest = PluginPackage.ReadManifest(target);
                        if (targetManifest != null
                            && !string.Equals((targetManifest.Id ?? string.Empty).Trim(), id, StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Warn("规范目录“" + target + "”已属于其它扩展，未迁移目录“" + name + "”。");
                            allMigrated = false;
                            continue;
                        }

                        if (targetManifest != null)
                        {
                            // 规范目录已存在且有效：清理旧版目录（被占用时登记待重启删除）
                            if (!TryRemoveDuplicateDirectory(dir)) allMigrated = false;
                            continue;
                        }

                        if (!TryDeleteInstalledDirectory(target))
                        {
                            Log.Warn("不完整的规范目录无法清理，本次不迁移：“" + target + "”。");
                            allMigrated = false;
                            continue;
                        }
                    }

                    try
                    {
                        Directory.Move(dir, target);
                        Log.Info("已迁移扩展目录：“" + name + "” -> “" + id + "”。");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("迁移扩展目录失败，保留原目录：" + dir + " -> " + target
                            + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                        allMigrated = false;
                    }
                }

                if (allMigrated) Log.Info("扩展目录迁移完成：无遗留的旧版目录。");
                return allMigrated;
            }
            catch (Exception ex)
            {
                Log.Warn("迁移扩展目录失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 安装扩展；目标目录被锁定（插件已加载、更新场景）时暂存到 pending 目录，重启后应用。
        /// pause：共享暂停信号（置位期间挂起下载，恢复后自动续传）。
        /// 失败原因（含下载校验、解压、暂存）通过 onError 回传。
        /// </summary>
        public static async Task<ExtensionInstallResult> InstallAsync(ExtensionInfo info, IProgress<double> progress, CancellationToken ct, ManualResetEventSlim pause = null, SemaphoreSlim sharedBudget = null, IProgress<Tuple<long, long>> byteProgress = null, Action<string> onError = null)
        {
            if (info == null)
            {
                ReportError(onError, "扩展信息为空，无法安装。");
                return ExtensionInstallResult.Failed;
            }

            string directoryName;
            var validationError = ValidateInstallRequest(info, out directoryName);
            if (validationError != null)
            {
                Log.Error("扩展安装请求无效（" + (info.Id ?? "(无标识)") + "）：" + validationError);
                ReportError(onError, validationError);
                return ExtensionInstallResult.Failed;
            }

            var temp = Path.Combine(Path.GetTempPath(), "uc_ext_" + Guid.NewGuid().ToString("N") + ".zip");

            Log.Info("安装扩展 " + info.Id + " " + info.Version + "...");
            try
            {
                await UpdateChecker.DownloadAsync(
                    info.DownloadUrl, temp, progress, ct, info.Sha256, pause, sharedBudget, byteProgress)
                    .ConfigureAwait(false);

                var packageError = VerifyDownloadedPackage(temp, info);
                if (packageError != null)
                {
                    Log.Error("扩展安装包校验失败（" + info.Id + "）：" + packageError);
                    ReportError(onError, packageError);
                    return ExtensionInstallResult.Failed;
                }

                ct.ThrowIfCancellationRequested();
                return ExtractOrStage(temp, directoryName, (info.Id ?? string.Empty).Trim(), onError);
            }
            catch (OperationCanceledException)
            {
                Log.Info("扩展安装已取消：" + info.Id);
                return ExtensionInstallResult.Failed;
            }
            catch (Exception ex)
            {
                Log.Error("扩展下载或校验失败（" + info.Id + "）：" + ex.GetType().Name + "：" + ex.Message);
                ReportError(onError, "扩展下载或校验失败：" + ex.Message);
                return ExtensionInstallResult.Failed;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>校验安装请求；不通过时返回正式中文原因，通过时输出规范目录名。</summary>
        private static string ValidateInstallRequest(ExtensionInfo info, out string directoryName)
        {
            directoryName = null;

            var id = (info.Id ?? string.Empty).Trim();
            if (!IsSafeId(id))
            {
                return "扩展标识缺失或不合法（仅允许字母、数字、点、下划线、连字符），无法确定安装目录。";
            }
            directoryName = GetCanonicalDirectoryName(id);

            Uri uri;
            if (string.IsNullOrWhiteSpace(info.DownloadUrl)
                || !Uri.TryCreate(info.DownloadUrl.Trim(), UriKind.Absolute, out uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return "扩展下载地址缺失或不是 HTTPS 地址，已停止安装。";
            }

            bool hasSize = info.Size.HasValue && info.Size.Value > 0;
            bool hasSha = !string.IsNullOrEmpty(info.Sha256);
            if (!hasSize && !hasSha)
            {
                return "扩展索引缺少 SHA256 与体积信息，无法校验安装包完整性，已停止安装。";
            }

            return null;
        }

        /// <summary>按索引记录校验下载结果（文件存在、非空、体积一致）；通过时返回 null。</summary>
        private static string VerifyDownloadedPackage(string path, ExtensionInfo info)
        {
            FileInfo file;
            try
            {
                file = new FileInfo(path);
            }
            catch (Exception ex)
            {
                return "无法读取已下载的扩展包：" + ex.Message;
            }

            if (!file.Exists) return "已下载的扩展包不存在。";
            if (file.Length <= 0) return "已下载的扩展包为空文件。";
            if (info.Size.HasValue && info.Size.Value > 0 && file.Length != info.Size.Value)
            {
                return string.Format(
                    "已下载的扩展包体积与索引记录不一致（期望 {0} 字节，实际 {1} 字节）。",
                    info.Size.Value, file.Length);
            }
            return null;
        }

        /// <summary>
        /// 解压到临时目录并校验，随后原子地替换目标目录：
        /// 目标被占用时暂存到 pending；否则备份旧目录后移动解压结果，移动失败则回滚。
        /// 所有临时目录在失败时清理，错误原因通过 onError 回传。
        /// </summary>
        private static ExtensionInstallResult ExtractOrStage(string zipPath, string directoryName, string extensionId, Action<string> onError)
        {
            var pluginsRoot = ConfigStore.UserPluginsDirectory;
            var target = Path.Combine(pluginsRoot, directoryName);
            var tempRoot = InstallTempDirectory;
            var staging = Path.Combine(
                tempRoot, StagingDirectoryPrefix + Guid.NewGuid().ToString("N").Substring(0, 12));
            string backup = null;
            bool stagingExists = false;
            bool packageValidated = false;

            try
            {
                Log.Info("解压扩展包：" + zipPath + " -> " + staging);
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(staging);
                stagingExists = true;

                PluginPackage.Extract(zipPath, staging);
                PluginPackage.ValidateExtractedDirectory(staging);

                var manifest = PluginPackage.ReadManifest(staging);
                if (manifest == null
                    || !string.Equals((manifest.Id ?? string.Empty).Trim(), extensionId, StringComparison.OrdinalIgnoreCase))
                {
                    var message = string.Format(
                        "扩展包清单标识与索引记录不一致（包内“{0}”，索引“{1}”），已停止安装。",
                        manifest == null ? "(缺失)" : (manifest.Id ?? "(缺失)"), extensionId);
                    Log.Error(message);
                    ReportError(onError, message);
                    return ExtensionInstallResult.Failed;
                }
                packageValidated = true;

                // 目标目录被已加载的插件占用时不做就地替换：
                // 直接暂存，重启后在插件加载前应用，避免运行中的旧程序读到新版本文件。
                if (Directory.Exists(target) && ContainsLockedBinary(target))
                {
                    Log.Info("目标目录正被占用，改为暂存更新（重启后生效）：" + target);
                    return TryStagePreparedDirectory(staging, directoryName, extensionId, onError);
                }

                if (Directory.Exists(target))
                {
                    var backupPath = Path.Combine(
                        tempRoot, BackupDirectoryPrefix + Guid.NewGuid().ToString("N").Substring(0, 12));
                    // 先写入恢复标记，再移动旧目录：若进程在两步之间中断，下次启动据此回滚
                    File.WriteAllText(backupPath + BackupMarkerSuffix,
                        "target=" + directoryName + Environment.NewLine);
                    try
                    {
                        Directory.Move(target, backupPath);
                        backup = backupPath;
                        Log.Info("已备份旧插件目录：" + target);
                    }
                    catch (Exception ex)
                    {
                        if (Directory.Exists(backupPath))
                        {
                            backup = backupPath;
                            Log.Warn("备份旧插件目录出现异常，但备份目录已存在，继续安装：" + ex.Message);
                        }
                        else
                        {
                            TryDelete(backupPath + BackupMarkerSuffix);
                            Log.Warn("备份旧插件目录失败，改为暂存更新（重启后生效）："
                                + target + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                            return TryStagePreparedDirectory(staging, directoryName, extensionId, onError);
                        }
                    }
                }

                Directory.Move(staging, target);
                stagingExists = false;
                TryDelete(backup == null ? null : backup + BackupMarkerSuffix);
                Log.Info("直接安装成功（未暂存）：" + target);
                if (backup != null) TryDeleteInstalledDirectory(backup);
                try
                {
                    RemoveDuplicateInstalls(extensionId, target);
                }
                catch (Exception ex)
                {
                    Log.Warn("清理重复的扩展目录失败：" + ex.Message);
                }
                return ExtensionInstallResult.Installed;
            }
            catch (Exception ex)
            {
                Log.Error("扩展解压或安装失败：" + ex.GetType().Name + "：" + ex.Message);
                ReportError(onError, "扩展解压或安装失败：" + ex.Message);
                RestoreBackupIfNeeded(backup, target, extensionId);

                if (stagingExists && packageValidated)
                {
                    var staged = TryStagePreparedDirectory(staging, directoryName, extensionId, onError);
                    if (staged == ExtensionInstallResult.StagedForRestart) return staged;
                }
                return ExtensionInstallResult.Failed;
            }
            finally
            {
                if (stagingExists) TryDeleteDir(staging);
            }
        }

        /// <summary>
        /// 把已解压且校验通过的临时目录移入 pending，并在移动完成后写入完成标记与暂存进程 PID。
        /// 只有完成标记齐备的暂存目录才会在下次启动被应用。
        /// </summary>
        private static ExtensionInstallResult TryStagePreparedDirectory(string preparedDir, string directoryName, string extensionId, Action<string> onError)
        {
            var pendingRoot = PendingUpdatesDirectory;
            var pendingDir = Path.Combine(pendingRoot, directoryName);
            var doneFile = GetPendingMarkerPath(directoryName, PendingDoneSuffix);
            var pidFile = GetPendingMarkerPath(directoryName, PendingPidSuffix);

            try
            {
                Directory.CreateDirectory(pendingRoot);
                if (Directory.Exists(pendingDir)) Directory.Delete(pendingDir, true);
                TryDelete(doneFile);
                TryDelete(pidFile);

                Directory.Move(preparedDir, pendingDir);

                // 目录就位后才写入完成标记：避免下次启动把未就绪的目录当作完整包应用
                File.WriteAllText(doneFile,
                    "id=" + extensionId + Environment.NewLine
                    + "stagedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
                try
                {
                    File.WriteAllText(pidFile, Process.GetCurrentProcess().Id.ToString());
                    Log.Info("已写入暂存进程 PID 文件：" + Path.GetFileName(pidFile));
                }
                catch (Exception ex)
                {
                    Log.Warn("写入暂存进程 PID 文件失败（不影响暂存应用）：" + ex.Message);
                }

                Log.Info("扩展 " + directoryName + " 更新已暂存（重启后生效）：" + pendingDir);
                return ExtensionInstallResult.StagedForRestart;
            }
            catch (Exception ex)
            {
                Log.Error("扩展暂存失败：" + ex.GetType().Name + "：" + ex.Message);
                ReportError(onError, "扩展暂存失败：" + ex.Message);

                // 清理不完整的暂存内容，避免下次启动误用
                TryDelete(doneFile);
                TryDelete(pidFile);
                TryDeleteDir(pendingDir);
                return ExtensionInstallResult.Failed;
            }
        }

        /// <summary>卸载扩展；目录被锁定（插件已加载）时记录到 pending-uninstall，重启时删除。</summary>
        public static ExtensionInstallResult Uninstall(ExtensionInfo info)
        {
            if (info == null)
            {
                Log.Error("卸载扩展失败：扩展信息为空。");
                return ExtensionInstallResult.Failed;
            }

            var dirs = FindDirectoriesForRemoval(info);
            Log.Info("卸载扩展 " + (info.Id ?? "(无标识)") + "：待删除目录 " + dirs.Count + " 个");
            if (dirs.Count == 0) return ExtensionInstallResult.Installed;

            bool staged = false;
            foreach (var dir in dirs)
            {
                var result = DeleteOrStage(dir);
                if (result == ExtensionInstallResult.Failed)
                {
                    Log.Error("卸载扩展失败：" + dir);
                    return ExtensionInstallResult.Failed;
                }
                if (result == ExtensionInstallResult.StagedForRestart) staged = true;
            }

            return staged ? ExtensionInstallResult.StagedForRestart : ExtensionInstallResult.Installed;
        }

        /// <summary>删除目录（或顶层文件）；被占用/失败时记录待重启删除。</summary>
        public static ExtensionInstallResult DeleteOrStage(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Log.Warn("删除路径为空，已忽略。");
                return ExtensionInstallResult.Failed;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    if (TryDeleteInstalledDirectory(path)) return ExtensionInstallResult.Installed;
                    return StageUninstall(path)
                        ? ExtensionInstallResult.StagedForRestart
                        : ExtensionInstallResult.Failed;
                }

                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                        return ExtensionInstallResult.Installed;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("删除文件被占用或失败，登记为待重启删除：" + path
                            + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                        return StageUninstall(path)
                            ? ExtensionInstallResult.StagedForRestart
                            : ExtensionInstallResult.Failed;
                    }
                }

                return ExtensionInstallResult.Installed;
            }
            catch (Exception ex)
            {
                Log.Warn("删除失败，登记为待重启删除：" + path + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                return StageUninstall(path)
                    ? ExtensionInstallResult.StagedForRestart
                    : ExtensionInstallResult.Failed;
            }
        }

        /// <summary>
        /// 启动时应用暂存的扩展更新（须在插件加载前调用）。
        /// 仅应用带完成标记且清单可读的暂存目录；先等待暂存时的旧进程完全退出再执行；
        /// 单个失败不影响其它；返回是否全部应用成功。
        /// </summary>
        public static bool ApplyPendingUpdates()
        {
            try
            {
                CleanupInstallTempDirectory();

                if (!Directory.Exists(PendingUpdatesDirectory))
                {
                    Log.Info("启动：无暂存扩展更新");
                    return true;
                }

                var pendingDirs = Directory.GetDirectories(PendingUpdatesDirectory);
                Log.Info("启动：发现 " + pendingDirs.Length + " 个暂存扩展更新："
                    + string.Join(", ", pendingDirs));

                bool allApplied = true;
                foreach (var dir in pendingDirs)
                {
                    var name = Path.GetFileName(dir);
                    var doneFile = GetPendingMarkerPath(name, PendingDoneSuffix);
                    var pidFile = GetPendingMarkerPath(name, PendingPidSuffix);

                    string pidText = null;
                    try { if (File.Exists(pidFile)) pidText = File.ReadAllText(pidFile).Trim(); } catch { }

                    if (!IsPendingComplete(dir, name))
                    {
                        Log.Warn("暂存扩展“" + name + "”未写入完成标记或清单不可读，未应用并清理：" + dir);
                        TryDelete(doneFile);
                        TryDelete(pidFile);
                        TryDeleteDir(dir);
                        allApplied = false;
                        continue;
                    }

                    Log.Info("  处理暂存更新“" + name + "”，暂存进程 PID：" + (pidText ?? "(无)"));
                    WaitForStagingProcessExit(pidText);

                    if (ApplyOnePendingUpdate(dir, name))
                    {
                        TryDelete(doneFile);
                        TryDelete(pidFile);
                    }
                    else
                    {
                        allApplied = false;
                    }
                }

                TryDeleteOrphanPendingMarkers();

                if (!HasPendingUpdateDirectories())
                {
                    try { Directory.Delete(PendingUpdatesDirectory, true); } catch { }
                    Log.Info(allApplied
                        ? "启动：全部暂存扩展更新已应用"
                        : "启动：暂存目录已清理（不完整或无效的暂存内容已丢弃）");
                }
                else
                {
                    Log.Warn("启动：部分暂存扩展更新未应用，将在下次启动重试。");
                }
                return allApplied;
            }
            catch (Exception ex)
            {
                Log.Warn("应用扩展更新失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>应用单个暂存目录：备份旧目录后原子移动，失败时回滚，保留暂存内容供下次重试。</summary>
        private static bool ApplyOnePendingUpdate(string pendingDir, string name)
        {
            var manifest = PluginPackage.ReadManifest(pendingDir);
            if (manifest == null)
            {
                Log.Error("暂存扩展“" + name + "”缺少可读取的 manifest.json，未应用并清理：" + pendingDir);
                TryDeleteDir(pendingDir);
                return false;
            }

            var id = (manifest.Id ?? string.Empty).Trim();
            string directoryName;
            if (IsSafeId(id))
            {
                directoryName = GetCanonicalDirectoryName(id);
            }
            else
            {
                Log.Warn("暂存扩展“" + name + "”的清单标识不合法（" + (id.Length == 0 ? "(空)" : id)
                    + "），沿用暂存目录名作为安装目录。");
                directoryName = name;
            }

            var pluginsRoot = ConfigStore.UserPluginsDirectory;
            var target = Path.Combine(pluginsRoot, directoryName);

            if (Directory.Exists(target) && ContainsLockedBinary(target))
            {
                Log.Warn("目标目录仍被占用，本次不应用暂存更新：" + target);
                return false;
            }

            var tempRoot = InstallTempDirectory;
            string backup = null;
            try
            {
                Directory.CreateDirectory(pluginsRoot);
                Directory.CreateDirectory(tempRoot);

                if (Directory.Exists(target))
                {
                    var backupPath = Path.Combine(
                        tempRoot, BackupDirectoryPrefix + Guid.NewGuid().ToString("N").Substring(0, 12));
                    File.WriteAllText(backupPath + BackupMarkerSuffix,
                        "target=" + directoryName + Environment.NewLine);
                    try
                    {
                        Directory.Move(target, backupPath);
                        backup = backupPath;
                        Log.Info("  已备份旧插件目录：" + target);
                    }
                    catch (Exception ex)
                    {
                        if (Directory.Exists(backupPath))
                        {
                            backup = backupPath;
                            Log.Warn("备份旧插件目录出现异常，但备份目录已存在，继续应用：" + ex.Message);
                        }
                        else
                        {
                            TryDelete(backupPath + BackupMarkerSuffix);
                            Log.Warn("备份旧插件目录失败，保留暂存更新待下次启动重试：" + target
                                + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                            return false;
                        }
                    }
                }

                MoveDirectoryWithRetry(pendingDir, target);
            }
            catch (Exception ex)
            {
                Log.Warn("应用暂存扩展更新失败“" + name + "”：" + ex.GetType().Name + "：" + ex.Message);
                RestoreBackupIfNeeded(backup, target, directoryName);
                return false;
            }

            TryDelete(backup == null ? null : backup + BackupMarkerSuffix);
            Log.Info("已应用扩展更新：" + name + " -> " + target);
            if (backup != null) TryDeleteInstalledDirectory(backup);
            try
            {
                RemoveDuplicateInstalls(directoryName, target);
            }
            catch (Exception ex)
            {
                Log.Warn("清理重复的扩展目录失败：" + ex.Message);
            }
            return true;
        }

        /// <summary>
        /// 启动时删除标记为待卸载的扩展目录（须在插件加载前调用）。
        /// 先等待暂存时的旧进程完全退出再执行；单个失败不影响其它；返回是否全部应用成功。
        /// </summary>
        public static bool ApplyPendingUninstalls()
        {
            try
            {
                var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, PendingUninstallDirectoryName);
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
                        Log.Warn("读取卸载标记失败：" + ex.Message);
                        allApplied = false;
                        continue;
                    }

                    target = (target ?? string.Empty).Trim();
                    if (!IsRemovablePluginPath(target))
                    {
                        Log.Error("卸载标记指向用户插件目录之外的路径，已忽略并删除标记：" + target);
                        TryDelete(marker);
                        allApplied = false;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(pidText)) WaitForStagingProcessExit(pidText);

                    if (!DeletePendingTarget(target))
                    {
                        allApplied = false;
                        continue;
                    }

                    TryDelete(marker);
                    Log.Info("已应用扩展卸载：" + target);
                }

                if (allApplied)
                {
                    try { Directory.Delete(markerRoot, true); } catch { }
                }
                return allApplied;
            }
            catch (Exception ex)
            {
                Log.Warn("应用扩展卸载失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>删除待卸载目标（目录或文件）；被占用时保留标记，下次启动重试。</summary>
        private static bool DeletePendingTarget(string target)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(target))
                    {
                        if (TryDeleteInstalledDirectory(target)) return true;
                        Thread.Sleep(500);
                        continue;
                    }

                    if (File.Exists(target))
                    {
                        File.Delete(target);
                        return true;
                    }

                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Log.Warn("删除待卸载目标失败：" + ex.Message);
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
                        Log.Warn("等待旧进程退出超时（PID " + pid + "），继续尝试应用。");
                    }
                    else
                    {
                        Log.Info("旧进程（PID " + pid + "）已退出。");
                    }
                }
            }
            catch (ArgumentException)
            {
                // 进程已不存在
            }
            catch (Exception ex)
            {
                Log.Warn("等待旧进程退出失败：" + ex.Message);
            }
        }

        /// <summary>是否仍有暂存未应用的扩展更新（供启动后提示）。</summary>
        public static bool HasPendingUpdates()
        {
            return HasPendingUpdateDirectories();
        }

        /// <summary>是否仍有暂存未应用的扩展卸载（供启动后提示）。</summary>
        public static bool HasPendingUninstalls()
        {
            try
            {
                var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, PendingUninstallDirectoryName);
                return Directory.Exists(markerRoot) && Directory.GetFiles(markerRoot, "*.txt").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasPendingUpdateDirectories()
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

        /// <summary>枚举用户插件目录下的一级子目录（不包含临时目录，临时目录位于配置目录下）。</summary>
        private static IList<string> GetInstallDirectories()
        {
            var result = new List<string>();
            try
            {
                var root = ConfigStore.UserPluginsDirectory;
                if (!Directory.Exists(root)) return result;
                result.AddRange(Directory.GetDirectories(root));
            }
            catch (Exception ex)
            {
                Log.Debug("枚举用户插件目录失败：" + ex.Message);
            }
            return result;
        }

        /// <summary>查找卸载时需要删除的目录：规范目录（若存在）与所有清单标识匹配的旧版目录。</summary>
        private static IList<string> FindDirectoriesForRemoval(ExtensionInfo info)
        {
            var result = new List<string>();
            var id = (info.Id ?? string.Empty).Trim();
            if (id.Length == 0) return result;

            if (IsSafeId(id))
            {
                var canonical = Path.Combine(ConfigStore.UserPluginsDirectory, id);
                if (Directory.Exists(canonical)) result.Add(canonical);
            }

            foreach (var dir in GetInstallDirectories())
            {
                if (ContainsPath(result, dir)) continue;
                var manifest = PluginPackage.ReadManifest(dir);
                if (manifest == null) continue;
                if (string.Equals((manifest.Id ?? string.Empty).Trim(), id, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(dir);
                }
            }
            return result;
        }

        /// <summary>清理同一扩展的重复安装目录（保留 keepDirectory），被占用时登记待重启删除。</summary>
        private static void RemoveDuplicateInstalls(string extensionId, string keepDirectory)
        {
            if (!IsSafeId(extensionId)) return;

            foreach (var dir in GetInstallDirectories())
            {
                if (string.Equals(dir, keepDirectory, StringComparison.OrdinalIgnoreCase)) continue;
                var manifest = PluginPackage.ReadManifest(dir);
                if (manifest == null) continue;
                if (!string.Equals((manifest.Id ?? string.Empty).Trim(), extensionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryRemoveDuplicateDirectory(dir);
            }
        }

        /// <summary>删除重复目录；被占用时登记待重启删除。返回是否已删除或已登记。</summary>
        private static bool TryRemoveDuplicateDirectory(string directory)
        {
            if (TryDeleteInstalledDirectory(directory)) return true;
            return StageUninstall(directory);
        }

        /// <summary>记录待重启删除标记（首行为暂存进程 PID，第二行为待删除路径）。</summary>
        private static bool StageUninstall(string path)
        {
            try
            {
                var markerRoot = Path.Combine(ConfigStore.ConfigDirectory, PendingUninstallDirectoryName);
                Directory.CreateDirectory(markerRoot);
                var marker = Path.Combine(markerRoot, Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(marker,
                    Process.GetCurrentProcess().Id.ToString() + Environment.NewLine + path);
                Log.Info("已登记待重启删除：" + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("登记待重启删除失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>回滚备份目录：目标缺失时把备份移回；目标已存在时仅清理恢复标记。</summary>
        private static void RestoreBackupIfNeeded(string backup, string target, string name)
        {
            if (string.IsNullOrEmpty(backup) || !Directory.Exists(backup)) return;

            if (Directory.Exists(target))
            {
                TryDelete(backup + BackupMarkerSuffix);
                return;
            }

            try
            {
                Directory.Move(backup, target);
                TryDelete(backup + BackupMarkerSuffix);
                Log.Info("已回滚到更新前的扩展目录：" + target);
            }
            catch (Exception ex)
            {
                Log.Error("回滚扩展“" + name + "”失败，已保留备份目录以便下次启动恢复："
                    + backup + "（" + ex.Message + "）");
            }
        }

        /// <summary>带重试地移动目录（应对杀毒软件等短暂占用）。</summary>
        private static void MoveDirectoryWithRetry(string source, string destination)
        {
            const int maxAttempts = 3;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (IOException ex)
                {
                    if (attempt >= maxAttempts - 1) throw;
                    Log.Warn("移动目录失败（第 " + (attempt + 1) + " 次），稍后重试：" + ex.Message);
                    Thread.Sleep(500);
                }
            }
        }

        /// <summary>
        /// 清理插件目录中的已安装目录：目录内二进制被占用时不做部分删除，保留至下次启动。
        /// 返回是否已删除（目录本就不存在时视为成功）。
        /// </summary>
        private static bool TryDeleteInstalledDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return true;
            if (!Directory.Exists(directory)) return true;

            if (ContainsLockedBinary(directory))
            {
                Log.Info("目录内仍有被占用的二进制文件，保留至下次启动清理：" + directory);
                return false;
            }

            try
            {
                Directory.Delete(directory, true);
                Log.Info("已删除目录：" + directory);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除目录失败，保留至下次启动重试：" + directory
                    + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                return false;
            }
        }

        /// <summary>判断目录（含子目录）中是否有被其它句柄占用的 DLL 或 EXE。</summary>
        private static bool ContainsLockedBinary(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return false;
                if (ContainsLockedFile(directory, "*.dll")) return true;
                if (ContainsLockedFile(directory, "*.exe")) return true;
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static bool ContainsLockedFile(string directory, string pattern)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                // 无法枚举时按占用处理，避免删除不完整内容
                return true;
            }

            foreach (var file in files)
            {
                try
                {
                    using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                    }
                }
                catch (IOException)
                {
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 清理安装临时区：恢复中断的安装（有恢复标记且目标缺失时回滚），
        /// 删除不再需要的备份、未完成的解压目录与孤立标记。
        /// </summary>
        private static void CleanupInstallTempDirectory()
        {
            try
            {
                var root = InstallTempDirectory;
                if (!Directory.Exists(root)) return;

                string[] dirs;
                try
                {
                    dirs = Directory.GetDirectories(root);
                }
                catch (Exception ex)
                {
                    Log.Warn("枚举扩展安装临时目录失败：" + ex.Message);
                    return;
                }

                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(BackupDirectoryPrefix, StringComparison.Ordinal))
                    {
                        RecoverOrRemoveBackup(dir, name);
                    }
                    else if (name.StartsWith(StagingDirectoryPrefix, StringComparison.Ordinal))
                    {
                        Log.Info("清理未完成的扩展解压临时目录：" + dir);
                        TryDeleteDir(dir);
                    }
                }

                try
                {
                    foreach (var file in Directory.GetFiles(root, "*" + BackupMarkerSuffix))
                    {
                        var backupDir = file.Substring(0, file.Length - BackupMarkerSuffix.Length);
                        if (!Directory.Exists(backupDir)) TryDelete(file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("清理扩展恢复标记失败：" + ex.Message);
                }

                try
                {
                    if (Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0)
                    {
                        Directory.Delete(root);
                    }
                }
                catch
                {
                    // 临时目录残留不影响主流程
                }
            }
            catch (Exception ex)
            {
                Log.Warn("清理扩展安装临时目录失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 处理单个备份目录：目标缺失且存在恢复标记时恢复旧版本；
        /// 其余情况（目标已存在、无恢复标记、名称无法识别）按临时目录清理。
        /// </summary>
        private static void RecoverOrRemoveBackup(string backupDir, string name)
        {
            var marker = backupDir + BackupMarkerSuffix;
            var targetName = ReadBackupTarget(marker);
            if (string.IsNullOrEmpty(targetName))
            {
                // 兼容旧版备份目录名（b-{标识}-{GUID}）；新格式从 .marker 读取 target=
                targetName = ParseBackupDirectoryName(name);
            }
            if (string.IsNullOrEmpty(targetName) || !IsSafeId(targetName))
            {
                Log.Warn("无法识别扩展备份目录的目标名称，按临时目录清理：" + backupDir);
                TryDeleteInstalledDirectory(backupDir);
                TryDelete(marker);
                return;
            }

            var target = Path.Combine(ConfigStore.UserPluginsDirectory, targetName);
            if (!Directory.Exists(target))
            {
                if (!File.Exists(marker))
                {
                    // 没有恢复标记说明安装已完成、目标目录随后被删除（例如用户卸载），不应恢复旧版本
                    Log.Info("备份目录没有恢复标记，按临时目录清理：" + backupDir);
                    TryDeleteInstalledDirectory(backupDir);
                    return;
                }

                try
                {
                    Directory.CreateDirectory(ConfigStore.UserPluginsDirectory);
                    Directory.Move(backupDir, target);
                    TryDelete(marker);
                    Log.Info("已恢复安装中断前的扩展目录：" + target);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn("恢复扩展备份失败，保留至下次启动重试：" + backupDir
                        + "（" + ex.Message + "）");
                    return;
                }
            }

            // 目标目录已存在：备份不再需要
            TryDeleteInstalledDirectory(backupDir);
            TryDelete(marker);
        }

        /// <summary>读取备份恢复标记中的 target=（规范安装目录名）；不存在或格式不符时返回 null。</summary>
        private static string ReadBackupTarget(string marker)
        {
            try
            {
                if (!File.Exists(marker)) return null;
                foreach (var line in File.ReadAllLines(marker))
                {
                    var text = line == null ? string.Empty : line.Trim();
                    if (text.StartsWith("target=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = text.Substring("target=".Length).Trim();
                        return value.Length == 0 ? null : value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("读取扩展备份标记失败：" + ex.Message);
            }
            return null;
        }

        /// <summary>兼容旧版备份目录名（backup-{标识}-{32 位 GUID}）解析扩展标识；无法识别时返回 null。</summary>
        private static string ParseBackupDirectoryName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith(BackupDirectoryPrefix, StringComparison.Ordinal))
                return null;

            var rest = name.Substring(BackupDirectoryPrefix.Length);
            var dash = rest.LastIndexOf('-');
            if (dash <= 0) return null;

            var id = rest.Substring(0, dash);
            var guidPart = rest.Substring(dash + 1);
            if (guidPart.Length != 32) return null;
            for (int i = 0; i < guidPart.Length; i++)
            {
                if (!IsHexDigit(guidPart[i])) return null;
            }
            return IsSafeId(id) ? id : null;
        }

        /// <summary>判断暂存目录是否已完成：新格式看 .done 标记，旧格式看解压成功后写入的 .pid 标记。</summary>
        private static bool IsPendingComplete(string pendingDir, string name)
        {
            if (File.Exists(GetPendingMarkerPath(name, PendingDoneSuffix))) return true;

            // 兼容旧版本：旧流程仅在解压成功后写入 PID 文件，可视为旧格式的完成标记
            if (File.Exists(GetPendingMarkerPath(name, PendingPidSuffix))
                && PluginPackage.ReadManifest(pendingDir) != null)
            {
                Log.Info("按旧版暂存标记识别暂存更新：" + name);
                return true;
            }
            return false;
        }

        private static string GetPendingMarkerPath(string name, string suffix)
        {
            return Path.Combine(PendingUpdatesDirectory, name + suffix);
        }

        /// <summary>删除没有对应暂存目录的孤立标记文件。</summary>
        private static void TryDeleteOrphanPendingMarkers()
        {
            try
            {
                if (!Directory.Exists(PendingUpdatesDirectory)) return;
                foreach (var file in Directory.GetFiles(PendingUpdatesDirectory))
                {
                    var name = Path.GetFileName(file);
                    string baseName = null;
                    if (name.EndsWith(PendingDoneSuffix, StringComparison.Ordinal))
                    {
                        baseName = name.Substring(0, name.Length - PendingDoneSuffix.Length);
                    }
                    else if (name.EndsWith(PendingPidSuffix, StringComparison.Ordinal))
                    {
                        baseName = name.Substring(0, name.Length - PendingPidSuffix.Length);
                    }

                    if (string.IsNullOrEmpty(baseName)) continue;
                    if (!Directory.Exists(Path.Combine(PendingUpdatesDirectory, baseName))) TryDelete(file);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("清理暂存标记文件失败：" + ex.Message);
            }
        }

        /// <summary>判断路径是否允许作为待卸载目标：必须位于用户插件目录之内，且不能是插件目录本身。</summary>
        private static bool IsRemovablePluginPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var root = Path.GetFullPath(ConfigStore.UserPluginsDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var full = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (root.Length == 0 || full.Length <= root.Length) return false;
                return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>标识是否可用于目录名：仅允许字母、数字、点、下划线、连字符，并排除路径穿越与设备名。</summary>
        private static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!string.Equals(id, id.Trim(), StringComparison.Ordinal)) return false;
            if (id.Length > MaxIdLength) return false;
            if (!SafeIdPattern.IsMatch(id)) return false;
            if (string.Equals(id, ".", StringComparison.Ordinal) || string.Equals(id, "..", StringComparison.Ordinal))
                return false;
            if (id[0] == '.') return false;
            if (id.EndsWith(".", StringComparison.Ordinal)) return false;
            if (IsReservedDeviceName(id)) return false;
            return true;
        }

        private static bool IsReservedDeviceName(string name)
        {
            var dot = name.IndexOf('.');
            var stem = dot >= 0 ? name.Substring(0, dot) : name;
            foreach (var reserved in ReservedDeviceNames)
            {
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static bool ContainsPath(IList<string> paths, string path)
        {
            foreach (var existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>删除目录（临时目录用；不检查占用，失败仅记录日志）。</summary>
        private static bool TryDeleteDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除目录失败（不影响主流程）：" + path
                    + "（" + ex.GetType().Name + "：" + ex.Message + "）");
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Debug("删除文件失败（不影响主流程）：" + path + "（" + ex.Message + "）");
            }
        }

        /// <summary>调用错误回调；回调自身异常不影响安装流程。</summary>
        private static void ReportError(Action<string> onError, string message)
        {
            if (onError == null || string.IsNullOrEmpty(message)) return;
            try
            {
                onError(message);
            }
            catch (Exception ex)
            {
                Log.Debug("错误回调执行失败：" + ex.Message);
            }
        }
    }
}
