using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Core.Process
{
    /// <summary>
    /// 面向"外部命令行工具"类插件的基类，封装了找工具、构建输出路径、跑进程、结果封装的通用逻辑。
    /// 派生类只需实现 BuildArguments 和（可选）解析进度的钩子，即可快速接入一种新转换器。
    /// </summary>
    public abstract class ExternalToolConverterBase : IConverterPlugin
    {
        protected IPluginContext Context { get; private set; }

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }

        /// <summary>外部工具名（用于 FindTool），如 "ffmpeg"。</summary>
        protected abstract string ToolName { get; }

        public virtual void Initialize(IPluginContext context)
        {
            Context = context;
        }

        public abstract IList<ConversionCapability> GetCapabilities();

        public virtual bool IsToolAvailable()
        {
            return Context != null && !string.IsNullOrEmpty(Context.FindTool(ToolName));
        }

        public virtual async Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress> progress,
            CancellationToken cancellationToken)
        {
            var started = DateTime.UtcNow;

            var tool = Context.FindTool(ToolName);
            if (string.IsNullOrEmpty(tool))
            {
                return ConversionResult.Failed($"未找到工具 '{ToolName}'", DateTime.UtcNow - started);
            }

            if (!File.Exists(request.InputPath))
            {
                return ConversionResult.Failed($"输入文件不存在：{request.InputPath}", DateTime.UtcNow - started);
            }

            var outputPath = ResolveOutputPath(request);
            var args = BuildArguments(request, outputPath);

            progress?.Report(new ConversionProgress(ConversionStage.Running, 0, "开始转换..."));

            var runResult = await Task.Run(() => ProcessRunner.Run(
                tool,
                args,
                cancellationToken,
                line => OnOutputLine(line, request, progress)
            ), cancellationToken).ConfigureAwait(false);

            var elapsed = DateTime.UtcNow - started;

            if (cancellationToken.IsCancellationRequested)
            {
                return ConversionResult.Failed("转换已取消", elapsed);
            }

            if (runResult.ExitCode != 0)
            {
                var detail = string.IsNullOrEmpty(runResult.StandardError)
                    ? runResult.StandardOutput
                    : runResult.StandardError;
                return ConversionResult.Failed(
                    $"工具 '{ToolName}' 返回错误码 {runResult.ExitCode}：{Truncate(detail)}", elapsed);
            }

            if (!File.Exists(outputPath))
            {
                return ConversionResult.Failed("转换未产生输出文件", elapsed);
            }

            return ConversionResult.Succeeded(outputPath, elapsed);
        }

        /// <summary>构建输出路径：未显式指定时，替换源文件扩展名。</summary>
        protected virtual string ResolveOutputPath(ConversionRequest request)
        {
            if (!string.IsNullOrEmpty(request.OutputPath)) return request.OutputPath;

            var dir = Path.GetDirectoryName(request.InputPath);
            var name = Path.GetFileNameWithoutExtension(request.InputPath);
            var ext = request.OutputExtension;
            if (string.IsNullOrEmpty(ext)) return Path.Combine(dir ?? "", name + ".out");
            if (!ext.StartsWith(".")) ext = "." + ext;
            return Path.Combine(dir ?? "", name + ext);
        }

        /// <summary>子类实现：根据请求生成命令行参数。</summary>
        protected abstract string BuildArguments(ConversionRequest request, string outputPath);

        /// <summary>子类可覆盖：解析外部工具输出以报告精确进度。默认不解析。</summary>
        protected virtual void OnOutputLine(string line, ConversionRequest request, IProgress<ConversionProgress> progress)
        {
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(无输出)";
            const int max = 500;
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}
