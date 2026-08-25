using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UniversalConvert.Core.Diagnostics;

namespace UniversalConvert.Core.Plugins
{
    /// <summary>
    /// 从目录扫描并加载插件 DLL。只做反射实例化，不执行任何转换。
    /// 加载过程中的错误收集到 Errors 供界面展示。
    /// </summary>
    public sealed class PluginLoader
    {
        private readonly Action<string> _log;
        private readonly List<PluginLoadError> _errors = new List<PluginLoadError>();
        private readonly SemVersion _appVersion;

        /// <summary>
        /// appVersion：当前应用版本（SemVer）。不传时尝试从本程序集读取
        /// InformationalVersion（仓库统一由 Directory.Build.props 注入，与 App 一致）。
        /// 用于在加载时强制校验插件的 MinAppVersion。
        /// </summary>
        public PluginLoader(Action<string> log = null, string appVersion = null)
        {
            _log = log ?? (_ => { });
            _appVersion = SemVersion.Parse(appVersion ?? ReadInformationalVersion());
        }

        private static string ReadInformationalVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>本次加载过程中收集到的错误（程序集/类型加载失败）。</summary>
        public IReadOnlyList<PluginLoadError> Errors => _errors;

        /// <summary>
        /// 加载指定目录下所有 DLL 中实现 IConverterPlugin 的类型。
        /// 单个 DLL 加载失败不影响其它插件。
        /// </summary>
        public IList<IConverterPlugin> Load(string pluginsDirectory)
        {
            var result = new List<IConverterPlugin>();
            _errors.Clear();

            if (string.IsNullOrEmpty(pluginsDirectory) || !Directory.Exists(pluginsDirectory))
            {
                Log.Debug("插件目录不存在: " + pluginsDirectory);
                _log("Plugins directory not found: " + pluginsDirectory);
                return result;
            }

            // 顶层 + 一层子目录（用户插件每个一个子目录，如 plugins\Pandoc\PandocPlugin.dll）
            var dlls = new List<string>();
            dlls.AddRange(Directory.GetFiles(pluginsDirectory, "*.dll"));
            foreach (var subdir in Directory.GetDirectories(pluginsDirectory))
            {
                dlls.AddRange(Directory.GetFiles(subdir, "*.dll"));
            }

            foreach (var dll in dlls)
            {
                try
                {
                    result.AddRange(LoadFromAssembly(dll));
                }
                catch (Exception ex)
                {
                    Log.Error($"加载插件程序集失败 '{dll}': {ex.Message}");
                    _log($"Failed to load plugin assembly '{dll}': {ex.Message}");
                    _errors.Add(new PluginLoadError { File = dll, Message = ex.Message });
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
                    Log.Error($"实例化插件类型失败 '{type.FullName}': {ex.Message}");
                    _log($"Failed to instantiate plugin type '{type.FullName}': {ex.Message}");
                    _errors.Add(new PluginLoadError { File = type.FullName, Message = ex.Message });
                    continue;
                }

                Log.Debug($"已加载插件 '{plugin.Id}' ({dllPath})");
                _log($"Loaded plugin '{plugin.Id}' from '{dllPath}'");

                // 加载时强制校验 MinAppVersion：应用版本低于插件要求时跳过加载，
                // 记录友好错误（而不是等插件功能运行时报反射/接口错误）。
                if (_appVersion != null && !string.IsNullOrEmpty(plugin.MinAppVersion))
                {
                    var min = SemVersion.Parse(plugin.MinAppVersion);
                    if (min != null && _appVersion.CompareTo(min) < 0)
                    {
                        var message = $"插件要求应用版本 >= {min}，当前应用版本 {_appVersion}，已跳过加载";
                        Log.Error($"插件 '{plugin.Id}' 要求应用版本 >= {min}，当前 {_appVersion}，已跳过");
                        _log($"Plugin '{plugin.Id}' requires app >= {min}, current {_appVersion}; skipped");
                        _errors.Add(new PluginLoadError { File = plugin.Id, Message = message });
                        continue;
                    }
                }

                yield return plugin;
            }
        }
    }
}
