using System;
using System.Collections.Generic;
using System.Linq;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Engine
{
    /// <summary>
    /// 格式注册表：把所有插件的转换能力展开成统一的 输入扩展名 -> 输出格式 映射。
    /// UI 与右键菜单都从这里读取，从而"自动感知"新插件，无需改动调用方。
    /// </summary>
    public sealed class FormatRegistry
    {
        private readonly List<ConversionEntry> _entries;

        public FormatRegistry(IEnumerable<IConverterPlugin> plugins)
        {
            _entries = new List<ConversionEntry>();
            foreach (var plugin in plugins)
            {
                foreach (var cap in plugin.GetCapabilities())
                {
                    var inputExt = Normalize(cap.InputExtension);
                    foreach (var output in cap.Outputs)
                    {
                        _entries.Add(new ConversionEntry
                        {
                            PluginId = plugin.Id,
                            PluginName = plugin.Name,
                            InputExtension = inputExt,
                            InputDisplayName = cap.InputDisplayName,
                            OutputExtension = Normalize(output.Extension),
                            OutputDisplayName = output.DisplayName,
                            IsAvailable = plugin.IsToolAvailable(),
                            IsUntested = plugin.IsUntested,
                            Presets = output.Presets,
                            Options = output.Options
                        });
                    }
                }
            }
        }

        /// <summary>所有可转换记录。</summary>
        public IReadOnlyList<ConversionEntry> Entries => _entries;

        /// <summary>给定文件扩展名（可含点、任意大小写），返回可转出的所有目标记录。</summary>
        public IEnumerable<ConversionEntry> GetConversionsFor(string fileExtension)
        {
            var key = Normalize(fileExtension);
            return _entries.Where(e => e.InputExtension == key);
        }

        /// <summary>按输入 + 输出扩展名精确查找一条转换记录。</summary>
        public ConversionEntry GetEntry(string inputExtension, string outputExtension)
        {
            var input = Normalize(inputExtension);
            var output = Normalize(outputExtension);
            return _entries.FirstOrDefault(e => e.InputExtension == input && e.OutputExtension == output);
        }

        /// <summary>所有"已注册"的输入扩展名集合。</summary>
        public ISet<string> KnownInputExtensions
        {
            get
            {
                return new HashSet<string>(_entries.Select(e => e.InputExtension));
            }
        }

        /// <summary>扩展名是否被任一插件支持为输入。</summary>
        public bool Supports(string fileExtension)
        {
            return _entries.Any(e => e.InputExtension == Normalize(fileExtension));
        }

        /// <summary>规范化扩展名：转小写、去掉前导点。</summary>
        public static string Normalize(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return string.Empty;
            var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
            return ext;
        }
    }
}
