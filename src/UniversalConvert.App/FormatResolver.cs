using System;
using System.Linq;
using UniversalConvert.Core;
using UniversalConvert.Core.Engine;

namespace UniversalConvert.App
{
    /// <summary>
    /// 格式方向解析器：同一「输入 → 输出」方向被多个插件/扩展注册时，
    /// 询问用户使用哪个，并可记住选择（设置在「高级」里可清除）。
    /// 若记住的插件后来被卸载，自动回退：候选里没有了就重新询问。
    /// </summary>
    public static class FormatResolver
    {
        public const string ChoiceKeyPrefix = "formatChoice.";

        public static string GetChoiceKey(string inputExt, string outputExt)
        {
            return ChoiceKeyPrefix + FormatRegistry.Normalize(inputExt) + "." + FormatRegistry.Normalize(outputExt);
        }

        /// <summary>解析该方向应使用的条目：唯一注册直接返回；多注册查记忆 → 无记忆则询问用户。</summary>
        public static ConversionEntry Resolve(CoreHost host, SettingsManager settings, string inputExt, string outputExt)
        {
            var candidates = host.Registry.GetConversionsFor(inputExt)
                .Where(e => string.Equals(e.OutputExtension, FormatRegistry.Normalize(outputExt), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count <= 1) return candidates.FirstOrDefault();

            // 有记住的选择且对应插件仍在 → 直接用（不再询问）
            var remembered = settings.Get(GetChoiceKey(inputExt, outputExt));
            if (!string.IsNullOrEmpty(remembered))
            {
                var entry = candidates.FirstOrDefault(e => e.PluginId == remembered);
                if (entry != null) return entry;
            }

            // 询问用户（可勾选「不再提醒」）
            var window = new FormatChoiceWindow(candidates, inputExt, outputExt);
            if (window.ShowDialog() == true && window.Choice != null)
            {
                if (window.Remember)
                {
                    settings.Set(GetChoiceKey(inputExt, outputExt), window.Choice.PluginId);
                }
                return window.Choice;
            }

            // 取消则退回第一个候选
            return candidates.FirstOrDefault();
        }

        /// <summary>清除所有记住的格式选择。</summary>
        public static void ClearAllChoices(SettingsManager settings)
        {
            settings.ClearKeysWithPrefix(ChoiceKeyPrefix);
        }
    }
}
