using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalConvert.Core.Engine
{
    /// <summary>
    /// 一个「输入扩展名 → 输出扩展名」方向的解析结果。
    /// 该类型不依赖界面，供主程序、命令行与右键菜单共用。
    /// </summary>
    public sealed class FormatSelection
    {
        /// <summary>所有注册了该方向的转换条目，按注册顺序排列。</summary>
        public IList<ConversionEntry> Candidates { get; private set; }

        /// <summary>最终选定的条目；无法唯一确定时为 null。</summary>
        public ConversionEntry Selected { get; private set; }

        /// <summary>设置中记住的插件 Id；未记住时为 null。</summary>
        public string RememberedPluginId { get; private set; }

        /// <summary>该方向在设置中的记忆键。</summary>
        public string ChoiceKey { get; private set; }

        /// <summary>调用方指定的插件 Id 不在候选列表中。</summary>
        public bool PreferredNotFound { get; private set; }

        /// <summary>没有任何插件注册该方向。</summary>
        public bool IsEmpty
        {
            get { return Candidates == null || Candidates.Count == 0; }
        }

        /// <summary>存在多个候选且无法依据指定插件或记忆确定唯一结果。</summary>
        public bool IsAmbiguous
        {
            get { return Selected == null && !IsEmpty && !PreferredNotFound; }
        }

        internal FormatSelection(
            IList<ConversionEntry> candidates,
            ConversionEntry selected,
            string rememberedPluginId,
            string choiceKey,
            bool preferredNotFound)
        {
            Candidates = candidates ?? new List<ConversionEntry>();
            Selected = selected;
            RememberedPluginId = rememberedPluginId;
            ChoiceKey = choiceKey;
            PreferredNotFound = preferredNotFound;
        }

        /// <summary>候选插件的可读描述，用于错误提示。</summary>
        public string DescribeCandidates()
        {
            if (IsEmpty) return string.Empty;
            return string.Join("，", Candidates
                .Select(c => c.PluginName + "（" + c.PluginId + "）")
                .ToArray());
        }
    }

    /// <summary>
    /// 格式方向解析器（无界面）：从注册表中取出候选条目，并按「显式指定 → 用户记忆 → 唯一候选」的顺序确定插件。
    /// 存在多个候选且无法确定时返回 Selected = null，由调用方决定询问用户或报错。
    /// </summary>
    public static class FormatSelector
    {
        /// <summary>格式选择记忆在设置中的键前缀。</summary>
        public const string ChoiceKeyPrefix = "formatChoice.";

        /// <summary>生成该方向的记忆键。</summary>
        public static string GetChoiceKey(string inputExt, string outputExt)
        {
            return ChoiceKeyPrefix + FormatRegistry.Normalize(inputExt) + "." + FormatRegistry.Normalize(outputExt);
        }

        /// <summary>获取该方向的全部候选条目（同一插件只保留一条）。</summary>
        public static IList<ConversionEntry> GetCandidates(CoreHost host, string inputExt, string outputExt)
        {
            if (host == null || host.Registry == null) return new List<ConversionEntry>();

            var key = FormatRegistry.Normalize(outputExt);
            if (string.IsNullOrEmpty(key)) return new List<ConversionEntry>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ConversionEntry>();
            foreach (var entry in host.Registry.GetConversionsFor(inputExt))
            {
                if (!string.Equals(entry.OutputExtension, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(entry.PluginId) && !seen.Add(entry.PluginId)) continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// 解析该方向应使用的条目。
        /// preferredPluginId：调用方显式指定的插件（可为空）；rememberedPluginId：设置中记住的插件（可为空）。
        /// </summary>
        public static FormatSelection Resolve(
            CoreHost host,
            string inputExt,
            string outputExt,
            string preferredPluginId = null,
            string rememberedPluginId = null)
        {
            var candidates = GetCandidates(host, inputExt, outputExt);
            return Choose(candidates, inputExt, outputExt, preferredPluginId, rememberedPluginId);
        }

        /// <summary>
        /// 在给定候选列表中确定最终条目。该方法不访问注册表，便于独立测试。
        /// </summary>
        public static FormatSelection Choose(
            IList<ConversionEntry> candidates,
            string inputExt,
            string outputExt,
            string preferredPluginId = null,
            string rememberedPluginId = null)
        {
            candidates = candidates ?? new List<ConversionEntry>();
            var choiceKey = GetChoiceKey(inputExt, outputExt);

            if (!string.IsNullOrEmpty(preferredPluginId))
            {
                var preferred = candidates.FirstOrDefault(
                    c => string.Equals(c.PluginId, preferredPluginId, StringComparison.OrdinalIgnoreCase));
                if (preferred == null)
                {
                    return new FormatSelection(candidates, null, rememberedPluginId, choiceKey, true);
                }
                return new FormatSelection(candidates, preferred, rememberedPluginId, choiceKey, false);
            }

            if (!string.IsNullOrEmpty(rememberedPluginId))
            {
                var remembered = candidates.FirstOrDefault(
                    c => string.Equals(c.PluginId, rememberedPluginId, StringComparison.OrdinalIgnoreCase));
                if (remembered != null)
                {
                    return new FormatSelection(candidates, remembered, rememberedPluginId, choiceKey, false);
                }
            }

            if (candidates.Count == 1)
            {
                return new FormatSelection(candidates, candidates[0], rememberedPluginId, choiceKey, false);
            }

            return new FormatSelection(candidates, null, rememberedPluginId, choiceKey, false);
        }
    }
}
