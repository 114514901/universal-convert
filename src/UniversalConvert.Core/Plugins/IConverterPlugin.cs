using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Models;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 转换器插件契约。
    /// 新增一种转换能力 = 新建一个类库 DLL 实现本接口，放入 Plugins 目录即可，
    /// 主程序与右键菜单通过 FormatRegistry 自动发现新能力，无需任何改动。
    ///
    /// 约束：GetCapabilities() 必须是"纯"的（只返回静态声明，不得加载原生库或外部进程），
    /// 因为右键菜单会把插件加载进 explorer.exe 进程，纯声明可保证菜单秒开且不拖垮资源管理器。
    /// 所有重量级操作只发生在 ConvertAsync 中（在主程序进程里执行）。
    /// </summary>
    public interface IConverterPlugin
    {
        /// <summary>插件唯一标识，例如 "com.universalconvert.ffmpeg"。</summary>
        string Id { get; }

        /// <summary>插件显示名称，例如 "FFmpeg"。</summary>
        string Name { get; }

        /// <summary>插件描述。</summary>
        string Description { get; }

        /// <summary>
        /// 插件加载后立即调用一次，用于注入宿主环境（工具定位、日志、数据目录等）。
        /// </summary>
        void Initialize(IPluginContext context);

        /// <summary>
        /// 声明转换能力：哪些输入扩展名 -> 可转出哪些输出格式。
        /// 纯声明，必须廉价且无副作用。
        /// </summary>
        IList<ConversionCapability> GetCapabilities();

        /// <summary>
        /// 外部工具（如 ffmpeg.exe）是否可用。用于过滤菜单项、UI 提示。
        /// 只做文件/路径存在性检查，不得启动转换。
        /// </summary>
        bool IsToolAvailable();

        /// <summary>
        /// 执行一次转换。运行在主程序进程，可启动外部进程、读写文件。
        /// </summary>
        Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress> progress,
            CancellationToken cancellationToken);
    }
}
