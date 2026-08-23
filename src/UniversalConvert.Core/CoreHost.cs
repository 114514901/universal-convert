using System;
using System.Collections.Generic;
using UniversalConvert.Core.Config;
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
        public AppConfig Config { get; }
        public IList<IConverterPlugin> Plugins { get; }
        public FormatRegistry Registry { get; }
        public ConversionEngine Engine { get; }

        public CoreHost(AppConfig config, string pluginsDirectory, Action<string> log = null)
        {
            Config = config ?? new AppConfig();
            log = log ?? (_ => { });

            var loader = new PluginLoader(log);
            Plugins = loader.Load(pluginsDirectory);

            var context = new PluginContext(Config, log);
            foreach (var plugin in Plugins)
            {
                try
                {
                    plugin.Initialize(context);
                }
                catch (Exception ex)
                {
                    log($"Failed to initialize plugin '{plugin.Id}': {ex.Message}");
                }
            }

            Registry = new FormatRegistry(Plugins);
            Engine = new ConversionEngine(Plugins);
        }
    }
}
