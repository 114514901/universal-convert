using System;
using System.Collections.Generic;
using System.Linq;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// 设置管理器：聚合应用级设置 + 所有插件的自定义设置（ISettingsContributor），
    /// 提供统一的读取、写入与持久化。
    /// </summary>
    public sealed class SettingsManager
    {
        private readonly AppConfig _config;
        private readonly ConfigStore _store = new ConfigStore();
        private readonly List<SettingDefinition> _definitions = new List<SettingDefinition>();

        public SettingsManager(AppConfig config, IEnumerable<IConverterPlugin> plugins)
        {
            _config = config ?? new AppConfig();

            _definitions.AddRange(SettingsRegistry.All);

            if (plugins != null)
            {
                foreach (var plugin in plugins)
                {
                    var contributor = plugin as ISettingsContributor;
                    if (contributor == null) continue;

                    var defs = contributor.GetSettings();
                    if (defs != null) _definitions.AddRange(defs);
                }
            }
        }

        public IReadOnlyList<SettingDefinition> Definitions => _definitions;

        public string Get(string key)
        {
            var definition = _definitions.FirstOrDefault(d => d.Key == key);
            var fallback = definition?.DefaultValue;

            if (_config.Settings != null && _config.Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            return fallback;
        }

        public void Set(string key, string value)
        {
            if (_config.Settings == null)
            {
                _config.Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            _config.Settings[key] = value;
        }

        /// <summary>删除所有以指定前缀开头的设置键（如 formatChoice.*）。</summary>
        public void ClearKeysWithPrefix(string prefix)
        {
            if (_config.Settings == null) return;
            var keys = _config.Settings.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keys)
            {
                _config.Settings.Remove(key);
            }
        }

        public void Save()
        {
            _store.Save(_config);
        }
    }
}
