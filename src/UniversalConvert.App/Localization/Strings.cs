using System.Globalization;
using System.Resources;

namespace UniversalConvert.App.Localization
{
    /// <summary>
    /// 强类型资源访问器。字符串按系统 UI 语言自动选择（zh 用 Strings.resx，en 用 Strings.en.resx）。
    /// XAML 中通过 {x:Static loc:Strings.Xxx} 绑定，代码中用 Strings.Xxx。
    /// </summary>
    public static class Strings
    {
        private static readonly ResourceManager Rm =
            new ResourceManager("UniversalConvert.App.Localization.Strings", typeof(Strings).Assembly);

        public static string DropHint => Get("DropHint");
        public static string NoFileSelected => Get("NoFileSelected");
        public static string SelectFile => Get("SelectFile");
        public static string OutputFormats => Get("OutputFormats");
        public static string Customize => Get("Customize");
        public static string Convert => Get("Convert");
        public static string SelectFileDialogTitle => Get("SelectFileDialogTitle");
        public static string AllFilesFilter => Get("AllFilesFilter");
        public static string PleaseSelectFormat => Get("PleaseSelectFormat");
        public static string NoParamsMessage => Get("NoParamsMessage");
        public static string Preparing => Get("Preparing");
        public static string ConvertingTitle => Get("ConvertingTitle");
        public static string Cancel => Get("Cancel");
        public static string Close => Get("Close");
        public static string OpenOutputFolder => Get("OpenOutputFolder");
        public static string Done => Get("Done");
        public static string ConvertingFormat => Get("ConvertingFormat");
        public static string ConvertSucceeded => Get("ConvertSucceeded");
        public static string ConvertFailedPrefix => Get("ConvertFailedPrefix");
        public static string CustomizeTitle => Get("CustomizeTitle");
        public static string Preset => Get("Preset");
        public static string StartConvert => Get("StartConvert");
        public static string DefaultRecommended => Get("DefaultRecommended");
        public static string TargetFormat => Get("TargetFormat");
        public static string InputFileMissing => Get("InputFileMissing");
        public static string UnsupportedConversion => Get("UnsupportedConversion");
        public static string ToolNotInstalled => Get("ToolNotInstalled");

        private static string Get(string key)
        {
            return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
    }
}
