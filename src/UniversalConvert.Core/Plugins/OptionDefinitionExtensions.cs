using System;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 选项声明辅助：用链式调用标注「值的单位类型」，让 UI 取值时能归一化。
    ///
    /// 各插件统一用它，不要再各自实现一份「码率选项工厂」——那种复制正是过去
    /// 「改一处漏两处」的来源（例如只有 FFmpeg 插件声明了单位，NCM/KGM/QMC 手输
    /// <c>320 kbps</c> 就会原样拼进命令行导致转换失败）。
    /// </summary>
    public static class OptionDefinitionExtensions
    {
        /// <summary>标注为码率：<c>"320 kbps"</c> / <c>"320k"</c> / <c>"320"</c> → <c>"320k"</c>。</summary>
        public static OptionDefinition AsBitrate(this OptionDefinition option)
        {
            if (option != null) option.UnitKind = ValueUnitKind.Bitrate;
            return option;
        }

        /// <summary>标注为采样率：<c>"44.1 kHz"</c> / <c>"44.1k"</c> → <c>"44100"</c>。</summary>
        public static OptionDefinition AsSampleRate(this OptionDefinition option)
        {
            if (option != null) option.UnitKind = ValueUnitKind.SampleRate;
            return option;
        }
    }
}
