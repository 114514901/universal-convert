using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UniversalConvert.App
{
    /// <summary>从 exe 提取图标（即 ApplicationIcon），供各窗口的标题栏/任务栏使用。</summary>
    public static class AppIcon
    {
        private static ImageSource _icon;

        public static ImageSource Get()
        {
            if (_icon == null)
            {
                try
                {
                    var exe = Assembly.GetExecutingAssembly().Location;
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                    {
                        if (icon != null)
                        {
                            _icon = Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                        }
                    }
                }
                catch
                {
                    _icon = null;
                }
            }
            return _icon;
        }
    }
}
