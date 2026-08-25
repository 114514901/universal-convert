using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 可选接口：为文件提供音频预览渲染能力（如 MIDI 合成）。
    /// 一个插件可同时实现 IConverterPlugin 与 IPreviewProvider，互不冲突。
    ///
    /// 主程序的预览窗口按"提供者优先"策略工作：若某插件声明支持该输入扩展名，
    /// 先调用 RenderPreviewAsync 渲染出可播放的音频文件再交给播放器；
    /// 渲染失败或没有提供者时，回退到播放器直接打开，最后再回退到 ffmpeg 转码。
    /// </summary>
    public interface IPreviewProvider
    {
        /// <summary>支持的预览输入扩展名（小写、含点，如 ".mid"、".midi"）。</summary>
        IList<string> SupportedPreviewExtensions { get; }

        /// <summary>
        /// 把输入文件渲染为可播放的音频文件（通常为 WAV）。
        /// 返回生成文件的绝对路径；失败返回 null（调用方回退到其它预览方式）。
        /// 返回的临时文件由调用方负责删除，插件不得自行清理。
        /// </summary>
        Task<string> RenderPreviewAsync(string inputPath, CancellationToken cancellationToken);
    }
}
