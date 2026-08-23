using System;
using System.Collections.Generic;
using System.Linq;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Engine
{
    /// <summary>
    /// 参数合并：默认值 → 预设覆盖 → 用户手改覆盖，三者统一成最终 Options 字典。
    /// 插件只消费 Options，无需感知来源。
    /// </summary>
    public static class PresetMerger
    {
        /// <summary>
        /// 合并出最终参数字典。
        /// </summary>
        /// <param name="options">参数 schema（取其 DefaultValue）。</param>
        /// <param name="presets">命名预设列表。</param>
        /// <param name="presetName">选中的预设名，可为空。</param>
        /// <param name="userOverrides">用户在表单里手改的覆盖，可为空。</param>
        public static IDictionary<string, string> Merge(
            IList<OptionDefinition> options,
            IList<ConversionPreset> presets,
            string presetName,
            IDictionary<string, string> userOverrides)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. 默认值
            if (options != null)
            {
                foreach (var option in options)
                {
                    if (!string.IsNullOrEmpty(option.Key) && !string.IsNullOrEmpty(option.DefaultValue))
                    {
                        result[option.Key] = option.DefaultValue;
                    }
                }
            }

            // 2. 预设覆盖
            if (!string.IsNullOrEmpty(presetName) && presets != null)
            {
                var preset = presets.FirstOrDefault(p => p.Name == presetName);
                if (preset != null)
                {
                    foreach (var kv in preset.Options)
                    {
                        result[kv.Key] = kv.Value;
                    }
                }
            }

            // 3. 用户覆盖
            if (userOverrides != null)
            {
                foreach (var kv in userOverrides)
                {
                    if (string.IsNullOrEmpty(kv.Value))
                    {
                        result.Remove(kv.Key);
                    }
                    else
                    {
                        result[kv.Key] = kv.Value;
                    }
                }
            }

            return result;
        }
    }
}
