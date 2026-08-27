using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using UniversalConvert.App.Localization;

namespace UniversalConvert.App
{
    /// <summary>
    /// 文本文件预览器：只读显示常见文本格式，JSON 自动语法高亮（键/字符串/数字/布尔/标点着色）。
    /// UTF-8 严格解码失败时回退 GBK（兼容中文老文件）。超大文件（≥1MB）跳过高亮仅纯文本显示。
    /// </summary>
    public partial class TextPreviewWindow : Window
    {
        private const long HightlightLimit = 1024 * 1024; // 1MB 以上不做高亮（性能）

        private readonly string _filePath;

        public TextPreviewWindow(string filePath)
        {
            InitializeComponent();
            Icon = AppIcon.Get();
            _filePath = filePath;
            Title = Strings.Preview;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = Path.GetFileName(_filePath);

            string text;
            try
            {
                text = ReadTextFile(_filePath);
            }
            catch (Exception ex)
            {
                StatusText.Text = Strings.TextPreviewFailed + "：" + ex.Message;
                return;
            }

            var isJson = string.Equals(Path.GetExtension(_filePath), ".json", StringComparison.OrdinalIgnoreCase);
            var highlight = isJson && text.Length <= HightlightLimit;

            var doc = new FlowDocument();
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            doc.Blocks.Add(paragraph);

            if (highlight)
            {
                JsonHighlighter.Fill(paragraph, text);
            }
            else
            {
                paragraph.Inlines.Add(new Run(text));
            }

            ContentBox.Document = doc;

            var lineCount = CountLines(text);
            StatusText.Text = string.Format(Strings.TextPreviewStatsFormat, lineCount, text.Length);
        }

        private static string ReadTextFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                // 严格 UTF-8（含 BOM 场景由解码器自动处理 BOM？UTF8Encoding 解码含 BOM 字节会保留 BOM 字符——先剥离）
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                }
                var strict = new UTF8Encoding(false, true);
                return strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // 非 UTF-8：回退 GBK（中文系统代码页），再不行用系统默认
                try { return Encoding.GetEncoding(936).GetString(bytes); }
                catch { return Encoding.Default.GetString(bytes); }
            }
        }

        private static int CountLines(string text)
        {
            int count = 0;
            foreach (var c in text)
            {
                if (c == '\n') count++;
            }
            return count + (text.Length > 0 && !text.EndsWith("\n") ? 1 : 0);
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>JSON 语法高亮：手写 tokenizer（容错非严格 JSON 也能着色）。</summary>
    internal static class JsonHighlighter
    {
        private static readonly SolidColorBrush KeyBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x8F, 0xE8));   // 键
        private static readonly SolidColorBrush StringBrush = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)); // 字符串
        private static readonly SolidColorBrush NumberBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)); // 数字
        private static readonly SolidColorBrush LiteralBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)); // true/false/null
        private static readonly SolidColorBrush BraceBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B));   // { }
        private static readonly SolidColorBrush BracketBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x78, 0xDD)); // [ ]
        private static readonly SolidColorBrush PunctuationBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)); // : 与 ,
        private static readonly SolidColorBrush PlainBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));  // 普通

        public static void Fill(Paragraph paragraph, string text)
        {
            int i = 0, n = text.Length;

            // 普通文本缓冲（避免每个字符一个 Run）
            var plain = new StringBuilder();

            void FlushPlain()
            {
                if (plain.Length == 0) return;
                paragraph.Inlines.Add(new Run(plain.ToString()) { Foreground = PlainBrush });
                plain.Clear();
            }

            void AppendPlain(char c) { plain.Append(c); }

            while (i < n)
            {
                char c = text[i];

                if (c == '"')
                {
                    FlushPlain();
                    int end = ReadString(text, i, out string value);
                    // 字符串后紧跟冒号 → 视为键
                    int j = end;
                    while (j < n && char.IsWhiteSpace(text[j])) j++;
                    var brush = (j < n && text[j] == ':') ? KeyBrush : StringBrush;
                    paragraph.Inlines.Add(new Run(value) { Foreground = brush });
                    i = end;
                }
                else if (char.IsDigit(c) || (c == '-' && i + 1 < n && char.IsDigit(text[i + 1])))
                {
                    FlushPlain();
                    int end = i + 1;
                    while (end < n && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == 'e' || text[end] == 'E'
                        || text[end] == '+' || text[end] == '-')) end++;
                    paragraph.Inlines.Add(new Run(text.Substring(i, end - i)) { Foreground = NumberBrush });
                    i = end;
                }
                else if (char.IsLetter(c))
                {
                    FlushPlain();
                    int end = i;
                    while (end < n && char.IsLetter(text[end])) end++;
                    var token = text.Substring(i, end - i);
                    var brush = (token == "true" || token == "false" || token == "null") ? LiteralBrush : PlainBrush;
                    paragraph.Inlines.Add(new Run(token) { Foreground = brush });
                    i = end;
                }
                else if (c == '{' || c == '}' || c == '[' || c == ']' || c == ':' || c == ',')
                {
                    FlushPlain();
                    var brush = (c == '{' || c == '}') ? BraceBrush
                        : (c == '[' || c == ']') ? BracketBrush
                        : PunctuationBrush;
                    paragraph.Inlines.Add(new Run(c.ToString()) { Foreground = brush });
                    i++;
                }
                else
                {
                    AppendPlain(c);
                    i++;
                }
            }

            FlushPlain();
        }

        /// <summary>读取字符串字面量（处理 \" 转义），返回结束索引（含闭引号），value 为含引号的原文。</summary>
        private static int ReadString(string text, int start, out string value)
        {
            int i = start + 1, n = text.Length;
            while (i < n)
            {
                if (text[i] == '\\' && i + 1 < n)
                {
                    i += 2;
                    continue;
                }
                if (text[i] == '"')
                {
                    i++;
                    break;
                }
                i++;
            }
            value = text.Substring(start, Math.Min(i, n) - start);
            return i;
        }
    }
}