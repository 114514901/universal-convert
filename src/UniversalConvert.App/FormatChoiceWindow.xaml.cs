using System.Collections.Generic;
using System.Windows;
using UniversalConvert.App.Localization;
using UniversalConvert.Core.Engine;

namespace UniversalConvert.App
{
    /// <summary>「同一格式方向被多个扩展注册」时的选择对话框，支持记住选择。</summary>
    public partial class FormatChoiceWindow : Window
    {
        private readonly IList<ConversionEntry> _candidates;
        private readonly string _inputExt;
        private readonly string _outputExt;

        /// <summary>用户选中的条目；取消为 null。</summary>
        public ConversionEntry Choice { get; private set; }

        /// <summary>是否勾选「不再提醒」。</summary>
        public bool Remember { get; private set; }

        public FormatChoiceWindow(IList<ConversionEntry> candidates, string inputExt, string outputExt)
        {
            InitializeComponent();
            _candidates = candidates;
            _inputExt = inputExt;
            _outputExt = outputExt;

            PromptText.Text = string.Format(
                Strings.FormatChoicePrompt,
                "." + FormatRegistry.Normalize(inputExt),
                "." + FormatRegistry.Normalize(outputExt));

            ChoiceList.ItemsSource = candidates;
            ChoiceList.SelectedIndex = 0;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Choice = ChoiceList.SelectedItem as ConversionEntry;
            Remember = RememberCheck.IsChecked == true;
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
