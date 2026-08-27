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
        public static string WorkerThreadsAuto => Get("WorkerThreadsAuto");
        public static string SupportedFormatsTitle => Get("SupportedFormatsTitle");
        public static string SourceColumn => Get("SourceColumn");
        public static string BuiltIn => Get("BuiltIn");
        public static string Extension => Get("Extension");
        public static string CheckUpdates => Get("CheckUpdates");
        public static string UninstalledRestart => Get("UninstalledRestart");
        public static string CheckingExtensionUpdates => Get("CheckingExtensionUpdates");
        public static string NoExtensionUpdates => Get("NoExtensionUpdates");
        public static string ExtensionUpdatesFound => Get("ExtensionUpdatesFound");
        public static string ExtensionUpdateFormat => Get("ExtensionUpdateFormat");
        public static string CheckUpdatePlugin => Get("CheckUpdatePlugin");
        public static string ExtensionUpToDate => Get("ExtensionUpToDate");
        public static string UpdateSkipped => Get("UpdateSkipped");
        public static string ExtensionUpdatingTitle => Get("ExtensionUpdatingTitle");
        public static string ExtensionInstallingTitle => Get("ExtensionInstallingTitle");
        public static string ExtensionWaiting => Get("ExtensionWaiting");
        public static string ExtensionExtracting => Get("ExtensionExtracting");
        public static string ExtensionDone => Get("ExtensionDone");
        public static string ExtensionDoneRestart => Get("ExtensionDoneRestart");
        public static string ExtensionFailedFormat => Get("ExtensionFailedFormat");
        public static string ExtensionCancelled => Get("ExtensionCancelled");
        public static string ExtensionAllSucceeded => Get("ExtensionAllSucceeded");
        public static string ExtensionSummaryFormat => Get("ExtensionSummaryFormat");
        public static string PendingChangesNotApplied => Get("PendingChangesNotApplied");
        public static string ExtensionUpdatesPrompt => Get("ExtensionUpdatesPrompt");
        public static string SingleExtensionUpdatePrompt => Get("SingleExtensionUpdatePrompt");
        public static string UpdatedRestart => Get("UpdatedRestart");
        public static string ExtensionsUpdatedRestart => Get("ExtensionsUpdatedRestart");
        public static string ExtensionUpdateFailed => Get("ExtensionUpdateFailed");
        public static string ExtensionRestartPrompt => Get("ExtensionRestartPrompt");
        public static string Uninstalled => Get("Uninstalled");
        public static string ExtensionUninstallFailed => Get("ExtensionUninstallFailed");
        public static string FormatChoiceTitle => Get("FormatChoiceTitle");
        public static string FormatChoicePrompt => Get("FormatChoicePrompt");
        public static string DontAskAgain => Get("DontAskAgain");
        public static string ClearFormatChoices => Get("ClearFormatChoices");
        public static string FormatChoicesCleared => Get("FormatChoicesCleared");
        public static string Customize => Get("Customize");
        public static string Convert => Get("Convert");
        public static string SelectFileDialogTitle => Get("SelectFileDialogTitle");
        public static string AllFilesFilter => Get("AllFilesFilter");
        public static string PleaseSelectFormat => Get("PleaseSelectFormat");
        public static string NoParamsMessage => Get("NoParamsMessage");
        public static string Preparing => Get("Preparing");
        public static string ConvertingTitle => Get("ConvertingTitle");
        public static string Cancel => Get("Cancel");
        public static string OK => Get("OK");
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
        public static string ErrorVersionMismatch => Get("ErrorVersionMismatch");
        public static string ErrorVersionMismatchSuggestion => Get("ErrorVersionMismatchSuggestion");
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
        public static string Settings => Get("Settings");
        public static string Save => Get("Save");
        public static string PluginManagerTitle => Get("PluginManagerTitle");
        public static string PluginNameColumn => Get("PluginNameColumn");
        public static string VersionColumn => Get("VersionColumn");
        public static string StatusCompatible => Get("StatusCompatible");
        public static string StatusAppTooOld => Get("StatusAppTooOld");
        public static string StatusUnverified => Get("StatusUnverified");
        public static string ReleaseNotesTitle => Get("ReleaseNotesTitle");
        public static string ReleaseNotesEmpty => Get("ReleaseNotesEmpty");
        public static string Plugins => Get("Plugins");
        public static string About => Get("About");
        public static string VersionFormat => Get("VersionFormat");
        public static string AboutDescription => Get("AboutDescription");
        public static string OpenProject => Get("OpenProject");
        public static string ThirdPartyText => Get("ThirdPartyText");
        public static string AudioPlayer => Get("AudioPlayer");
        public static string Play => Get("Play");
        public static string Pause => Get("Pause");
        public static string Resume => Get("Resume");
        public static string Stop => Get("Stop");
        public static string CannotPlay => Get("CannotPlay");
        public static string PreviewRendering => Get("PreviewRendering");
        public static string PlaybackDone => Get("PlaybackDone");
        public static string Playing => Get("Playing");
        public static string PlayingSimple => Get("PlayingSimple");
        public static string Volume => Get("Volume");
        public static string Mono => Get("Mono");
        public static string Stereo => Get("Stereo");
        public static string ChannelsFormat => Get("ChannelsFormat");
        public static string ExtensionsCenter => Get("ExtensionsCenter");
        public static string Refresh => Get("Refresh");
        public static string Install => Get("Install");
        public static string InstallUpdate => Get("InstallUpdate");
        public static string Update => Get("Update");
        public static string Uninstall => Get("Uninstall");
        public static string InstalledVersionColumn => Get("InstalledVersionColumn");
        public static string SizeColumn => Get("SizeColumn");
        public static string InstalledVersionFormat => Get("InstalledVersionFormat");
        public static string NotInstalled => Get("NotInstalled");
        public static string InstallDone => Get("InstallDone");
        public static string InstallFailed => Get("InstallFailed");
        public static string Installing => Get("Installing");
        public static string UninstallConfirm => Get("UninstallConfirm");
        public static string UninstallConfirmMany => Get("UninstallConfirmMany");
        public static string UninstalledMany => Get("UninstalledMany");
        public static string LoadingExtensions => Get("LoadingExtensions");
        public static string LoadExtensionsFailed => Get("LoadExtensionsFailed");
        public static string ViewLog => Get("ViewLog");
        public static string ClearLog => Get("ClearLog");
        public static string CrashTest => Get("CrashTest");
        public static string LogCleared => Get("LogCleared");
        public static string LogViewerTitle => Get("LogViewerTitle");
        public static string OpenLogFolder => Get("OpenLogFolder");
        public static string EmptyLog => Get("EmptyLog");
        public static string RestartRequiredMessage => Get("RestartRequiredMessage");
        public static string DevVersionWarning => Get("DevVersionWarning");
        public static string CrashReportTitle => Get("CrashReportTitle");
        public static string LogLabel => Get("LogLabel");
        public static string CopyReport => Get("CopyReport");
        public static string CopyLog => Get("CopyLog");
        public static string UntestedWarning => Get("UntestedWarning");
        public static string StatusUntested => Get("StatusUntested");
        public static string LicenseText => Get("LicenseText");
        public static string Original => Get("Original");
        public static string ManualCustom => Get("ManualCustom");
        public static string Preview => Get("Preview");
        public static string FitToWindow => Get("FitToWindow");
        public static string PreviewLoading => Get("PreviewLoading");
        public static string ImagePreviewFailed => Get("ImagePreviewFailed");
        public static string ImagePreviewSizeFormat => Get("ImagePreviewSizeFormat");
        public static string TextPreviewFailed => Get("TextPreviewFailed");
        public static string TextPreviewStatsFormat => Get("TextPreviewStatsFormat");
        public static string ParamsColumn => Get("ParamsColumn");
        public static string FormatColumn => Get("FormatColumn");
        public static string OutputLocation => Get("OutputLocation");
        public static string Browse => Get("Browse");
        public static string CheckUpdate => Get("CheckUpdate");
        public static string CheckingUpdate => Get("CheckingUpdate");
        public static string UpToDate => Get("UpToDate");
        public static string Restart => Get("Restart");
        public static string TestHang => Get("TestHang");
        public static string HangReportSummary => Get("HangReportSummary");
        public static string HangReportText => Get("HangReportText");
        public static string ExceptionLabel => Get("ExceptionLabel");
        public static string ReportLabel => Get("ReportLabel");
        public static string DumpLabel => Get("DumpLabel");
        public static string LogDirectory => Get("LogDirectory");
        public static string TimeLabel => Get("TimeLabel");

        /// <summary>按 key 取本地化字符串（设置/插件标签用）。</summary>
        public static string Get(string key)
        {
            return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }

        /// <summary>解析标签：以 '@' 开头视为资源键并本地化，否则原样返回。</summary>
        public static string L(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text[0] == '@' ? Get(text.Substring(1)) : text;
        }
    }
}
