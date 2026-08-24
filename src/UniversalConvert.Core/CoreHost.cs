using System;
using System.Collections.Generic;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Engine;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core
{
    /// <summary>
    /// 应用宿主：统一完成 配置加载 -> 插件扫描 -> 注入上下文 -> 构建注册表/引擎 的流程。
    /// 主程序、右键菜单、安装器都复用它，保证三处看到的能力一致。
    /// </summary>
    public sealed class CoreHost
    {
        private readonly List<PluginLoadError> _loadErrors = new List<PluginLoadError>();

        public AppConfig Config { get; }
        public IList<IConverterPlugin> Plugins { get; }
        public FormatRegistry Registry { get; }
        public ConversionEngine Engine { get; }

        /// <summary>插件加载/初始化过程中的错误（供扩展管理器展示）。</summary>
        public IReadOnlyList<PluginLoadError> LoadErrors => _loadErrors;

        public CoreHost(AppConfig config, string pluginsDirectory, Action<string> log = null)
        {
            Config = config ?? new AppConfig();
            log = log ?? (_ => { });

            var loader = new PluginLoader(log);

            Log.Info($"扫描内置插件目录: {pluginsDirectory}");
            var builtinPlugins = loader.Load(pluginsDirectory);
            _loadErrors.AddRange(loader.Errors);

            Log.Info($"扫描用户插件目录: {ConfigStore.UserPluginsDirectory}");
            var userPlugins = loader.Load(ConfigStore.UserPluginsDirectory);
            _loadErrors.AddRange(loader.Errors);

            // 用户目录优先：同 Id 时用户安装的插件覆盖内置插件
            Plugins = MergePlugins(userPlugins, builtinPlugins);
            Log.Info($"共加载 {Plugins.Count} 个插件");

            var context = new PluginContext(Config, log);
            foreach (var plugin in Plugins)
            {
                try
                {
                    plugin.Initialize(context);
                }
                catch (Exception ex)
                {
                    Log.Error($"插件初始化失败 '{plugin.Id}': {ex.Message}");
                    log($"Failed to initialize plugin '{plugin.Id}': {ex.Message}");
                    _loadErrors.Add(new PluginLoadError { File = plugin.Id, Message = ex.Message });
                }
            }

            Registry = new FormatRegistry(Plugins);
            Engine = new ConversionEngine(Plugins);
        }

        private static IList<IConverterPlugin> MergePlugins(
            IList<IConverterPlugin> first, IList<IConverterPlugin> second)
        {
            var result = new List<IConverterPlugin>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var plugin in first)
            {
                if (!string.IsNullOrEmpty(plugin.Id) && seen.Add(plugin.Id))
                    result.Add(plugin);
            }
            foreach (var plugin in second)
            {
                if (!string.IsNullOrEmpty(plugin.Id) && seen.Add(plugin.Id))
                    result.Add(plugin);
            }

            return result;
        }
    }
}
