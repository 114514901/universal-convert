namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 插件包的清单（manifest.json），用于在加载 DLL 之前展示信息、判断兼容性。
    /// 字段与 IConverterPlugin 的元数据对应，供扩展中心浏览/安装/更新时使用。
    /// </summary>
    public sealed class PluginManifest
    {
        /// <summary>插件唯一标识，如 "com.universalconvert.pandoc"。</summary>
        public string Id { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>插件版本（SemVer）。</summary>
        public string Version { get; set; }

        public string Author { get; set; }

        /// <summary>最低可运行应用版本（SemVer）；null 表示无限制。</summary>
        public string MinAppVersion { get; set; }

        /// <summary>最高已知可用应用版本（SemVer）；null 表示无上限。</summary>
        public string MaxAppVersion { get; set; }

        public string Homepage { get; set; }
    }
}
