namespace UniversalConvert.Core.Plugins
{
    /// <summary>插件加载/初始化错误信息，供扩展管理器展示。</summary>
    public sealed class PluginLoadError
    {
        /// <summary>出错的文件路径或插件 Id。</summary>
        public string File { get; set; }

        /// <summary>错误描述。</summary>
        public string Message { get; set; }
    }
}
