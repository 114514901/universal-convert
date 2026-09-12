using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Models;
using UniversalConvert.Core.Plugins;
using UniversalConvert.Core.Process;

namespace UniversalConvert.Plugin.Ncm
{
    /// <summary>
    /// 网易云音乐 .ncm 插件（纯 C# 解密 + 可选 FFmpeg 转码）。
    /// 解密逻辑移植自 hkylin/ncmdumpGUI（MIT License, Copyright (c) 2018 kpali）。
    /// 流程：先解密成文件原格式（mp3/flac），若目标格式与原格式不同，再用 FFmpeg 转码。
    /// </summary>
    public sealed class NcmPlugin : IConverterPlugin, IPreviewProvider
    {
        private static readonly byte[] Magic = { 0x43, 0x54, 0x45, 0x4E, 0x46, 0x44, 0x41, 0x4D }; // "CTENFDAM"
        private static readonly byte[] CoreKey = { 0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57 };
        private static readonly byte[] ModifyKey = { 0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28 };
        private const uint MaxChunkSize = 100 * 1024 * 1024;

        private static readonly string[] AudioFormats =
            { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".opus" };

        private IPluginContext _context;

        public string Id => "com.universalconvert.ncm";
        public string Name => "NCM 解密";
        public string Description => "网易云音乐 .ncm 解密（可选转码为其它音频格式）";
        public string Version => "1.1.0";
        public string MinAppVersion => null;
        public string MaxAppVersion => null;



        // ---------- IPreviewProvider（预览 = 临时解密后播放，关闭时由调用方清理） ----------

        public IList<string> SupportedPreviewExtensions => new List<string> { ".ncm" };

        public async Task<string> RenderPreviewAsync(string inputPath, CancellationToken cancellationToken)
        {
            try
            {
                var temp = Path.Combine(Path.GetTempPath(), "uc_prev_" + Guid.NewGuid().ToString("N"));
                byte[] coverData;
                var fmt = DecryptTo(inputPath, temp, null, cancellationToken, out coverData);
                if (string.IsNullOrEmpty(fmt) || !File.Exists(temp))
                {
                    TryDelete(temp);
                    return null;
                }
                var final = temp + "." + fmt.TrimStart('.');
                File.Move(temp, final);

                // 有内嵌封面且目标格式支持：用 FFmpeg 封装补上封面，播放器预览时能显示
                if (coverData != null && coverData.Length > 0 && SupportsCoverArt(fmt))
                {
                    var withCover = TryAttachCover(final, fmt, coverData);
                    if (!string.IsNullOrEmpty(withCover))
                    {
                        TryDelete(final);
                        return withCover;
                    }
                }
                return final;
            }
            catch
            {
                return null;
            }
        }

        public void Initialize(IPluginContext context)
        {
            _context = context;
        }

        public bool IsToolAvailable()
        {
            return true; // 解密始终可用；转码时才需要 FFmpeg
        }

        public bool IsUntested => false;

