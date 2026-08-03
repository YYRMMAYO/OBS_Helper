using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using OBS_Helper.Wpf.Errors;

namespace OBS_Helper.Wpf.Controls;

/// <summary>Markdown 里的一个二级章节，供页面生成目录并滚动定位。</summary>
/// <param name="Title">章节标题文本。</param>
/// <param name="Anchor">该标题在可视化树中的元素，滚动定位时对它调用 BringIntoView。</param>
public sealed record MarkdownSection(string Title, FrameworkElement Anchor);

/// <summary>
/// 把 Markdown 渲染成一串 WPF 元素。
///
/// 为什么不复用 <see cref="Services.Markdown.MarkdownRenderer"/>：那个渲染器产出的是 HTML 字符串，
/// 只对 Blazor 的 MarkupString 有意义；WPF 没有 HTML 宿主（引 WebView2 只为排版一份内置文档不划算），
/// 所以这里按同一套语法规则重写块级 / 行内解析，直接产出控件树。
/// 语法支持范围与 Blazor 版严格对齐：标题、段落、有序 / 无序列表、代码块、引用、分隔线，
/// 行内 **粗体** *斜体* `代码` [链接](url)；不支持表格、图片与嵌套列表。
///
/// 颜色与字号一律用 SetResourceReference（等价于 XAML 的 DynamicResource），
/// 这样在设置页切主题 / 改字号时，已经渲染出来的正文能立即跟着变。
/// </summary>
public partial class MarkdownView : UserControl
{
    /// <summary>行内语法一次扫描：按出现顺序切分，交替顺序保证 ** 先于 * 匹配。</summary>
    private static readonly Regex InlineRegex = new(
        @"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|\*(?<italic>[^*]+)\*|\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(@"^([#]{1,6})\s+(.*)$", RegexOptions.Compiled);

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    private readonly List<MarkdownSection> _sections = new();

    public MarkdownView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 跳过文档最开头的一级标题。排障指引的 H1 与主窗口顶栏标题重复，页面可以要求丢掉。
    /// </summary>
    public bool SkipTopLevelHeading { get; set; }

    /// <summary>最近一次渲染得到的二级章节列表（渲染前为空）。</summary>
    public IReadOnlyList<MarkdownSection> Sections => _sections;

    /// <summary>重新渲染整篇文档。重复调用会先清空已有内容。</summary>
    public void Render(string? markdown)
    {
        Host.Children.Clear();
        _sections.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        var firstBlock = true;

        while (i < lines.Length)
        {
            var line = lines[i];

            // ---- 代码块：``` 之间原样保留，不做任何行内解析
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Append(lines[i]).Append('\n');
                    i++;
                }
                i++; // 跳过结束的 ```
                AddCodeBlock(code.ToString().TrimEnd('\n'));
                firstBlock = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            var trimmed = line.Trim();

            // ---- 分隔线
            if (trimmed is "---" or "***" or "___")
            {
                AddRule();
                i++;
                firstBlock = false;
                continue;
            }

            // ---- 标题
            var hm = HeadingRegex.Match(line);
            if (hm.Success)
            {
                var level = hm.Groups[1].Value.Length;
                var text = hm.Groups[2].Value;
                i++;

                // 文档标题只在「本页顶栏已经显示同名标题」时才丢弃，正文中间的 H1 仍然渲染
                if (level == 1 && firstBlock && SkipTopLevelHeading)
                {
                    firstBlock = false;
                    continue;
                }

                AddHeading(level, text);
                firstBlock = false;
                continue;
            }

            // ---- 引用：连续的 > 行合并成一段
            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                var quote = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    var content = lines[i].TrimStart();
                    content = content.Substring(1);
                    quote.Add(content.Trim());
                    i++;
                }
                AddQuote(quote);
                firstBlock = false;
                continue;
            }

            // ---- 列表：首行的标记决定整段是有序还是无序
            if (IsListStart(trimmed))
            {
                var ordered = char.IsDigit(trimmed[0]);
                var items = new List<string>();
                while (i < lines.Length && IsListStart(lines[i].Trim()))
                {
                    var item = lines[i].Trim();
                    items.Add(item[0] is '*' or '-' or '+'
                        ? item.Substring(1).Trim()
                        : item.Substring(item.IndexOf('.') + 1).Trim());
                    i++;
                }
                AddList(ordered, items);
                firstBlock = false;
                continue;
            }

            // ---- 段落：吃掉连续的普通行，行间用软换行连接
            var para = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines[i]))
            {
                para.Add(lines[i].Trim());
                i++;
            }
            AddParagraph(para);
            firstBlock = false;
        }
    }

    // ------------------------------------------------------------ 块级判定

    private static bool IsListStart(string trimmed)
    {
        if (trimmed.Length == 0) return false;
        if (trimmed[0] is '*' or '-' or '+')
            return trimmed.Length > 1 && char.IsWhiteSpace(trimmed[1]);

        var dot = trimmed.IndexOf('.');
        return dot > 0 && dot <= 3 && dot + 1 < trimmed.Length && trimmed[dot + 1] == ' '
            && int.TryParse(trimmed.Substring(0, dot), out _);
    }

    private static bool IsBlockStart(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith("```", StringComparison.Ordinal)) return true;
        if (t.StartsWith(">", StringComparison.Ordinal)) return true;
        if (IsListStart(t)) return true;
        if (HeadingRegex.IsMatch(line)) return true;
        var tr = t.Trim();
        return tr is "---" or "***" or "___";
    }

    // ------------------------------------------------------------ 块级渲染

    private void AddHeading(int level, string text)
    {
        var tb = NewTextBlock(level switch
        {
            1 => "FontSizeXl",
            2 => "FontSizeLg",
            3 => "FontSizeMd",
            _ => "FontSizeBase"
        }, "TextBrush");
        tb.FontWeight = level == 1 ? FontWeights.Bold : FontWeights.SemiBold;
        FillInlines(tb, text);

        if (level == 2)
        {
            // 二级标题是章节入口，左侧加一根品牌色竖条，长文里更容易扫到
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 9, 2) };
            bar.SetResourceReference(Border.BackgroundProperty, "BrandBrush");

            var row = new DockPanel { Margin = new Thickness(0, 22, 0, 10), LastChildFill = true };
            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);
            row.Children.Add(tb);

            Host.Children.Add(row);
            _sections.Add(new MarkdownSection(StripInline(text), row));
            return;
        }

        tb.Margin = level switch
        {
            1 => new Thickness(0, 0, 0, 12),
            3 => new Thickness(0, 14, 0, 6),
            _ => new Thickness(0, 12, 0, 5)
        };
        Host.Children.Add(tb);
    }

    private void AddParagraph(List<string> lines)
    {
        if (lines.Count == 0) return;

        var tb = NewTextBlock("FontSizeBase", "TextBrush");
        tb.LineHeight = 23;
        tb.Margin = new Thickness(0, 0, 0, 10);
        for (var n = 0; n < lines.Count; n++)
        {
            if (n > 0) tb.Inlines.Add(new LineBreak());
            FillInlines(tb, lines[n]);
        }
        Host.Children.Add(tb);
    }

    private void AddList(bool ordered, List<string> items)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 0, 0, 10) };

        for (var n = 0; n < items.Count; n++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 有序列表统一从 1 重新编号，与原版 <ol> 的行为一致（不沿用原文里写的数字）
            var marker = NewTextBlock("FontSizeBase", ordered ? "TextBrush" : "BrandBrush");
            marker.Text = ordered ? $"{n + 1}." : "\u2022";
            marker.MinWidth = 20;
            marker.Margin = new Thickness(0, 0, 8, 0);
            marker.TextWrapping = TextWrapping.NoWrap;
            if (ordered) marker.FontWeight = FontWeights.SemiBold;

            var content = NewTextBlock("FontSizeBase", "TextBrush");
            content.LineHeight = 23;
            FillInlines(content, items[n]);
            Grid.SetColumn(content, 1);

            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        Host.Children.Add(panel);
    }

    private void AddCodeBlock(string code)
    {
        var text = NewTextBlock("FontSizeSm", "TextBrush");
        text.Text = code;
        text.FontFamily = MonoFont;
        text.TextWrapping = TextWrapping.NoWrap;
        text.LineHeight = 20;

        // 代码块不换行，宽度不够时横向滚动，避免把命令 / 配置断在中间
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = text
        };

        var box = new Border { Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 2, 0, 12) };
        box.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
        box.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        box.BorderThickness = new Thickness(1);
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadiusMd");
        box.Child = scroll;

        Host.Children.Add(box);
    }

    private void AddQuote(List<string> lines)
    {
        var tb = NewTextBlock("FontSizeBase", "MutedBrush");
        tb.LineHeight = 23;
        for (var n = 0; n < lines.Count; n++)
        {
            if (n > 0) tb.Inlines.Add(new LineBreak());
            FillInlines(tb, lines[n]);
        }

        var box = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(13, 10, 13, 10),
            Margin = new Thickness(0, 2, 0, 12),
            Child = tb
        };
        box.SetResourceReference(Border.BorderBrushProperty, "BrandBrush");
        box.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadiusSm");

        Host.Children.Add(box);
    }

    private void AddRule()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14) };
        line.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        Host.Children.Add(line);
    }

    // ------------------------------------------------------------ 行内渲染

    /// <summary>把一行 Markdown 文本解析成若干 Inline 追加到目标 TextBlock。</summary>
    private void FillInlines(TextBlock target, string text)
    {
        var pos = 0;
        foreach (Match m in InlineRegex.Matches(text))
        {
            if (m.Index > pos) target.Inlines.Add(new Run(text.Substring(pos, m.Index - pos)));
            pos = m.Index + m.Length;

            if (m.Groups["code"].Success)
            {
                var run = new Run(m.Groups["code"].Value) { FontFamily = MonoFont };
                run.SetResourceReference(TextElement.BackgroundProperty, "Surface3Brush");
                run.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
                target.Inlines.Add(run);
            }
            else if (m.Groups["bold"].Success)
            {
                target.Inlines.Add(new Bold(new Run(m.Groups["bold"].Value)));
            }
            else if (m.Groups["italic"].Success)
            {
                target.Inlines.Add(new Italic(new Run(m.Groups["italic"].Value)));
            }
            else
            {
                target.Inlines.Add(BuildLink(m.Groups["label"].Value, m.Groups["url"].Value));
            }
        }

        if (pos < text.Length) target.Inlines.Add(new Run(text.Substring(pos)));
    }

    /// <summary>
    /// 生成外链。只有 http/https 才做成可点链接：其余协议（mailto、相对路径）在桌面端点了也打不开，
    /// 按原版的做法降级为纯文本，避免给用户一个点不动的蓝字。
    /// </summary>
    private Inline BuildLink(string label, string url)
    {
        if (!IsSafeUrl(url)) return new Run(label);

        var link = new Hyperlink(new Run(label))
        {
            Tag = url,
            Cursor = Cursors.Hand,
            ToolTip = url
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "BrandBrush");
        link.Click += OnLinkClick;
        return link;
    }

    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Contains('"') || url.Contains('<') || url.Contains('>') || url.Contains(' ')) return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink link || link.Tag is not string url) return;
        try
        {
            await AppServices.Host.OpenExternalAsync(url);
        }
        catch (Exception ex)
        {
            App.ReportError(ErrorCodes.Unknown, ex);
        }
    }

    /// <summary>去掉行内标记，只留纯文字。目录项不需要粗体 / 链接这些装饰。</summary>
    private static string StripInline(string text)
        => InlineRegex.Replace(text, m =>
            m.Groups["code"].Success ? m.Groups["code"].Value
            : m.Groups["bold"].Success ? m.Groups["bold"].Value
            : m.Groups["italic"].Success ? m.Groups["italic"].Value
            : m.Groups["label"].Value);

    private static TextBlock NewTextBlock(string fontSizeKey, string foregroundKey)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
        tb.SetResourceReference(TextBlock.FontSizeProperty, fontSizeKey);
        tb.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        return tb;
    }
}
