using System.Reflection;
using UniversalConvert.Core;

namespace UniversalConvert.App
{
    /// <summary>当前应用版本（读取 InformationalVersion，含可能的 prerelease 后缀）。</summary>
    public static class AppVersion
    {
        public static SemVersion Current
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return SemVersion.Parse(attr?.InformationalVersion);
            }
        }
    }
}