        public IList<ConversionCapability> GetCapabilities()
        {
            return new List<ConversionCapability>
            {
                new ConversionCapability
                {
                    InputExtension = ".ncm",
                    InputDisplayName = "网易云音乐 NCM",
                    Outputs = AudioFormats.Select(AudioOutput).ToList()
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

            // 先解密到临时文件，得到原格式 + 内嵌封面
            var tempPath = Path.Combine(Path.GetTempPath(), "uc_ncm_" + Guid.NewGuid().ToString("N") + ".tmp");
            string originalFormat;
            byte[] coverData;

            try
            {
                originalFormat = DecryptTo(input, tempPath, progress, ct, out coverData);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }

            var outputPath = ResolveOutputPath(request, targetExt);
            var sameFormat = string.Equals(originalFormat, targetExt, StringComparison.OrdinalIgnoreCase);
            var wantCover = !IsOptionTrue(request.Options, "noCoverArt")
                && coverData != null && coverData.Length > 0
                && SupportsCoverArt(targetExt);

            string coverPath = null;
            try
            {
                if (wantCover)
                {
                    coverPath = WriteTempCover(coverData);
                }

                if (sameFormat && coverPath == null)
                {
                    // 目标就是原格式且不带封面：直接拷贝（最快且无损）
                    progress?.Report(new ConversionProgress(ConversionStage.Finalizing, 100, "写入输出..."));
                    File.Copy(tempPath, outputPath, true);
                    return outputPath;
                }

                // 需要转码，或需要重新封装以附加封面
                var ffmpeg = _context?.FindTool("ffmpeg");
                if (string.IsNullOrEmpty(ffmpeg))
                {
                    if (sameFormat)
                    {
                        // 同格式但缺 FFmpeg：退化为直接拷贝（丢封面）
                        progress?.Report(new ConversionProgress(ConversionStage.Finalizing, 100, "写入输出（未能附加封面）..."));
                        File.Copy(tempPath, outputPath, true);
                        return outputPath;
                    }
                    throw new InvalidOperationException("转码需要 FFmpeg，但未找到。请安装 FFmpeg 或将其放入 tools 目录。");
                }

                progress?.Report(new ConversionProgress(ConversionStage.Running, -1, sameFormat ? "写入输出..." : "转码中..."));
                var run = ProcessRunner.Run(ffmpeg, BuildFfmpegArgs(tempPath, outputPath, request.Options, coverPath, sameFormat), ct);

                if (run.ExitCode != 0)
                {
                    throw new InvalidOperationException("FFmpeg 处理失败（错误码 " + run.ExitCode + "）：" + (run.StandardError ?? string.Empty));
                }

                return outputPath;
            }
            finally
            {
                TryDelete(tempPath);
                if (coverPath != null) TryDelete(coverPath);
            }
        }

        private string DecryptTo(string input, string outputPath, IProgress<ConversionProgress> progress, CancellationToken ct, out byte[] coverData)
        {
            coverData = null;
            progress?.Report(new ConversionProgress(ConversionStage.Starting, 0, "解析 NCM 文件..."));

            using (var fs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                var magic = reader.ReadBytes(8);
                if (magic.Length != 8 || !magic.SequenceEqual(Magic))
                {
                    throw new InvalidDataException("不是有效的 NCM 文件");
                }

                reader.ReadBytes(2); // 跳过版本号

                // 1. 密钥块
                var keyChunk = ReadChunk(reader);
                for (int i = 0; i < keyChunk.Length; i++) keyChunk[i] ^= 0x64;
                var decryptedKey = AesEcbDecrypt(keyChunk, CoreKey);
                if (decryptedKey.Length <= 17)
                {
                    throw new InvalidDataException("NCM 密钥块解析失败");
                }
                var rc4Key = new byte[decryptedKey.Length - 17];
                Array.Copy(decryptedKey, 17, rc4Key, 0, rc4Key.Length);

                // 2. 元数据块 -> format
                var metaChunk = ReadChunk(reader);
                for (int i = 0; i < metaChunk.Length; i++) metaChunk[i] ^= 0x63;
                int colon = Array.IndexOf(metaChunk, (byte)':');
                if (colon < 0)
                {
                    throw new InvalidDataException("NCM 元数据解析失败");
                }
                var base64 = new byte[metaChunk.Length - colon - 1];
                Array.Copy(metaChunk, colon + 1, base64, 0, base64.Length);
                var decryptedMeta = AesEcbDecrypt(Convert.FromBase64String(Encoding.UTF8.GetString(base64)), ModifyKey);
                if (decryptedMeta.Length <= 6)
                {
                    throw new InvalidDataException("NCM 元数据解密失败");
                }
                var json = Encoding.UTF8.GetString(decryptedMeta, 6, decryptedMeta.Length - 6);
                var format = ExtractFormat(json);
                if (string.IsNullOrEmpty(format)) format = "mp3";

                // 3. 跳过 CRC 等无用字节
                reader.ReadBytes(9);

                // 4. 封面块：提取内嵌封面图片（JPEG/PNG），供输出时附加（不再丢弃）
                var imageChunk = ReadChunk(reader);
                if (imageChunk != null && imageChunk.Length > 0) coverData = imageChunk;

                // 5. RC4 密钥盒
                var keyBox = BuildKeyBox(rc4Key);

                // 6. 解密音频数据
                long total = fs.Length - fs.Position;
                long processed = 0;

                using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    var buffer = new byte[0x8000];
                    int n;
                    while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) & 0xff;
                            buffer[i] ^= keyBox[(keyBox[j] + keyBox[(keyBox[j] + j) & 0xff]) & 0xff];
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

                return format;
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

        private static string BuildFfmpegArgs(string input, string output, IDictionary<string, string> options,
            string coverPath, bool copyAudio)
        {
            var sb = new StringBuilder();
            sb.Append("-y -hide_banner -loglevel error -i ").Append(ProcessRunner.Quote(input));

            if (!string.IsNullOrEmpty(coverPath))
            {
                // 附加封面（attached pic）：JPEG 原样拷贝，其它（PNG 等）转 mjpeg 以兼容容器
                sb.Append(" -i ").Append(ProcessRunner.Quote(coverPath));
                sb.Append(" -map 0:a -map 1:v");
                sb.Append(IsJpeg(coverPath) ? " -c:v copy" : " -c:v mjpeg");
                sb.Append(" -disposition:v attached_pic");
            }
            else
            {
                sb.Append(" -vn");
            }

            if (copyAudio)
            {
                // 同格式：音频不重编码（只重新封装）
                sb.Append(" -c:a copy");
            }
            else
            {
                string value;
                if (options != null && options.TryGetValue("audioBitrate", out value) && !string.IsNullOrEmpty(value))
                {
                    sb.Append(" -b:a ").Append(value);
                }
                if (options != null && options.TryGetValue("sampleRate", out value) && !string.IsNullOrEmpty(value))
                {
                    sb.Append(" -ar ").Append(value);
                }
            }

            sb.Append(' ').Append(ProcessRunner.Quote(output));
            return sb.ToString();
        }

        /// <summary>目标音频容器是否支持内嵌封面（attached pic）：mp3/m4a/flac 支持。</summary>
        private static bool SupportsCoverArt(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            var e = ext.StartsWith(".") ? ext : "." + ext;
            return e.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || e.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                || e.Equals(".flac", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOptionTrue(IDictionary<string, string> options, string key)
        {
            string v;
            return options != null && options.TryGetValue(key, out v)
                && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsJpeg(string path)
        {
            return path != null && path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把封面字节写成临时文件（按魔数决定扩展名）；失败返回 null。</summary>
        private static string WriteTempCover(byte[] coverData)
        {
            if (coverData == null || coverData.Length < 4) return null;
            try
            {
                var jpeg = coverData[0] == 0xFF && coverData[1] == 0xD8;
                var path = Path.Combine(Path.GetTempPath(),
                    "uc_ncm_cover_" + Guid.NewGuid().ToString("N") + (jpeg ? ".jpg" : ".png"));
                File.WriteAllBytes(path, coverData);
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>用 FFmpeg 把封面附加到音频文件（音频不重编码）。成功返回新文件路径，失败返回 null。</summary>
        private string TryAttachCover(string audioPath, string format, byte[] coverData)
        {
            var ffmpeg = _context?.FindTool("ffmpeg");
            if (string.IsNullOrEmpty(ffmpeg)) return null;

            var coverPath = WriteTempCover(coverData);
            if (string.IsNullOrEmpty(coverPath)) return null;

            var outPath = audioPath + ".cov." + format.TrimStart('.');
            try
            {
                var args = BuildFfmpegArgs(audioPath, outPath, null, coverPath, copyAudio: true);
                var run = ProcessRunner.Run(ffmpeg, args, CancellationToken.None);
                if (run.ExitCode == 0 && File.Exists(outPath)) return outPath;
                TryDelete(outPath);
                return null;
            }
            catch
            {
                TryDelete(outPath);
                return null;
            }
            finally
            {
                TryDelete(coverPath);
            }
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

        private static byte[] ReadChunk(BinaryReader reader)
        {
            uint len = reader.ReadUInt32();
            if (len > MaxChunkSize)
            {
                throw new InvalidDataException("NCM 块长度异常：" + len);
            }
            return reader.ReadBytes((int)len);
        }

        private static byte[] AesEcbDecrypt(byte[] data, byte[] key)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Key = key;
                aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        private static byte[] BuildKeyBox(byte[] rc4Key)
        {
            var keyBox = new byte[256];
            for (int i = 0; i < 256; i++) keyBox[i] = (byte)i;

            byte swap = 0, c = 0, last = 0;
            int keyOffset = 0;
            for (int i = 0; i < 256; i++)
            {
                swap = keyBox[i];
                c = (byte)((swap + last + rc4Key[keyOffset++]) & 0xff);
                if (keyOffset >= rc4Key.Length) keyOffset = 0;
                keyBox[i] = keyBox[c];
                keyBox[c] = swap;
                last = c;
            }
            return keyBox;
        }

        private static string ExtractFormat(string json)
        {
            var match = Regex.Match(json, "\"format\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
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
                        Choice("96000", "96000 Hz")),
                    BoolOption("noCoverArt", "不转换封面")
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

        private static OptionDefinition BoolOption(string key, string label, bool defaultValue = false)
        {
            return new OptionDefinition
            {
                Key = key,
                Label = label,
                Type = OptionType.Bool,
                DefaultValue = defaultValue ? "true" : "false"
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
