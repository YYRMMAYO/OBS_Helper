using OBS_Helper.Wpf.Services.Markdown;

namespace OBS_Helper.Wpf.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal("", MarkdownRenderer.RenderToHtml(null));
        Assert.Equal("", MarkdownRenderer.RenderToHtml(""));
    }

    [Fact]
    public void Heading_Rendered()
    {
        Assert.Contains("<h1>Title</h1>", MarkdownRenderer.RenderToHtml("# Title"));
        Assert.Contains("<h2>Sub</h2>", MarkdownRenderer.RenderToHtml("## Sub"));
    }

    [Fact]
    public void UnorderedList_Rendered()
    {
        var html = MarkdownRenderer.RenderToHtml("- one\n- two");
        Assert.Contains("<ul><li>one</li><li>two</li></ul>", html);
    }

    [Fact]
    public void OrderedList_Rendered()
    {
        var html = MarkdownRenderer.RenderToHtml("1. first\n2. second");
        Assert.Contains("<ol><li>first</li><li>second</li></ol>", html);
    }

    [Fact]
    public void Inline_BoldItalic()
    {
        var html = MarkdownRenderer.RenderToHtml("**bold** and *ital*");
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>ital</em>", html);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:a@b.com", true)]
    [InlineData("#section", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    public void Link_SafeUrlsOnly(string url, bool expectedLinked)
    {
        var html = MarkdownRenderer.RenderToHtml("[label](" + url + ")");
        if (expectedLinked)
            Assert.Contains("href=\"" + url + "\"", html);
        else
            Assert.DoesNotContain("href=", html);
    }

    [Fact]
    public void RawHtml_IsEscaped()
    {
        var html = MarkdownRenderer.RenderToHtml("<img src=x onerror=alert(1)>");
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }
}
