using System.Collections.Generic;
using UniversalConvert.Core;
using UniversalConvert.Core.Engine;

namespace UniversalConvert.App
{
    /// <summary>
    /// 格式方向解析器（界面侧）：从 Core 的 FormatSelector 取得候选，
    /// 需要时弹出选择窗口并记住用户选择。
    /// </summary>
    public static class FormatResolver
    {
        public const string ChoiceKeyPrefix = FormatSelector.ChoiceKeyPrefix;

        public static string GetChoiceKey(string inputExt, string outputExt)
        {
            return FormatSelector.GetChoiceKey(inputExt, outputExt);
        }

        /// <summary>解析该方向应使用的条目：唯一注册直接返回；多注册查记忆，无记忆则询问用户。</summary>
        /// <param name="preferredPluginId">命令行显式指定的插件；指定后不再询问，未命中返回 null。</param>
        public static ConversionEntry Resolve(
            CoreHost host,
            SettingsManager settings,
            string inputExt,
            string outputExt,
            string preferredPluginId = null)
        {
            var remembered = settings.Get(GetChoiceKey(inputExt, outputExt));
            var selection = FormatSelector.Resolve(host, inputExt, outputExt, preferredPluginId, remembered);

            if (selection.PreferredNotFound) return null;
            if (selection.Selected != null) return selection.Selected;
            if (selection.IsEmpty) return null;

            // 询问用户（可勾选「不再提醒」）
            var window = new FormatChoiceWindow(selection.Candidates, inputExt, outputExt);
            if (window.ShowDialog() == true && window.Choice != null)
            {
                if (window.Remember)
                {
                    settings.Set(GetChoiceKey(inputExt, outputExt), window.Choice.PluginId);
                }
                return window.Choice;
            }

            // 取消则退回第一个候选
            return selection.Candidates.Count > 0 ? selection.Candidates[0] : null;
        }

        /// <summary>获取该方向的全部候选条目，供错误提示使用。</summary>
        public static IList<ConversionEntry> GetCandidates(
            CoreHost host,
            string inputExt,
            string outputExt)
        {
            return FormatSelector.GetCandidates(host, inputExt, outputExt);
        }

        /// <summary>清除所有记住的格式选择。</summary>
        public static void ClearAllChoices(SettingsManager settings)
        {
            settings.ClearKeysWithPrefix(ChoiceKeyPrefix);
        }
    }
}
