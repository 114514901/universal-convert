using System.Collections.Generic;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 视频预览提供者（可选）：扩展可以声明以自己的播放器接管视频预览
    /// （如内嵌 VLC 的扩展，实现全格式播放与原生 seek 预览帧）。
    /// 加载后主程序预览视频时优先调用；未安装此类扩展时回退内置预览。
    /// 实现此类接口的扩展同时应实现 IConverterPlugin（空能力即可）以便被插件加载器发现。
    /// </summary>
    public interface IVideoPreviewProvider
    {
        /// <summary>是否声明支持预览该扩展名（含点，如 ".mkv"；返回 true 表示全部支持）。</summary>
        bool CanPreviewVideo(string extension);

        /// <summary>以自有预览窗口播放该文件（扩展全权弹窗）。返回 true 表示已接管预览。</summary>
        bool ShowPreview(string filePath);
    }
}