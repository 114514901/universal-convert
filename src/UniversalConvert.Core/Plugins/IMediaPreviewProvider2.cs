namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 媒体预览提供者 v2（可选）：在 IMediaPreviewProvider 基础上支持传入「显示名」——
    /// 渲染/解密预览（ncm/midi 等）时文件是临时产物，标题应显示原文件名。
    /// 主程序优先尝试本接口，旧扩展（仅实现 IMediaPreviewProvider）继续走回退路径。
    /// </summary>
    public interface IMediaPreviewProvider2 : IMediaPreviewProvider
    {
        /// <summary>以自有预览窗口播放文件，标题显示 displayName（原文件名）。返回 true 表示已接管。</summary>
        bool ShowPreviewWithName(string filePath, string displayName);
    }
}