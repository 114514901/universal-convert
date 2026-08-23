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
        public static string AdminWarning => Get("AdminWarning");
        public static string Retry => Get("Retry");
        public static string CopyError => Get("CopyError");
        public static string ErrorDetail => Get("ErrorDetail");
        public static string ErrorUnknown => Get("ErrorUnknown");
        public static string ErrorUnknownSuggestion => Get("ErrorUnknownSuggestion");
        public static string ErrorToolNotFound => Get("ErrorToolNotFound");
        public static string ErrorToolNotFoundSuggestion => Get("ErrorToolNotFoundSuggestion");
        public static string ErrorInputFileMissing => Get("ErrorInputFileMissing");
        public static string ErrorInputFileMissingSuggestion => Get("ErrorInputFileMissingSuggestion");
        public static string ErrorPermissionDenied => Get("ErrorPermissionDenied");
        public static string ErrorPermissionDeniedSuggestion => Get("ErrorPermissionDeniedSuggestion");
        public static string ErrorUnknownEncoder => Get("ErrorUnknownEncoder");
        public static string ErrorUnknownEncoderSuggestion => Get("ErrorUnknownEncoderSuggestion");
        public static string ErrorInvalidInput => Get("ErrorInvalidInput");
        public static string ErrorInvalidInputSuggestion => Get("ErrorInvalidInputSuggestion");
        public static string ErrorNoSpaceLeft => Get("ErrorNoSpaceLeft");
        public static string ErrorNoSpaceLeftSuggestion => Get("ErrorNoSpaceLeftSuggestion");
        public static string UpdateAvailable => Get("UpdateAvailable");
        public static string DownloadUpdate => Get("DownloadUpdate");
        public static string FilesLabel => Get("FilesLabel");
        public static string AddFiles => Get("AddFiles");
        public static string RemoveSelected => Get("RemoveSelected");
        public static string ClearAll => Get("ClearAll");
        public static string NoCommonFormat => Get("NoCommonFormat");
        public static string BatchTitle => Get("BatchTitle");
        public static string FileColumn => Get("FileColumn");
        public static string StatusColumn => Get("StatusColumn");
        public static string StatusPending => Get("StatusPending");
        public static string StatusConverting => Get("StatusConverting");
        public static string StatusDone => Get("StatusDone");
        public static string StatusSkipped => Get("StatusSkipped");
        public static string StatusFailed => Get("StatusFailed");
        public static string BatchSummary => Get("BatchSummary");
        public static string ErrorsLabel => Get("ErrorsLabel");
        public static string ViewReleaseNotes => Get("ViewReleaseNotes");
        public static string Downloading => Get("Downloading");
        public static string DownloadComplete => Get("DownloadComplete");
        public static string DownloadFailed => Get("DownloadFailed");

        private static string Get(string key)
        {
            return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
    }
}
