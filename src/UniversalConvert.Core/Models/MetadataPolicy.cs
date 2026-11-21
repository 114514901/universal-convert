using System;
using System.Collections.Generic;

namespace UniversalConvert.Core.Models
{
    /// <summary>
    /// 元数据清除级别。Keep 保留；Tags 清除文本标签但保留封面；All 清除文本标签与封面。
    /// </summary>
    public enum MetadataMode
    {
        Keep,
        Tags,
        All
    }

    /// <summary>插件共用的元数据策略解析，兼容旧版 noCoverArt 布尔选项。</summary>
    public static class MetadataPolicy
    {
        public const string OptionKey = "metadataMode";
        public const string KeepValue = "keep";
        public const string TagsValue = "tags";
        public const string AllValue = "all";

        public static MetadataMode Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return MetadataMode.Keep;
            switch (value.Trim().ToLowerInvariant())
            {
                case TagsValue:
                    return MetadataMode.Tags;
                case AllValue:
                    return MetadataMode.All;
                default:
                    return MetadataMode.Keep;
            }
        }

        /// <summary>解析最终模式；未设置或非法值按 Keep 处理。</summary>
        public static MetadataMode Resolve(IDictionary<string, string> options)
        {
            if (options == null) return MetadataMode.Keep;
            string value;
            return options.TryGetValue(OptionKey, out value)
                ? Parse(value)
                : MetadataMode.Keep;
        }

        /// <summary>旧版 noCoverArt=true 只丢弃封面，不改变标签清除级别。</summary>
        public static bool HasLegacyNoCoverArt(IDictionary<string, string> options)
        {
            string value;
            return options != null
                && options.TryGetValue("noCoverArt", out value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldStripTags(MetadataMode mode)
        {
            return mode == MetadataMode.Tags || mode == MetadataMode.All;
        }

        public static bool ShouldDropCover(MetadataMode mode)
        {
            return mode == MetadataMode.All;
        }
    }
}
