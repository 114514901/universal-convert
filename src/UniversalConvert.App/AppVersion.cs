using System.Reflection;

namespace UniversalConvert.App
{
    /// <summary>当前应用版本（读取 InformationalVersion，含可能的 prerelease 后缀；失败则回退到程序集版本）。</summary>
    public static class AppVersion
    {
        public static SemVersion Current
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();

                var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var version = SemVersion.Parse(attr?.InformationalVersion);
                if (version != null) return version;

                var av = assembly.GetName().Version;
                return new SemVersion(av.Major, av.Minor, av.Build, null);
            }
        }
    }
}
