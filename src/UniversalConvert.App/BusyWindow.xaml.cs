using System.Windows;

namespace UniversalConvert.App
{
    /// <summary>轻量等待提示窗口（如 MIDI 合成期间的无进度反馈）。</summary>
    public partial class BusyWindow : Window
    {
        public BusyWindow(string message)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            MsgText.Text = message;
        }
    }
}