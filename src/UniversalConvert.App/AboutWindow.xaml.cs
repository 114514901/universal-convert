using System.Diagnostics;
using System.Windows;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>关于页面：应用信息、版本、第三方组件声明。</summary>
    public partial class AboutWindow : Window
    {
        private const string ProjectUrl = "https://github.com/114514901/universal-convert";

        public AboutWindow()
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            IconImage.Source = AppIcon.Get();
            VersionText.Text = string.Format(Strings.VersionFormat, AppVersion.Current?.ToString() ?? "?");
            DescText.Text = Strings.AboutDescription;
            ThirdPartyText.Text = Strings.ThirdPartyText;
            LicenseText.Text = Strings.LicenseText;
        }

        private void OnOpenProject(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(ProjectUrl);
            }
            catch
            {
                // 忽略打开失败
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
