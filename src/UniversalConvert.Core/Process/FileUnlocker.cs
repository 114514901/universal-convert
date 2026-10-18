using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.Core.Process
{
    public enum FileUnlockPolicy
    {
        /// <summary>仅处理本产品进程与 explorer，避免关闭未知应用的文件句柄。</summary>
        Safe,
        /// <summary>处理所有可访问且非系统关键进程；仍不结束进程，只关闭命中目标文件的句柄。</summary>
        All
    }

    public sealed class FileLockProcess
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ApplicationName { get; set; }
    }

    public sealed class FileUnlockResult
    {
        public int HandlesClosed { get; set; }
        public bool Complete { get; set; }
        public int FilesRemainingLocked { get; set; }
        public List<string> Processes { get; private set; }
        public List<string> RemainingProcesses { get; private set; }

        public FileUnlockResult()
        {
            Processes = new List<string>();
            RemainingProcesses = new List<string>();
        }
    }

    /// <summary>
    /// 文件占用解除：先用 Restart Manager 找出占用进程，再枚举这些进程的句柄，
    /// 仅对命中目标文件路径的文件句柄执行 DuplicateHandle(..., DUPLICATE_CLOSE_SOURCE)。
    /// 不结束进程，不关闭未知文件句柄；对已映射为镜像的 DLL 仍可能受 Windows 映像段限制，
    /// 此时调用方应退回暂存更新或重启应用。
    /// </summary>
    public static class FileUnlocker
    {
        private const int ErrorSuccess = 0;
        private const int ErrorMoreData = 234;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
        private const int StatusBufferOverflow = unchecked((int)0x80000005);
        private const uint ProcessDupHandle = 0x0040;
        private const uint DuplicateSameAccess = 0x00000002;
        private const uint DuplicateCloseSource = 0x00000001;
        private const int ObjectNameInformation = 1;
        private const int ObjectTypeInformation = 2;
        private const int SystemExtendedHandleInformation = 64;

        private static readonly string[] SafeProcessNames =
        {
            "explorer",
            "universalconvert.app",
            "universalconvert.cli",
            "universalconvert.contextmenu",
            "universalconvert.installer"
        };

        private static readonly string[] CriticalProcessNames =
        {
            "system", "idle", "smss", "csrss", "wininit", "services", "lsass", "winlogon"
        };

        public static IList<FileLockProcess> FindLockingProcesses(IEnumerable<string> paths)
        {
            var files = ExpandFiles(paths);
            if (files.Count == 0) return new List<FileLockProcess>();

            try
            {
                return QueryRestartManager(files);
            }
            catch (Exception ex)
            {
                Log.Warn("Restart Manager 查询文件占用失败：" + ex.Message);
                return new List<FileLockProcess>();
            }
        }

        /// <summary>
        /// 关闭占用进程内命中目标文件的句柄。policy=Safe 只处理 explorer 与本产品进程；
        /// policy=All 处理所有可访问的非系统关键进程。任何情况下都不结束进程。
        /// </summary>
        public static FileUnlockResult Unlock(IEnumerable<string> paths, FileUnlockPolicy policy)
        {
            var files = ExpandFiles(paths);
            var result = new FileUnlockResult();
            if (files.Count == 0)
            {
                result.Complete = true;
                return result;
            }

            var lockers = FindLockingProcesses(files);
            var allowed = new Dictionary<int, FileLockProcess>();
            foreach (var locker in lockers)
            {
                var name = (locker.ProcessName ?? string.Empty).ToLowerInvariant();
                if (locker.ProcessId == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                if (name.Length == 0) continue;
                if (CriticalProcessNames.Contains(name)) continue;
                if (policy == FileUnlockPolicy.Safe && !SafeProcessNames.Contains(name)) continue;
                allowed[locker.ProcessId] = locker;
            }

            if (allowed.Count == 0)
            {
                result.FilesRemainingLocked = CountExclusiveFailures(files);
                result.Complete = result.FilesRemainingLocked == 0;
                return result;
            }

            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var ntPath = TryGetNtPath(file);
                if (!string.IsNullOrEmpty(ntPath)) expectedPaths.Add(NormalizeNtPath(ntPath));
            }

            if (expectedPaths.Count == 0)
            {
                Log.Warn("无法解析目标文件的 NT 路径，跳过句柄关闭。");
                result.FilesRemainingLocked = CountExclusiveFailures(files);
                result.Complete = result.FilesRemainingLocked == 0;
                return result;
            }

            try
            {
                var entries = QuerySystemHandles();
                var processHandles = new Dictionary<int, IntPtr>();
                try
                {
                    foreach (var entry in entries)
                    {
                        if (!allowed.ContainsKey(entry.ProcessId)) continue;
                        IntPtr processHandle;
                        if (!processHandles.TryGetValue(entry.ProcessId, out processHandle))
                        {
                            processHandle = OpenProcess(ProcessDupHandle, false, entry.ProcessId);
                            processHandles[entry.ProcessId] = processHandle;
                        }
                        if (processHandle == IntPtr.Zero) continue;

                        IntPtr duplicated;
                        if (!DuplicateHandle(
                            processHandle, entry.Handle, GetCurrentProcess(), out duplicated,
                            0, false, DuplicateSameAccess))
                        {
                            continue;
                        }

                        try
                        {
                            var type = QueryObjectName(duplicated, ObjectTypeInformation);
                            if (!string.Equals(type, "File", StringComparison.OrdinalIgnoreCase)) continue;

                            var name = QueryObjectName(duplicated, ObjectNameInformation);
                            if (string.IsNullOrEmpty(name)) continue;
                            if (!expectedPaths.Contains(NormalizeNtPath(name))) continue;

                            IntPtr closedHandle;
                            if (DuplicateHandle(
                                processHandle, entry.Handle, GetCurrentProcess(), out closedHandle,
                                0, false, DuplicateCloseSource))
                            {
                                if (closedHandle != IntPtr.Zero) CloseHandle(closedHandle);
                                result.HandlesClosed++;
                                var label = DescribeProcess(entry.ProcessId);
                                if (!result.Processes.Contains(label)) result.Processes.Add(label);
                            }
                        }
                        finally
                        {
                            CloseHandle(duplicated);
                        }
                    }
                }
                finally
                {
                    foreach (var pair in processHandles)
                    {
                        if (pair.Value != IntPtr.Zero) CloseHandle(pair.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("关闭文件句柄失败：" + ex.Message);
            }

            result.FilesRemainingLocked = CountExclusiveFailures(files);
            var remaining = FindLockingProcesses(files);
            foreach (var item in remaining)
            {
                var label = DescribeProcess(item.ProcessId);
                if (!result.RemainingProcesses.Contains(label)) result.RemainingProcesses.Add(label);
            }
            result.Complete = result.FilesRemainingLocked == 0;
            return result;
        }

        /// <summary>探测文件能否以独占方式打开；用于验证解除占用是否完成。</summary>
        public static bool TryOpenExclusive(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return true;
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int CountExclusiveFailures(IList<string> files)
        {
            var count = 0;
            foreach (var file in files)
            {
                if (!TryOpenExclusive(file)) count++;
            }
            return count;
        }

        private static IList<string> ExpandFiles(IEnumerable<string> paths)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null) return result;

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    if (File.Exists(path))
                    {
                        var full = Path.GetFullPath(path);
                        if (seen.Add(full)) result.Add(full);
                    }
                    else if (Directory.Exists(path))
                    {
                        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        {
                            var full = Path.GetFullPath(file);
                            if (seen.Add(full)) result.Add(full);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("枚举待解锁路径失败：" + path + "（" + ex.Message + "）");
                }
            }
            return result;
        }

        private static IList<FileLockProcess> QueryRestartManager(IList<string> files)
        {
            var result = new List<FileLockProcess>();
            uint session;
            var key = new StringBuilder(256);
            var start = RmStartSession(out session, 0, key);
            if (start != ErrorSuccess) return result;

            try
            {
                var resourceArray = files.ToArray();
                var register = RmRegisterResources(
                    session, (uint)resourceArray.Length, resourceArray, 0, null, 0, null);
                if (register != ErrorSuccess) return result;

                uint needed;
                uint count = 0;
                uint reasons = 0;
                var query = RmGetList(session, out needed, ref count, null, ref reasons);
                if (query == ErrorSuccess && needed == 0) return result;
                if (query != ErrorMoreData && query != ErrorSuccess) return result;

                count = needed;
                var infos = new RM_PROCESS_INFO[count];
                query = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (query != ErrorSuccess) return result;

                for (uint i = 0; i < count; i++)
                {
                    var info = infos[i];
                    var pid = info.Process.dwProcessId;
                    if (pid <= 0) continue;
                    var name = GetProcessName(pid, info.strAppName);
                    if (result.Any(x => x.ProcessId == pid)) continue;
                    result.Add(new FileLockProcess
                    {
                        ProcessId = pid,
                        ProcessName = name,
                        ApplicationName = info.strAppName
                    });
                }
            }
            finally
            {
                RmEndSession(session);
            }
            return result;
        }

        private static string GetProcessName(int pid, string fallback)
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById(pid))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return string.IsNullOrEmpty(fallback) ? "pid:" + pid : fallback;
            }
        }

        private static string DescribeProcess(int pid)
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById(pid))
                {
                    return process.ProcessName + " (" + pid + ")";
                }
            }
            catch
            {
                return "pid:" + pid;
            }
        }

        private static IList<SystemHandleEntry> QuerySystemHandles()
        {
            var size = 1024 * 1024;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int returned;
                    var status = NtQuerySystemInformation(
                        SystemExtendedHandleInformation, buffer, size, out returned);
                    if (status == StatusInfoLengthMismatch || status == StatusBufferOverflow)
                    {
                        size = Math.Max(returned + 1024 * 1024, size * 2);
                        continue;
                    }
                    if (status != 0) throw new Win32Exception(status);

                    var count = IntPtr.Size == 8
                        ? Marshal.ReadInt64(buffer)
                        : (long)Marshal.ReadInt32(buffer);
                    var entrySize = IntPtr.Size == 8 ? 40 : 28;
                    var offset = 2 * IntPtr.Size;
                    var entries = new List<SystemHandleEntry>();
                    for (long i = 0; i < count; i++)
                    {
                        var entry = IntPtr.Add(buffer, offset + (int)(i * entrySize));
                        var pid = IntPtr.Size == 8
                            ? (int)Marshal.ReadInt64(entry, IntPtr.Size)
                            : Marshal.ReadInt32(entry, IntPtr.Size);
                        var handle = IntPtr.Size == 8
                            ? Marshal.ReadIntPtr(entry, 2 * IntPtr.Size)
                            : (IntPtr)Marshal.ReadInt32(entry, 2 * IntPtr.Size);
                        if (pid > 0) entries.Add(new SystemHandleEntry(pid, handle));
                    }
                    return entries;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return new List<SystemHandleEntry>();
        }

        private static string TryGetNtPath(string file)
        {
            try
            {
                using (var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    return QueryObjectName(stream.SafeFileHandle.DangerousGetHandle(), ObjectNameInformation);
                }
            }
            catch
            {
                return TryMapDosPath(file);
            }
        }

        private static string TryMapDosPath(string file)
        {
            try
            {
                var full = Path.GetFullPath(file);
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root) || root.StartsWith("\\\\", StringComparison.Ordinal)) return null;
                var deviceName = root.TrimEnd('\\');
                var target = new StringBuilder(1024);
                if (QueryDosDevice(deviceName, target, target.Capacity) == 0) return null;
                var relative = full.Substring(root.Length);
                return target.ToString().TrimEnd('\\') + "\\" + relative;
            }
            catch
            {
                return null;
            }
        }

        private static string QueryObjectName(IntPtr handle, int infoClass)
        {
            var size = 64 * 1024;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                int returned;
                var status = NtQueryObject(handle, infoClass, buffer, size, out returned);
                if (status != 0) return null;
                var unicode = (UNICODE_STRING)Marshal.PtrToStructure(buffer, typeof(UNICODE_STRING));
                if (unicode.Buffer == IntPtr.Zero || unicode.Length == 0) return null;
                return Marshal.PtrToStringUni(unicode.Buffer, unicode.Length / 2);
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string NormalizeNtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var value = path.Trim();
            if (value.StartsWith("\\??\\", StringComparison.Ordinal)) value = value.Substring(4);
            return value.TrimEnd('\\');
        }

        private sealed class SystemHandleEntry
        {
            public int ProcessId { get; private set; }
            public IntPtr Handle { get; private set; }

            public SystemHandleEntry(int processId, IntPtr handle)
            {
                ProcessId = processId;
                Handle = handle;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryObject(
            IntPtr Handle, int ObjectInformationClass, IntPtr ObjectInformation,
            int ObjectInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
    }
}
