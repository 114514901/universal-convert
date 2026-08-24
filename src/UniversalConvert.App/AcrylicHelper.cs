using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UniversalConvert.App
{
    /// <summary>
    /// 窗口级亚克力（Acrylic Blur）助手：通过未公开的 SetWindowCompositionAttribute 实现。
    /// 仅 Win10 1803+ 支持；旧系统调用会静默失败（窗口保持纯色背景）。
    /// </summary>
    public static class AcrylicHelper
    {
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>为窗口启用亚克力模糊背景（浅色，半透明白）。失败则静默忽略。</summary>
        public static void EnableAcrylic(Window window)
        {
            // 先尝试亚克力（Win10 1803+）；部分 Win11 上可能失效，降级为普通模糊
            if (!Apply(window, ACCENT_ENABLE_ACRYLICBLURBEHIND, unchecked((int)0x99FFFFFF)))
            {
                Apply(window, ACCENT_ENABLE_BLURBEHIND, unchecked((int)0x99FFFFFF));
            }
        }

        /// <summary>关闭亚克力，恢复普通背景。</summary>
        public static void DisableAcrylic(Window window)
        {
            Apply(window, ACCENT_DISABLED, 0);
        }

        private static bool Apply(Window window, int accentState, int gradientColor)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return false;

                var accent = new AccentPolicy
                {
                    AccentState = accentState,
                    AccentFlags = 2,
                    GradientColor = gradientColor,
                    AnimationId = 0
                };

                var size = Marshal.SizeOf(typeof(AccentPolicy));
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        SizeOfData = size,
                        Data = ptr
                    };
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch
            {
                // 亚克力失败不影响主流程
                return false;
            }
        }
    }
}
