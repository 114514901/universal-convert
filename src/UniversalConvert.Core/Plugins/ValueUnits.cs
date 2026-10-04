using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 参数值的单位类型。UI（编辑表单）与插件（拼命令行）共用同一套归一化，
    /// 避免「可读写法」（如 "24 kbps"）漏到命令行或高级参数框里。
    /// </summary>
    public enum ValueUnitKind
    {
        /// <summary>无单位约定，原样使用。</summary>
        None,

        /// <summary>码率：可读写法带单位（"24 kbps" / "24k"）→ 工具需要的 "24k"。</summary>
        Bitrate,

        /// <summary>采样率：可读写法（"44.1 kHz" / "44.1k" / "44100"）→ 工具需要的 Hz 整数。</summary>
        SampleRate
    }

    /// <summary>单位归一化：把用户可读写法转成外部工具要求的紧凑写法。</summary>
    public static class ValueUnits
    {
        /// <summary>按单位类型归一化；无法识别时原样返回。</summary>
        public static string Normalize(string value, ValueUnitKind kind)
        {
            switch (kind)
            {
                case ValueUnitKind.Bitrate: return NormalizeBitrate(value);
                case ValueUnitKind.SampleRate: return NormalizeSampleRate(value);
                default: return value;
            }
        }

        /// <summary>
        /// 归一化码率：预设显示 "500 kbps" / 用户输入 "500k"，FFmpeg 需要 "500k"。
        /// 识别写法："320 kbps" / "320kbps" / "320k" / "320" → "320k"；无法识别则原样返回。
        /// </summary>
        public static string NormalizeBitrate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var m = Regex.Match(value.Trim(), @"(\d+(?:\.\d+)?)");
            if (!m.Success) return value;

            return m.Groups[1].Value + "k";
        }

        /// <summary>
        /// 归一化采样率：预设显示 "44.1 kHz"，FFmpeg 需要 Hz（44100）。
        /// 识别 "44.1 kHz" / "44.1k" / "44100" / "48k" → 转整数 Hz；无法识别则原样返回。
        /// </summary>
        public static string NormalizeSampleRate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var m = Regex.Match(value.Trim(), @"(\d+(?:\.\d+)?)\s*(k|khz|hz)?", RegexOptions.IgnoreCase);
            if (!m.Success) return value;

            double num;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out num))
            {
                return value;
            }

            var unit = (m.Groups[2].Value ?? string.Empty).ToLowerInvariant();
            if (unit.StartsWith("k")) num *= 1000.0;   // kHz → Hz
            return ((int)Math.Round(num)).ToString(CultureInfo.InvariantCulture);
        }
    }
}
