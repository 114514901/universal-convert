using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 从目录扫描并加载插件 DLL。只做反射实例化，不执行任何转换。
    /// </summary>
    public sealed class PluginLoader
    {
        private readonly Action<string> _log;

        public PluginLoader(Action<string> log = null)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// 加载指定目录下所有 DLL 中实现 IConverterPlugin 的类型。
        /// 单个 DLL 加载失败不影响其它插件。
        /// </summary>
        public IList<IConverterPlugin> Load(string pluginsDirectory)
        {
            var result = new List<IConverterPlugin>();
            if (string.IsNullOrEmpty(pluginsDirectory) || !Directory.Exists(pluginsDirectory))
            {
                _log("Plugins directory not found: " + pluginsDirectory);
                return result;
            }

            foreach (var dll in Directory.GetFiles(pluginsDirectory, "*.dll"))
            {
                try
                {
                    result.AddRange(LoadFromAssembly(dll));
                }
                catch (Exception ex)
                {
                    _log($"Failed to load plugin assembly '{dll}': {ex.Message}");
                }
            }

            return result;
        }

        private IEnumerable<IConverterPlugin> LoadFromAssembly(string dllPath)
        {
            var assembly = Assembly.LoadFrom(dllPath);
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IConverterPlugin).IsAssignableFrom(t)
                            && t.IsClass
                            && !t.IsAbstract);

            foreach (var type in pluginTypes)
            {
                IConverterPlugin plugin;
                try
                {
                    plugin = (IConverterPlugin)Activator.CreateInstance(type);
                }
                catch (Exception ex)
                {
                    _log($"Failed to instantiate plugin type '{type.FullName}': {ex.Message}");
                    continue;
                }

                _log($"Loaded plugin '{plugin.Id}' from '{dllPath}'");
                yield return plugin;
            }
        }
    }
}
