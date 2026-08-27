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

            var ext = Path.GetExtension(_filePath);
            var isJson = string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase);
            var isYaml = string.Equals(ext, ".yml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".yaml", StringComparison.OrdinalIgnoreCase);
            var highlight = (isJson || isYaml) && text.Length <= HightlightLimit;

            var doc = new FlowDocument();
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            doc.Blocks.Add(paragraph);

            if (highlight)
            {
                if (isJson)
                {
                    JsonHighlighter.Fill(paragraph, text);
                }
                else
                {
                    JsonHighlighter.FillYaml(paragraph, text);
                }
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

    /// <summary>JSON/YAML 语法高亮：手写 tokenizer（容错非严格输入也能着色）。</summary>
    internal static class JsonHighlighter
    {
        private static readonly SolidColorBrush KeyBrush = new SolidColorBrush(Color.FromRgb(0x61, 0xAF, 0xEF));   // 键（蓝）
        private static readonly SolidColorBrush StringBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xC3, 0x79)); // 字符串（绿）
        private static readonly SolidColorBrush NumberBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x9A, 0x66)); // 数字（橙）
        private static readonly SolidColorBrush LiteralBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)); // true/false/null（蓝）
        private static readonly SolidColorBrush BraceBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B));   // { }（金，与字符串绿明显区分）
        private static readonly SolidColorBrush BracketBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x78, 0xDD)); // [ ]（紫）
        private static readonly SolidColorBrush PunctuationBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)); // : 与 ,
        private static readonly SolidColorBrush PlainBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));  // 普通
        private static readonly SolidColorBrush CommentBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55)); // 注释（灰绿）

        /// <summary>YAML 高亮（简化）：键蓝、字符串绿、数字橙、布尔蓝、注释灰绿、列表符灰。</summary>
        public static void FillYaml(Paragraph paragraph, string text)
        {
            int i = 0, n = text.Length;
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
                // 注释
                if (text[i] == '#' && (i == 0 || text[i - 1] == ' ' || text[i - 1] == '\t' || text[i - 1] == '\n' || text[i - 1] == '\r'))
                {
                    FlushPlain();
                    int end = text.IndexOf('\n', i);
                    if (end < 0) end = n;
                    paragraph.Inlines.Add(new Run(text.Substring(i, end - i)) { Foreground = CommentBrush });
                    i = end;
                    continue;
                }

                // 字符串值（引号）
                if (text[i] == '"' || text[i] == '\'')
                {
                    FlushPlain();
                    int end = ReadString(text, i, out string value);
                    paragraph.Inlines.Add(new Run(value) { Foreground = StringBrush });
                    i = end;
                    continue;
                }

                // 数字
                if (char.IsDigit(text[i]) || (text[i] == '-' && i + 1 < n && char.IsDigit(text[i + 1])))
                {
                    FlushPlain();
                    int end = i + 1;
                    while (end < n && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == 'e' || text[end] == 'E'
                        || text[end] == '+' || text[end] == '-' || text[end] == '_')) end++;
                    paragraph.Inlines.Add(new Run(text.Substring(i, end - i)) { Foreground = NumberBrush });
                    i = end;
                    continue;
                }

                // 键：identifier 后跟 ':' +（空白或行尾）
                if (char.IsLetter(text[i]) || text[i] == '_')
                {
                    int wordEnd = i;
                    while (wordEnd < n && (char.IsLetterOrDigit(text[wordEnd]) || text[wordEnd] == '_' || text[wordEnd] == '-')) wordEnd++;
                    int j = wordEnd;
                    while (j < n && text[j] == ' ') j++;
                    if (j < n && text[j] == ':' && (j + 1 >= n || text[j + 1] == ' ' || text[j + 1] == '\t' || text[j + 1] == '\r' || text[j + 1] == '\n'))
                    {
                        FlushPlain();
                        paragraph.Inlines.Add(new Run(text.Substring(i, wordEnd - i)) { Foreground = KeyBrush });
                        paragraph.Inlines.Add(new Run(":") { Foreground = PunctuationBrush });
                        i = j + 1;
                        continue;
                    }
                    // 不是键 → 布尔字面量？
                    var word = text.Substring(i, wordEnd - i);
                    if (word == "true" || word == "false" || word == "null" || word == "yes" || word == "no" || word == "on" || word == "off")
                    {
                        FlushPlain();
                        paragraph.Inlines.Add(new Run(word) { Foreground = LiteralBrush });
                        i = wordEnd;
                        continue;
                    }
                    // 普通词
                    plain.Append(word);
                    i = wordEnd;
                    continue;
                }

                // 列表项标记
                if (text[i] == '-')
                {
                    FlushPlain();
                    paragraph.Inlines.Add(new Run("-") { Foreground = PunctuationBrush });
                    i++;
                    continue;
                }

                AppendPlain(text[i]);
                i++;
            }

            FlushPlain();
        }

        /// <summary>读取字符串字面量（处理转义），返回结束索引（含闭引号），value 为含引号的原文。</summary>
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
    }
}