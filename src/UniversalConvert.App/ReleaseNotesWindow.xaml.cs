using System.Windows;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>应用内显示更新内容（Release 正文）。</summary>
    public partial class ReleaseNotesWindow : Window
    {
        public ReleaseNotesWindow(string version, string body)
        {
            InitializeComponent();
            Icon = AppIcon.Get();

            VersionText.Text = version ?? string.Empty;
            BodyBox.Text = string.IsNullOrEmpty(body) ? Strings.ReleaseNotesEmpty : body;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
