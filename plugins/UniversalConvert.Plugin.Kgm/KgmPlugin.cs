using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Plugin.Kgm
{
    /// <summary>
    /// 酷狗音乐 .kgm/.kgma 解密插件（纯 C#，密钥表随安装包分发）。
    /// 解密算法移植自 ghtz08/kugou-kgm-decoder（Anti 996 License，详见同目录 LICENSE）。
    /// 流程：先解密成原格式（由解密后的文件头识别），目标格式不同则用 FFmpeg 转码。
    /// </summary>
    public sealed class KgmPlugin : IConverterPlugin
    {
        private const int HeaderLen = 1024;
        private const string KeyFileName = "kugou_key.dat";

        private static readonly byte[] MagicHeader =
        {
            0x7c, 0xd5, 0x32, 0xeb, 0x86, 0x02, 0x7f, 0x4b, 0xa8, 0xaf, 0xa6, 0x8e, 0x0f, 0xff, 0x99, 0x14,
            0x00, 0x04, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        };

        private static readonly byte[] PubKeyMend =
        {
            0xB8, 0xD5, 0x3D, 0xB2, 0xE9, 0xAF, 0x78, 0x8C, 0x83, 0x33, 0x71, 0x51,
            0x76, 0xA0, 0xCD, 0x37, 0x2F, 0x3E, 0x35, 0x8D, 0xA9, 0xBE, 0x98, 0xB7,
            0xE7, 0x8C, 0x22, 0xCE, 0x5A, 0x61, 0xDF, 0x68, 0x69, 0x89, 0xFE, 0xA5,
            0xB6, 0xDE, 0xA9, 0x77, 0xFC, 0xC8, 0xBD, 0xBD, 0xE5, 0x6D, 0x3E, 0x5A,
            0x36, 0xEF, 0x69, 0x4E, 0xBE, 0xE1, 0xE9, 0x66, 0x1C, 0xF3, 0xD9, 0x02,
            0xB6, 0xF2, 0x12, 0x9B, 0x44, 0xD0, 0x6F, 0xB9, 0x35, 0x89, 0xB6, 0x46,
            0x6D, 0x73, 0x82, 0x06, 0x69, 0xC1, 0xED, 0xD7, 0x85, 0xC2, 0x30, 0xDF,
            0xA2, 0x62, 0xBE, 0x79, 0x2D, 0x62, 0x62, 0x3D, 0x0D, 0x7E, 0xBE, 0x48,
            0x89, 0x23, 0x02, 0xA0, 0xE4, 0xD5, 0x75, 0x51, 0x32, 0x02, 0x53, 0xFD,
            0x16, 0x3A, 0x21, 0x3B, 0x16, 0x0F, 0xC3, 0xB2, 0xBB, 0xB3, 0xE2, 0xBA,
            0x3A, 0x3D, 0x13, 0xEC, 0xF6, 0x01, 0x45, 0x84, 0xA5, 0x70, 0x0F, 0x93,
            0x49, 0x0C, 0x64, 0xCD, 0x31, 0xD5, 0xCC, 0x4C, 0x07, 0x01, 0x9E, 0x00,
            0x1A, 0x23, 0x90, 0xBF, 0x88, 0x1E, 0x3B, 0xAB, 0xA6, 0x3E, 0xC4, 0x73,
            0x47, 0x10, 0x7E, 0x3B, 0x5E, 0xBC, 0xE3, 0x00, 0x84, 0xFF, 0x09, 0xD4,
            0xE0, 0x89, 0x0F, 0x5B, 0x58, 0x70, 0x4F, 0xFB, 0x65, 0xD8, 0x5C, 0x53,
            0x1B, 0xD3, 0xC8, 0xC6, 0xBF, 0xEF, 0x98, 0xB0, 0x50, 0x4F, 0x0F, 0xEA,
            0xE5, 0x83, 0x58, 0x8C, 0x28, 0x2C, 0x84, 0x67, 0xCD, 0xD0, 0x9E, 0x47,
            0xDB, 0x27, 0x50, 0xCA, 0xF4, 0x63, 0x63, 0xE8, 0x97, 0x7F, 0x1B, 0x4B,
            0x0C, 0xC2, 0xC1, 0x21, 0x4C, 0xCC, 0x58, 0xF5, 0x94, 0x52, 0xA3, 0xF3,
            0xD3, 0xE0, 0x68, 0xF4, 0x00, 0x23, 0xF3, 0x5E, 0x0A, 0x7B, 0x93, 0xDD,
            0xAB, 0x12, 0xB2, 0x13, 0xE8, 0x84, 0xD7, 0xA7, 0x9F, 0x0F, 0x32, 0x4C,
            0x55, 0x1D, 0x04, 0x36, 0x52, 0xDC, 0x03, 0xF3, 0xF9, 0x4E, 0x42, 0xE9,
            0x3D, 0x61, 0xEF, 0x7C, 0xB6, 0xB3, 0x93, 0x50,
        };

        private static readonly string[] AudioFormats =
            { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".opus" };

        private static byte[] _keyTable;
        private IPluginContext _context;

        public string Id => "com.universalconvert.kgm";
        public string Name => "KGM 解密";
        public string Description => "酷狗音乐 .kgm/.kgma 解密（可选转码为其它音频格式）";

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
                new ConversionCapability
                {
                    InputExtension = ".kgm",
                    InputDisplayName = "酷狗音乐 KGM",
                    Outputs = outputs
                },
                new ConversionCapability
                {
                    InputExtension = ".kgma",
                    InputDisplayName = "酷狗音乐 KGMA",
                    Outputs = outputs
                }
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

            var tempPath = Path.Combine(Path.GetTempPath(), "uc_kgm_" + Guid.NewGuid().ToString("N") + ".tmp");
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
            progress?.Report(new ConversionProgress(ConversionStage.Starting, 0, "解析 KGM 文件..."));

            using (var fs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                var header = reader.ReadBytes(HeaderLen);
                if (header.Length != HeaderLen || !header.Take(MagicHeader.Length).SequenceEqual(MagicHeader))
                {
                    throw new InvalidDataException("不是有效的 KGM 文件");
                }

                var ownKey = new byte[17];
                Array.Copy(header, 0x1C, ownKey, 0, 16);

                var keyTable = GetKeyTable();
                long maxAudio = (long)keyTable.Length * 16;
                if (fs.Length - HeaderLen > maxAudio)
                {
                    throw new InvalidDataException("文件过大，超出密钥表支持范围");
                }

                byte[] head = null;
                long total = fs.Length - fs.Position;
                long processed = 0;

                using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    var buffer = new byte[0x8000];
                    int n;
                    while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        for (int j = 0; j < n; j++)
                        {
                            long i = processed + j;
                            int med = ownKey[(int)(i % 17)] ^ buffer[j];
                            med ^= (med & 0x0F) << 4;
                            int msk = PubKeyMend[(int)(i % 272)] ^ keyTable[(int)(i / 16)];
                            msk ^= (msk & 0x0F) << 4;
                            buffer[j] = (byte)(med ^ msk);
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

        private static byte[] GetKeyTable()
        {
            if (_keyTable == null)
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                var path = Path.Combine(dir, KeyFileName);
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException("找不到 KGM 密钥表文件 " + KeyFileName);
                }
                _keyTable = File.ReadAllBytes(path);
            }
            return _keyTable;
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
