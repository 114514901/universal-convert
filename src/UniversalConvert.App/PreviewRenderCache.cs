using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// 预览渲染缓存：按「插件 Id + 插件版本 + 输入文件指纹」缓存渲染产物，避免重复渲染
    /// （如 MIDI 合成）。会话级缓存——应用启动与退出时整体清空。
    ///
    /// 关键约束：
    /// - 缓存本体**绝不交给播放器**，命中/未命中都返回一份 %TEMP% 副本，避免文件被占用导致清不掉；
    /// - 淘汰按最旧（LastWriteTime，命中时 touch），被占用的文件跳过继续挑下一个；
    /// - 同时限制文件数与总体积（任一超限即淘汰）。
    /// </summary>
    public static class PreviewRenderCache
    {
        /// <summary>最多保留的缓存文件数。</summary>
        private const int MaxFiles = 5;

        /// <summary>缓存总体积上限（3GB）。</summary>
        private const long MaxBytes = 3L * 1024 * 1024 * 1024;

        /// <summary>指纹采样长度（首尾各取这么多字节）。</summary>
        private const int SampleBytes = 64 * 1024;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Task<string>> InFlight =
            new Dictionary<string, Task<string>>(StringComparer.Ordinal);

        /// <summary>缓存目录：%TEMP%\UniversalConvert-Cache\preview（会话级，不放 Roaming）。</summary>
        public static string CacheDirectory =>
            Path.Combine(Path.GetTempPath(), "UniversalConvert-Cache", "preview");

        /// <summary>清空整个缓存目录。应用启动与退出时各调一次（启动那次兼作崩溃残留兜底）。</summary>
        public static void Clear()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                }
            }
            catch (Exception ex)
            {
                // 文件仍被占用时整体删除会失败，交由下次启动清理
                Log.Warn("清理预览渲染缓存失败：" + ex.Message);
            }
        }

        /// <summary>当前缓存占用（字节数 + 文件数），供设置界面展示。</summary>
        public static void GetStats(out long bytes, out int files)
        {
            bytes = 0;
            files = 0;
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;
                foreach (var f in new DirectoryInfo(CacheDirectory).GetFiles())
                {
                    if (IsPartFile(f.Name)) continue;
                    bytes += f.Length;
                    files++;
                }
            }
            catch
            {
                // 忽略统计失败
            }
        }

        /// <summary>
        /// 取渲染产物：命中则返回缓存副本，未命中则渲染并写入缓存。
        /// 返回值是 %TEMP% 下的临时文件，**由调用方负责清理**；失败返回 null。
        /// </summary>
        public static async Task<string> GetOrRenderAsync(IPreviewProvider provider, string inputPath, CancellationToken ct)
        {
            if (provider == null || string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath)) return null;

            string key = null;
            try { key = ComputeKey(provider, inputPath); }
            catch { /* 指纹算不出来就直通渲染 */ }

            if (string.IsNullOrEmpty(key))
            {
                return await provider.RenderPreviewAsync(inputPath, ct).ConfigureAwait(false);
            }

            // 1) 命中缓存：touch 续命 + 复制一份给播放器
            var cached = FindCached(key);
            if (cached != null)
            {
                Touch(cached);
                var copy = CopyToTemp(cached);
                if (copy != null)
                {
                    Log.Info($"预览渲染缓存命中: {Path.GetFileName(cached)}");
                    return copy;
                }
                // 复制失败（缓存刚好被清理等）→ 落回渲染
            }

            // 2) 未命中：同 key 单飞，避免并发重复渲染
            Task<string> task;
            lock (Sync)
            {
                if (!InFlight.TryGetValue(key, out task))
                {
                    task = Task.Run(() => RenderAndCacheAsync(provider, inputPath, key, ct), ct);
                    InFlight[key] = task;
                }
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (Sync) { InFlight.Remove(key); }
            }
        }

        /// <summary>渲染并把产物写入缓存；返回渲染产物本身（已可直接播放）。</summary>
        private static async Task<string> RenderAndCacheAsync(IPreviewProvider provider, string inputPath, string key, CancellationToken ct)
        {
            var rendered = await provider.RenderPreviewAsync(inputPath, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(rendered) || !File.Exists(rendered)) return null;

            try
            {
                Directory.CreateDirectory(CacheDirectory);

                var ext = Path.GetExtension(rendered);
                if (string.IsNullOrEmpty(ext)) ext = ".bin";
                var cachePath = Path.Combine(CacheDirectory, key + ext);
                var partPath = cachePath + ".part";

                // 原子写：先写 .part，成功后再改名，避免半个文件被当成命中
                File.Copy(rendered, partPath, true);
                if (File.Exists(cachePath)) File.Delete(cachePath);
                File.Move(partPath, cachePath);

                Trim();
            }
            catch (Exception ex)
            {
                // 写缓存失败不影响本次预览
                Log.Warn("写入预览渲染缓存失败：" + ex.Message);
            }

            return rendered;
        }

        /// <summary>按「文件数 + 总体积」双上限淘汰最旧的缓存；被占用的跳过，继续挑下一个。</summary>
        private static void Trim()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;

                var files = new DirectoryInfo(CacheDirectory).GetFiles()
                    .Where(f => !IsPartFile(f.Name))
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                long total = 0;
                foreach (var f in files) total += f.Length;
                int count = files.Count;

                foreach (var f in files)
                {
                    if (count <= MaxFiles && total <= MaxBytes) break;

                    long size = f.Length;
                    try
                    {
                        f.Delete();
                        count--;
                        total -= size;
                    }
                    catch
                    {
                        // 被占用：跳过它，继续淘汰下一个
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("淘汰预览渲染缓存失败：" + ex.Message);
            }
        }

        /// <summary>缓存键：插件类型 + 插件版本 + 文件指纹（长度 + 修改时间 + 首尾采样）。</summary>
        private static string ComputeKey(IPreviewProvider provider, string inputPath)
        {
            var info = new FileInfo(inputPath);

            var sb = new StringBuilder();
            sb.Append(provider.GetType().FullName).Append('\n');
            var conv = provider as IConverterPlugin;
            if (conv != null)
            {
                try { sb.Append(conv.Version ?? string.Empty); } catch { }
            }
            sb.Append('\n');
            sb.Append(info.Length).Append('\n');
            sb.Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            sb.Append(SampleHash(inputPath, info.Length));

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(32);
                for (int i = 0; i < 16; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        /// <summary>首尾各采样一段算 hash：小文件等价于全量，大文件也够快。</summary>
        private static string SampleHash(string path, long length)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sha = SHA256.Create())
                {
                    var buf = new byte[Math.Min(SampleBytes, Math.Max(1, length))];

                    int n = fs.Read(buf, 0, buf.Length);
                    if (n > 0) sha.TransformBlock(buf, 0, n, null, 0);

                    if (length > SampleBytes)
                    {
                        fs.Seek(Math.Max(0, length - SampleBytes), SeekOrigin.Begin);
                        n = fs.Read(buf, 0, buf.Length);
                        if (n > 0) sha.TransformBlock(buf, 0, n, null, 0);
                    }

                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    var hash = sha.Hash;
                    if (hash == null) return string.Empty;

                    var hex = new StringBuilder(16);
                    for (int i = 0; i < 8 && i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
                    return hex.ToString();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FindCached(string key)
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return null;
                var matches = Directory.GetFiles(CacheDirectory, key + ".*");
                foreach (var m in matches)
                {
                    if (!IsPartFile(m)) return m;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>命中即刷新写入时间，让 LRU 把「常用」的留在缓存里。</summary>
        private static void Touch(string path)
        {
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
        }

        /// <summary>复制缓存文件到 %TEMP%，交给播放器（缓存本体不外借）。</summary>
        private static string CopyToTemp(string source)
        {
            try
            {
                var ext = Path.GetExtension(source);
                var dest = Path.Combine(Path.GetTempPath(),
                    "uc_cache_" + Guid.NewGuid().ToString("N") + ext);
                File.Copy(source, dest, true);
                return dest;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPartFile(string name)
        {
            return name.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
        }
    }
}
