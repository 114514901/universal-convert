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

namespace UniversalConvert.Plugin.Ncm
{
    /// <summary>
    /// 网易云音乐 .ncm 解密插件（纯 C#，无外部依赖）。
    /// 解密逻辑移植自 hkylin/ncmdumpGUI（MIT License, Copyright (c) 2018 kpali）。
    /// NCM 文件内封装的原始格式（mp3/flac）由文件自身元数据决定，解密时按原始格式输出。
    /// </summary>
    public sealed class NcmPlugin : IConverterPlugin
    {
        private static readonly byte[] Magic = { 0x43, 0x54, 0x45, 0x4E, 0x46, 0x44, 0x41, 0x4D }; // "CTENFDAM"
        private static readonly byte[] CoreKey = { 0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57 };
        private static readonly byte[] ModifyKey = { 0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28 };
        private const uint MaxChunkSize = 100 * 1024 * 1024;

        public string Id => "com.universalconvert.ncm";
        public string Name => "NCM 解密";
        public string Description => "网易云音乐 .ncm 解密，按原始格式输出 mp3/flac";

        public void Initialize(IPluginContext context)
        {
        }

        public bool IsToolAvailable()
        {
            return true;
        }

        public IList<ConversionCapability> GetCapabilities()
        {
            return new List<ConversionCapability>
            {
                new ConversionCapability
                {
                    InputExtension = ".ncm",
                    InputDisplayName = "网易云音乐 NCM",
                    Outputs = new List<OutputFormat>
                    {
                        new OutputFormat { Extension = ".mp3", DisplayName = "MP3（原始格式）" },
                        new OutputFormat { Extension = ".flac", DisplayName = "FLAC（原始格式）" }
                    }
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
                    var outputPath = Decrypt(request, progress, cancellationToken);
                    return ConversionResult.Succeeded(outputPath, DateTime.UtcNow - started);
                }
                catch (OperationCanceledException)
                {
                    return ConversionResult.Failed("转换已取消", DateTime.UtcNow - started);
                }
                catch (Exception ex)
                {
                    return ConversionResult.Failed(ex.Message, DateTime.UtcNow - started);
                }
            }, cancellationToken);
        }

        private string Decrypt(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
        {
            var input = request.InputPath;
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("输入文件不存在：" + input);
            }

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

                // 1. 密钥块：XOR 0x64 后 AES-ECB 解密，得到 "neteasecloudmusic" + RC4 密钥
                var keyChunk = ReadChunk(reader);
                for (int i = 0; i < keyChunk.Length; i++) keyChunk[i] ^= 0x64;
                var decryptedKey = AesEcbDecrypt(keyChunk, CoreKey);
                if (decryptedKey.Length <= 17)
                {
                    throw new InvalidDataException("NCM 密钥块解析失败");
                }
                var rc4Key = new byte[decryptedKey.Length - 17];
                Array.Copy(decryptedKey, 17, rc4Key, 0, rc4Key.Length);

                // 2. 元数据块：XOR 0x63 -> base64 -> AES-ECB 解密 -> "music:" + JSON，取 format
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

                // 4. 封面块（暂不写入，读掉以推进流位置）
                ReadChunk(reader);

                // 5. 生成 RC4 密钥盒
                var keyBox = BuildKeyBox(rc4Key);

                // 6. 解密剩余音频数据
                var outputPath = ResolveOutputPath(request, format);
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

                progress?.Report(new ConversionProgress(ConversionStage.Completed, 100, "完成"));
                return outputPath;
            }
        }

        private string ResolveOutputPath(ConversionRequest request, string format)
        {
            if (!string.IsNullOrEmpty(request.OutputPath))
            {
                var dir = Path.GetDirectoryName(request.OutputPath);
                var name = Path.GetFileNameWithoutExtension(request.OutputPath);
                return Path.Combine(dir ?? string.Empty, name + "." + format);
            }

            var inputDir = Path.GetDirectoryName(request.InputPath);
            var inputName = Path.GetFileNameWithoutExtension(request.InputPath);
            return Path.Combine(inputDir ?? string.Empty, inputName + "." + format);
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
    }
}
