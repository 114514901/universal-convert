using System;
using System.Diagnostics;
using System.Reflection;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Cli
{
    /// <summary>
    /// 命令行程序与 UniversalConvert.Core.dll 的版本一致性检查。
    /// 安装过程中文件被占用并跳过时，CLI 可能比 Core 新；提前给出明确提示，
    /// 避免在调用 Core 新方法时抛出 MissingMethodException。
    /// </summary>
    internal static class CliCompatibility
    {
        public static string GetCliVersion()
        {
            return GetInformationalVersion(typeof(CliRunner).Assembly);
        }

        public static string GetCoreVersion()
        {
            return GetInformationalVersion(typeof(OutputPathHelper).Assembly);
        }

        /// <summary>返回 null 表示版本一致；否则返回可直接输出的正式错误信息。</summary>
        public static string Check()
        {
            try
            {
                var cli = GetCliVersion();
                var core = GetCoreVersion();
                if (string.Equals(Normalize(cli), Normalize(core), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return string.Format(CliText.ComponentMismatch, core ?? CliText.UnknownVersion, cli ?? CliText.UnknownVersion);
            }
            catch (Exception ex)
            {
                return string.Format(CliText.ComponentLoadFailed, ex.Message);
            }
        }

        public static string GetInformationalVersion(Assembly assembly)
        {
            if (assembly == null) return null;
            try
            {
                var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var version = attribute != null ? attribute.InformationalVersion : null;
                if (string.IsNullOrEmpty(version))
                {
                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(assembly.Location);
                        version = info.ProductVersion;
                    }
                    catch
                    {
                        // 回退到程序集版本
                    }
                }
                if (string.IsNullOrEmpty(version) && assembly.GetName().Version != null)
                {
                    version = assembly.GetName().Version.ToString();
                }
                if (string.IsNullOrEmpty(version)) return null;

                var plus = version.IndexOf('+');
                if (plus > 0) version = version.Substring(0, plus);
                return version.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string Normalize(string version)
        {
            if (string.IsNullOrEmpty(version)) return string.Empty;
            return version.Trim().TrimStart('v', 'V');
        }
    }
}
