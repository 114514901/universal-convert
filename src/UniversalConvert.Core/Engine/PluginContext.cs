using System;
using System.Collections.Generic;
using System.IO;
using UniversalConvert.Core.Config;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Engine
{
    /// <summary>
    /// IPluginContext 默认实现。工具定位优先级：配置文件 > 安装目录 > 系统 PATH。
    /// </summary>
    public sealed class PluginContext : IPluginContext
    {
        private readonly AppConfig _config;
        private readonly Action<string> _log;

        public PluginContext(AppConfig config, Action<string> log = null)
        {
            _config = config ?? new AppConfig();
            _log = log ?? (_ => { });
        }

        public string DataDirectory
        {
            get
            {
                var dir = Path.Combine(ConfigStore.ConfigDirectory, "data");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public string FindTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return null;

            // 1. 配置文件显式指定
            string path;
            if (_config.ToolPaths != null && _config.ToolPaths.TryGetValue(toolName, out path))
            {
                if (File.Exists(path)) return path;
            }

            // 2. 安装目录内
            if (!string.IsNullOrEmpty(_config.InstallDirectory))
            {
                var local = Path.Combine(_config.InstallDirectory, "tools", toolName + ".exe");
                if (File.Exists(local)) return local;
            }

            // 3. 系统 PATH
            var fromPath = FindOnPath(toolName);
            if (fromPath != null) return fromPath;

            return null;
        }

        public void Log(string message)
        {
            _log(message);
        }

        private static string FindOnPath(string toolName)
        {
            var name = toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? toolName
                : toolName + ".exe";

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // 忽略非法路径段
                }
            }
            return null;
        }
    }
}
