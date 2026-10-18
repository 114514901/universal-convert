using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Process;

namespace UniversalConvert.App
{
    /// <summary>
    /// 更新安装包的统一下载与启动入口。主界面与设置界面共用，避免两套实现分叉。
    ///
    /// 约定：
    /// - 只处理具备 DownloadUrl 与 Sha256 的更新；
    /// - 下载到唯一临时文件，失败/取消时删除；
    /// - 启动前校验最低可执行文件特征；
    /// - 安装包由 Inno Setup 以静默模式运行，并保留右键菜单任务。
    /// </summary>
    public static class UpdateInstaller
    {
        public static async Task<string> DownloadAsync(
            UpdateInfo info,
            IProgress<double> progress,
            CancellationToken ct)
        {
            if (info == null) throw new ArgumentNullException("info");
            if (!info.CanAutoInstall)
            {
                throw new InvalidOperationException("该版本缺少下载地址或完整性摘要，无法自动安装。");
            }

            var destination = Path.Combine(
                Path.GetTempPath(),
                "UniversalConvert-Setup-" + SanitizeVersion(info.Version) + "-" +
                Guid.NewGuid().ToString("N") + ".exe");

            try
            {
                await UpdateChecker.DownloadAsync(
                    info.DownloadUrl, destination, progress, ct, info.Sha256).ConfigureAwait(false);
                UpdateChecker.VerifyInstallerFile(destination);
                return destination;
            }
            catch
            {
                TryDelete(destination);
                throw;
            }
        }

        /// <summary>启动安装包。失败时抛出异常，由调用方展示正式提示。</summary>
        public static void Launch(string installerPath)
        {
            if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
            {
                throw new FileNotFoundException("安装包不存在。", installerPath);
            }
            UpdateChecker.VerifyInstallerFile(installerPath);

            var installDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            try
            {
                // 先按句柄关闭 explorer 等进程对本产品文件的占用；当前进程自身持有的句柄
                // 由安装器在退出应用后处理，不能在本进程内替换已加载的程序集。
                var unlock = FileUnlocker.Unlock(new[] { installDirectory }, FileUnlockPolicy.Safe);
                if (unlock.HandlesClosed > 0)
                {
                    Log.Info("启动更新前已关闭文件句柄 " + unlock.HandlesClosed
                        + " 个：" + string.Join("、", unlock.Processes.ToArray()));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("启动更新前解除文件占用失败：" + ex.Message);
            }

            var startInfo = new ProcessStartInfo(installerPath)
            {
                Arguments = "/SILENT /NORESTART /FORCECLOSEAPPLICATIONS /MERGETASKS=runapp,contextmenu /DIR=\"" + installDirectory + "\"",
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo);
        }

        /// <summary>打开发布页，供缺少自动安装条件或用户拒绝自动更新时使用。</summary>
        public static void OpenReleasePage(UpdateInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Url))
            {
                throw new InvalidOperationException("发布页地址未知。");
            }
            Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true });
        }

        public static void DeleteDownloadedFile(string path)
        {
            TryDelete(path);
        }

        private static string SanitizeVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(version.Length);
            foreach (var ch in version.Trim().TrimStart('v', 'V'))
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }
            var value = builder.ToString().Trim();
            return value.Length == 0 ? "unknown" : value;
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 临时文件清理失败由系统临时目录兜底
            }
        }
    }
}
