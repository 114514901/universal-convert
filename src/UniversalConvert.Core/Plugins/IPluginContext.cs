using System;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 宿主环境注入给插件的上下文。插件不直接读配置/找工具，而是通过它解耦。
    /// </summary>
    public interface IPluginContext
    {
        /// <summary>按工具名（如 "ffmpeg"）解析外部工具可执行文件路径；找不到返回 null。</summary>
        string FindTool(string toolName);

        /// <summary>插件私有数据目录（用于写临时文件等）。</summary>
        string DataDirectory { get; }

        /// <summary>写日志。</summary>
        void Log(string message);
    }
}
