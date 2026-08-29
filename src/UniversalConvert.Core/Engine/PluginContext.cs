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

            // 1. 配置文件显式指定（用户明确配置，信任其选择）
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

            // 注：不再回退系统 PATH——PATH 中的同名工具可能为旧版/带漏洞版本
            // （如 ffmpeg < 8.1.2 的 CVE-2026-8461 越界写入），静默使用存在安全风险；
            // 缺失时明确报「未找到所需工具」，由用户显式配置路径。

            return null;
        }

        public string GetSetting(string key, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (_config.Settings != null && _config.Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            return defaultValue;
        }

        public void Log(string message)
        {
            _log(message);
        }
    }
}
