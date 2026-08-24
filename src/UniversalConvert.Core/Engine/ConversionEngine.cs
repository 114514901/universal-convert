using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Engine
{
    /// <summary>
    /// 转换引擎：负责按插件 Id 找到插件并执行转换，统一错误处理与进度中转。
    /// </summary>
    public sealed class ConversionEngine
    {
        private readonly IDictionary<string, IConverterPlugin> _plugins;

        public ConversionEngine(IEnumerable<IConverterPlugin> plugins)
        {
            _plugins = new Dictionary<string, IConverterPlugin>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in plugins)
            {
                if (!string.IsNullOrEmpty(plugin.Id))
                {
                    _plugins[plugin.Id] = plugin;
                }
            }
        }

        public IReadOnlyList<IConverterPlugin> Plugins => _plugins.Values.ToList();

        public IConverterPlugin GetPlugin(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return null;
            IConverterPlugin plugin;
            return _plugins.TryGetValue(pluginId, out plugin) ? plugin : null;
        }

        /// <summary>执行转换。找不到插件或工具不可用时返回失败结果而不抛异常。</summary>
        public async Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress> progress,
            CancellationToken cancellationToken)
        {
            var started = DateTime.UtcNow;
            progress?.Report(new ConversionProgress(ConversionStage.Starting, 0, "准备转换..."));
            Log.Info($"转换开始: {request.InputPath} -> .{request.OutputExtension} (插件 {request.PluginId})");

            var plugin = GetPlugin(request.PluginId);
            if (plugin == null)
            {
                Log.Error($"未找到插件 '{request.PluginId}'");
                return ConversionResult.Failed($"未找到插件 '{request.PluginId}'", Elapsed(started));
            }

            if (!plugin.IsToolAvailable())
            {
                Log.Warn($"插件 '{plugin.Name}' 所需的外部工具不可用");
                return ConversionResult.Failed($"插件 '{plugin.Name}' 所需的外部工具不可用", Elapsed(started));
            }

            try
            {
                var result = await plugin.ConvertAsync(request, progress, cancellationToken).ConfigureAwait(false);
                progress?.Report(new ConversionProgress(
                    result.Success ? ConversionStage.Completed : ConversionStage.Finalizing,
                    result.Success ? 100 : -1,
                    result.Success ? "完成" : result.ErrorMessage));
                if (result.Success)
                {
                    Log.Info($"转换完成: {result.OutputPath} (用时 {result.Duration.TotalSeconds:F1} 秒)");
                }
                else
                {
                    Log.Error($"转换失败: {result.ErrorMessage}");
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                Log.Info("转换已取消");
                return ConversionResult.Failed("转换已取消", Elapsed(started));
            }
            catch (Exception ex)
            {
                Log.Error("转换异常: " + ex);
                return ConversionResult.Failed(ex.Message, Elapsed(started));
            }
        }

        private static TimeSpan Elapsed(DateTime started)
        {
            return DateTime.UtcNow - started;
        }
    }
}
