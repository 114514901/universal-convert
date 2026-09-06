using System;
using System.IO;
using System.Linq;
using UniversalConvert.Core;
using UniversalConvert.Core.Diagnostics;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.App
{
    /// <summary>
    /// 预览分发：扩展（VLC 等）优先，未接管由调用方回退内置。供主窗口与批量转换窗口共用，
    /// 避免「转换后窗口双击仍弹内置」的分叉。
    /// </summary>
    public static class PreviewDispatcher
    {
        private static readonly string[] PlayableAudioExtensions =
            { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".opus" };

        private static readonly string[] PreviewableImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
            ".ico", ".webp", ".heic", ".heif", ".avif", ".psd", ".tga"
        };

        private static readonly string[] PreviewableVideoExtensions =
        {
            ".mp4", ".m4v", ".avi", ".wmv", ".mov", ".mpg", ".mpeg", ".ts",
            ".mkv", ".webm", ".flv", ".3gp", ".ogv", ".rmvb"
        };

        private static readonly string[] PreviewableTextExtensions =
        {
            ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".csv", ".ini", ".cfg",
            ".srt", ".lrc", ".yaml", ".yml", ".css", ".js", ".py", ".bat", ".cmd", ".ps1"
        };

        public static bool CanPreviewAudio(CoreHost host, string path)
        {
            var ext = Path.GetExtension(path);
            if (PlayableAudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return true;
            if (host?.Plugins != null)
            {
                return host.Plugins.OfType<IPreviewProvider>().Any(p =>
                    p.SupportedPreviewExtensions != null
                    && p.SupportedPreviewExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
            }
            return false;
        }

        public static bool CanPreviewImage(string path)
            => PreviewableImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public static bool CanPreviewVideo(string path)
            => PreviewableVideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public static bool CanPreviewText(string path)
            => PreviewableTextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        /// <summary>查找首个支持该扩展名的渲染提供者（如 MIDI 合成、ncm 解密）。</summary>
        public static IPreviewProvider FindRenderProvider(CoreHost host, string path)
        {
            if (host?.Plugins == null) return null;
            string ext;
            try { ext = Path.GetExtension(path); } catch { return null; }
            return host.Plugins.OfType<IPreviewProvider>()
                .FirstOrDefault(p => p.SupportedPreviewExtensions != null
                    && p.SupportedPreviewExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>尝试让媒体预览提供者扩展接管音频预览；无提供者/拒绝/异常时返回 false 回退内置。</summary>
        public static bool TryAudioPreview(CoreHost host, string path, string displayName = null)
        {
            if (host == null || host.Plugins == null || string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            foreach (var provider in host.Plugins.OfType<IMediaPreviewProvider>())
            {
                try
                {
                    if (!provider.CanPreviewAudio(ext)) continue;
                    Log.Info($"音频预览提供者接管: {provider.GetType().FullName} ({path})");
                    var v2 = provider as IMediaPreviewProvider2;
                    var ok = v2 != null
                        ? v2.ShowPreviewWithName(path, displayName ?? Path.GetFileName(path))
                        : provider.ShowPreview(path);
                    if (ok) return true;
                    Log.Info($"音频预览提供者拒绝接管: {provider.GetType().FullName}");
                }
                catch (Exception ex)
                {
                    Log.Error($"音频预览提供者异常: {provider.GetType().FullName}: {ex}");
                }
            }
            return false;
        }

        /// <summary>尝试让视频预览提供者扩展接管视频预览；无提供者/拒绝/异常时返回 false 回退内置。</summary>
        public static bool TryVideoPreview(CoreHost host, string path, string displayName = null)
        {
            if (host == null || host.Plugins == null || string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            foreach (var provider in host.Plugins.OfType<IVideoPreviewProvider>())
            {
                try
                {
                    if (!provider.CanPreviewVideo(ext)) continue;
                    Log.Info($"视频预览提供者接管: {provider.GetType().FullName} ({path})");
                    var v2 = provider as IMediaPreviewProvider2;
                    var ok = v2 != null
                        ? v2.ShowPreviewWithName(path, displayName ?? Path.GetFileName(path))
                        : provider.ShowPreview(path);
                    if (ok) return true;
                    Log.Info($"视频预览提供者拒绝接管: {provider.GetType().FullName}");
                }
                catch (Exception ex)
                {
                    Log.Error($"视频预览提供者异常: {provider.GetType().FullName}: {ex}");
                }
            }
            return false;
        }
    }
}
