namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 媒体预览提供者（音视频通用，扩展 IVideoPreviewProvider）：
    /// 扩展可声明同时接管音频与视频预览（如内嵌 VLC 的扩展）。
    /// 主程序预览音频时优先调用；未安装此类扩展时回退内置音频预览。
    /// </summary>
    public interface IMediaPreviewProvider : IVideoPreviewProvider
    {
        /// <summary>是否声明支持预览该音频扩展名（含点，如 ".flac"；返回 true 表示全部支持）。</summary>
        bool CanPreviewAudio(string extension);
    }
}