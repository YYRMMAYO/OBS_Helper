using OBS_Helper.Client.Services.Markdown;
using Xunit;

namespace OBS_Helper.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.RenderToHtml(null));
        Assert.Equal(string.Empty, MarkdownRenderer.RenderToHtml(""));
        Assert.Equal(string.Empty, MarkdownRenderer.RenderToHtml("   \n  "));
    }

    [Fact]
    public void Heading_Levels()
    {
        var html = MarkdownRenderer.RenderToHtml("# 标题一\n## 标题二\n### 标题三");
        Assert.Contains("<h1>标题一</h1>", html);
        Assert.Contains("<h2>标题二</h2>", html);
        Assert.Contains("<h3>标题三</h3>", html);
    }

    [Fact]
    public void UnorderedList_RendersUl()
    {
        var html = MarkdownRenderer.RenderToHtml("- 苹果\n- 香蕉\n- 橙子");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>苹果</li>", html);
        Assert.Contains("<li>香蕉</li>", html);
        Assert.Contains("<li>橙子</li>", html);
        Assert.Contains("</ul>", html);
    }

    [Fact]
    public void OrderedList_RendersOl()
    {
        var html = MarkdownRenderer.RenderToHtml("1. 第一步\n2. 第二步");
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>第一步</li>", html);
        Assert.Contains("<li>第二步</li>", html);
    }

    [Fact]
    public void CodeBlock_RendersPreCode()
    {
        var html = MarkdownRenderer.RenderToHtml("```text\n1280x720, 30fps\n```");
        Assert.Contains("<pre><code>", html);
        Assert.Contains("1280x720, 30fps", html);
        Assert.Contains("</code></pre>", html);
    }

    [Fact]
    public void Inline_Formatting()
    {
        var html = MarkdownRenderer.RenderToHtml("这是 **粗体** 与 *斜体* 与 `代码`");
        Assert.Contains("<strong>粗体</strong>", html);
        Assert.Contains("<em>斜体</em>", html);
        Assert.Contains("<code>代码</code>", html);
    }

    [Fact]
    public void SafeHttpsLink_RendersAnchor()
    {
        var html = MarkdownRenderer.RenderToHtml("见 [官方文档](https://obsproject.com/zh-cn/docs)");
        Assert.Contains("<a href=\"https://obsproject.com/zh-cn/docs\"", html);
        Assert.Contains(">官方文档</a>", html);
        Assert.Contains("noopener noreferrer", html);
    }

    [Fact]
    public void UnsafeLink_DroppedKeepsText()
    {
        var html = MarkdownRenderer.RenderToHtml("点 [这里](javascript:alert(1))");
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("这里", html);
    }

    [Fact]
    public void RawHtml_IsEscaped()
    {
        var html = MarkdownRenderer.RenderToHtml("危险 <script>alert(1)</script> 文本");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void HorizontalRule_Renders()
    {
        var html = MarkdownRenderer.RenderToHtml("上\n\n---\n\n下");
        Assert.Contains("<hr/>", html);
    }

    [Fact]
    public void Blockquote_Renders()
    {
        var html = MarkdownRenderer.RenderToHtml("> 提示：先检查权限");
        Assert.Contains("<blockquote>", html);
        Assert.Contains("提示：先检查权限", html);
    }
}
