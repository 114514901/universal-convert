using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Plugin.Qmc
{
    /// <summary>
    /// QQ 音乐 QMC 解密插件（纯 C#）。
    /// 支持 .qmc0/.qmc3/.qmcflac/.qmcogg（老格式），算法移植自 presburger/qmc-decoder
    /// （Anti 996 + MIT 许可证，详见同目录 LICENSE-NOTICE）。
    /// 流程：先解密成原格式（由解密后的文件头识别），目标格式不同则用 FFmpeg 转码。
    /// </summary>
    public sealed class QmcPlugin : IConverterPlugin
    {
        // QMC 老格式的固定密钥表（8 行 × 7 列），解密时蛇形遍历生成密钥流
        private static readonly byte[,] SeedMap = new byte[8, 7]
        {
            { 0x4a, 0xd6, 0xca, 0x90, 0x67, 0xf7, 0x52 },
            { 0x5e, 0x95, 0x23, 0x9f, 0x13, 0x11, 0x7e },
            { 0x47, 0x74, 0x3d, 0x90, 0xaa, 0x3f, 0x51 },
            { 0xc6, 0x09, 0xd5, 0x9f, 0xfa, 0x66, 0xf9 },
            { 0xf3, 0xd6, 0xa1, 0x90, 0xa0, 0xf7, 0xf0 },
            { 0x1d, 0x95, 0xde, 0x9f, 0x84, 0x11, 0xf4 },
            { 0x0e, 0x74, 0xbb, 0x90, 0xbc, 0x3f, 0x92 },
            { 0x00, 0x09, 0x5b, 0x9f, 0x62, 0x66, 0xa1 }
        };

        private static readonly string[] AudioFormats =
            { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".opus" };

        private IPluginContext _context;

        public string Id => "com.universalconvert.qmc";
        public string Name => "QMC 解密";
        public string Description => "QQ 音乐 .qmc0/.qmc3/.qmcflac/.qmcogg 解密（可选转码为其它音频格式）";
        public string Version => "1.0.0";
        public string MinAppVersion => null;
        public string MaxAppVersion => null;

        public void Initialize(IPluginContext context)
        {
            _context = context;
        }

        public bool IsToolAvailable()
        {
            return true;
        }

        public IList<ConversionCapability> GetCapabilities()
        {
            var outputs = AudioFormats.Select(AudioOutput).ToList();
            return new List<ConversionCapability>
            {
                new ConversionCapability { InputExtension = ".qmc0", InputDisplayName = "QQ 音乐 QMC0", Outputs = outputs },
                new ConversionCapability { InputExtension = ".qmc3", InputDisplayName = "QQ 音乐 QMC3", Outputs = outputs },
                new ConversionCapability { InputExtension = ".qmcflac", InputDisplayName = "QQ 音乐 QMCFLAC", Outputs = outputs },
                new ConversionCapability { InputExtension = ".qmcogg", InputDisplayName = "QQ 音乐 QMCOGG", Outputs = outputs }
            };
        }

        public Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var started = DateTime.UtcNow;
                try
                {
                    var outputPath = ConvertCore(request, progress, cancellationToken);
                    return ConversionResult.Succeeded(outputPath, DateTime.UtcNow - started);
                }
                catch (OperationCanceledException)
                {
                    return ConversionResult.Failed("转换已取消", DateTime.UtcNow - started);
                }
                catch (Exception ex)
                {
                    return ConversionResult.Failed(ex.Message, DateTime.UtcNow - started, ex.ToString());
                }
            }, cancellationToken);
        }

        private string ConvertCore(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
        {
            var input = request.InputPath;
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("输入文件不存在：" + input);
            }

            var targetExt = NormalizeExt(request.OutputExtension);
            if (string.IsNullOrEmpty(targetExt)) targetExt = "mp3";

            var tempPath = Path.Combine(Path.GetTempPath(), "uc_qmc_" + Guid.NewGuid().ToString("N") + ".tmp");
            string originalFormat;

            try
            {
                originalFormat = DecryptTo(input, tempPath, progress, ct);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }

            var outputPath = ResolveOutputPath(request, targetExt);

            try
            {
                if (string.Equals(originalFormat, targetExt, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ConversionProgress(ConversionStage.Finalizing, 100, "写入输出..."));
                    File.Copy(tempPath, outputPath, true);
                }
                else
                {
                    var ffmpeg = _context?.FindTool("ffmpeg");
                    if (string.IsNullOrEmpty(ffmpeg))
                    {
                        throw new InvalidOperationException("转码需要 FFmpeg，但未找到。请安装 FFmpeg 或将其放入 tools 目录。");
                    }

                    progress?.Report(new ConversionProgress(ConversionStage.Running, -1, "转码中..."));
                    var run = ProcessRunner.Run(ffmpeg, BuildFfmpegArgs(tempPath, outputPath, request.Options), ct);

                    if (run.ExitCode != 0)
                    {
                        throw new InvalidOperationException("FFmpeg 转码失败（错误码 " + run.ExitCode + "）：" + (run.StandardError ?? string.Empty));
                    }
                }

                return outputPath;
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private string DecryptTo(string input, string outputPath, IProgress<ConversionProgress> progress, CancellationToken ct)
        {
            progress?.Report(new ConversionProgress(ConversionStage.Starting, 0, "解密 QMC 文件..."));

            int x = -1, y = 8, dx = 1, index = -1;

            using (var fs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                long total = fs.Length;
                long processed = 0;
                byte[] head = null;
                var buffer = new byte[0x8000];
                int n;

                while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    for (int i = 0; i < n; i++)
                    {
                        buffer[i] ^= NextMask(ref x, ref y, ref dx, ref index);
                    }

                    if (head == null)
                    {
                        int take = Math.Min(12, n);
                        head = new byte[take];
                        Array.Copy(buffer, 0, head, 0, take);
                    }

                    outStream.Write(buffer, 0, n);
                    processed += n;

                    if (total > 0)
                    {
                        var pct = Math.Min(100, processed * 100.0 / total);
                        progress?.Report(new ConversionProgress(ConversionStage.Running, pct, "解密中..."));
                    }
                }
            }

            return head != null ? DetectFormat(head) : "mp3";
        }

        private static byte NextMask(ref int x, ref int y, ref int dx, ref int index)
        {
            byte ret;
            while (true)
            {
                index++;
                if (x < 0)
                {
                    dx = 1;
                    y = (8 - y) % 8;
                    ret = 0xc3;
                }
                else if (x > 6)
                {
                    dx = -1;
                    y = 7 - y;
                    ret = 0xd8;
                }
                else
                {
                    ret = SeedMap[y, x];
                }

                x += dx;

                if (index == 0x8000 || (index > 0x8000 && (index + 1) % 0x8000 == 0))
                {
                    continue; // 每 0x8000 字节丢弃一个掩码
                }
                return ret;
            }
        }

        private string ResolveOutputPath(ConversionRequest request, string targetExt)
        {
            if (!string.IsNullOrEmpty(request.OutputPath))
            {
                var dir = Path.GetDirectoryName(request.OutputPath);
                var name = Path.GetFileNameWithoutExtension(request.OutputPath);
                return Path.Combine(dir ?? string.Empty, name + "." + targetExt);
            }

            var inputDir = Path.GetDirectoryName(request.InputPath);
            var inputName = Path.GetFileNameWithoutExtension(request.InputPath);
            return Path.Combine(inputDir ?? string.Empty, inputName + "." + targetExt);
        }

        private static string BuildFfmpegArgs(string input, string output, IDictionary<string, string> options)
        {
            var sb = new StringBuilder();
            sb.Append("-y -hide_banner -loglevel error -i ").Append(ProcessRunner.Quote(input));
            sb.Append(" -vn");

            string value;
            if (options != null && options.TryGetValue("audioBitrate", out value) && !string.IsNullOrEmpty(value))
            {
                sb.Append(" -b:a ").Append(value);
            }
            if (options != null && options.TryGetValue("sampleRate", out value) && !string.IsNullOrEmpty(value))
            {
                sb.Append(" -ar ").Append(value);
            }

            sb.Append(' ').Append(ProcessRunner.Quote(output));
            return sb.ToString();
        }

        private static string DetectFormat(byte[] head)
        {
            if (head.Length >= 4)
            {
                if (head[0] == 'I' && head[1] == 'D' && head[2] == '3') return "mp3";
                if (head[0] == 'f' && head[1] == 'L' && head[2] == 'a' && head[3] == 'C') return "flac";
                if (head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S') return "ogg";
                if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F') return "wav";
            }
            if (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return "mp3";
            if (head.Length >= 12 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p') return "m4a";
            return "mp3";
        }

        private static string NormalizeExt(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return string.Empty;
            return extension.Trim().TrimStart('.').ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 忽略清理失败
            }
        }

        private static OutputFormat AudioOutput(string ext)
        {
            return new OutputFormat
            {
                Extension = ext,
                DisplayName = ext.TrimStart('.').ToUpperInvariant(),
                Options = new List<OptionDefinition>
                {
                    EnumOption("audioBitrate", "音频码率", "192k",
                        Choice("", "原始"),
                        Choice("96k", "96 kbps"),
                        Choice("128k", "128 kbps"),
                        Choice("160k", "160 kbps"),
                        Choice("192k", "192 kbps"),
                        Choice("256k", "256 kbps"),
                        Choice("320k", "320 kbps")),
                    EnumOption("sampleRate", "采样率", "",
                        Choice("", "原始"),
                        Choice("44100", "44100 Hz"),
                        Choice("48000", "48000 Hz"),
                        Choice("88200", "88200 Hz"),
                        Choice("96000", "96000 Hz"))
                },
                Presets = new List<ConversionPreset>
                {
                    Preset("320 kbps", "audioBitrate=320k"),
                    Preset("192 kbps", "audioBitrate=192k"),
                    Preset("128 kbps", "audioBitrate=128k")
                }
            };
        }

        private static OptionDefinition EnumOption(string key, string label, string defaultValue, params OptionChoice[] choices)
        {
            return new OptionDefinition
            {
                Key = key,
                Label = label,
                Type = OptionType.Enum,
                DefaultValue = defaultValue,
                Choices = choices.ToList()
            };
        }

        private static OptionChoice Choice(string value, string label)
        {
            return new OptionChoice { Value = value, Label = label };
        }

        private static ConversionPreset Preset(string name, params string[] keyValues)
        {
            var preset = new ConversionPreset { Name = name };
            foreach (var kv in keyValues)
            {
                var i = kv.IndexOf('=');
                if (i > 0)
                {
                    preset.Options[kv.Substring(0, i)] = kv.Substring(i + 1);
                }
            }
            return preset;
        }
    }
}
