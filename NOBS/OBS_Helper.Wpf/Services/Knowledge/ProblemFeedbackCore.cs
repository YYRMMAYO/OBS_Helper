using System.Text;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models;

namespace OBS_Helper.Wpf.Services.Knowledge;

/// <summary>一次「这条解决问题了吗」的回执。</summary>
public sealed record ProblemFeedback(
    /// <summary>被评价的问题。</summary>
    Problem Problem,
    /// <summary>是否解决了（true = 解决了，false = 没解决）。</summary>
    bool Solved,
    /// <summary>用户补充的几句话（可为空）。</summary>
    string UserNote,
    /// <summary>本地知识库版本号（用于让维护者知道这条是基于哪一版写的）。</summary>
    string KbVersion,
    /// <summary>应用版本。</summary>
    string AppVersion,
    /// <summary>本机是否用的是热更新过的知识库（false = 随包内置）。</summary>
    bool UsingExternalKb,
    /// <summary>本机 OBS 版本（可为空）。</summary>
    string ObsVersion,
    /// <summary>本机系统描述（可为空）。</summary>
    string Platform);

/// <summary>
/// 知识库回执核心（V3.0 / D8）。纯逻辑、零 IO，单测工程直接链接编译。
///
/// 解决的问题：知识库只能被动接收热更新，用户端**没有回写路径** ——
/// 「这条不解决问题」这个最有价值的信息此前根本传不回来。
///
/// 两条刻意的设计：
/// <list type="number">
///   <item><b>不自动上传</b>：生成的是一段 Markdown，由用户自己贴到 Issue / 邮件里。
///     本地知识库的更新与反馈是两件事，不该让「点一下按钮」变成一次静默的数据外发。</item>
///   <item><b>带上问题 id 与知识库版本</b>：否则维护者收到「这条没用」时，
///     既不知道说的是哪一条，也不知道对方看的是哪一版内容（热更新会改条目）。</item>
/// </list>
/// </summary>
public static class ProblemFeedbackCore
{
    /// <summary>Issue 标题：一眼能看出是哪条、哪一版（维护者按标题就能分流）。</summary>
    public static string BuildTitle(ProblemFeedback feedback)
    {
        var kind = feedback.Solved ? Strings.T("kb.feedback.title.solved") : Strings.T("kb.feedback.title.unsolved");
        return Strings.T("kb.feedback.issueTitle", kind,
            OneLine(feedback.Problem.Id), OneLine(feedback.Problem.Title), feedback.KbVersion);
    }

    /// <summary>
    /// 把用户可控文本压成单行（V3.0 第三轮验证修复）。
    ///
    /// 本地条目的 id / 标题是用户自己写的，里面完全可能有换行：标题里的裸 <c>---</c> 会把回执
    /// 的 Markdown 切开、<c># </c> 会插出一个标题；换行带进 Issue 标题还会变成 %0A。
    /// 回执是给维护者看的，单行化比「保留原文换行」更符合它的用途。
    /// </summary>
    internal static string OneLine(string? text)
        => (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>可粘贴的 Markdown 回执。</summary>
    public static string BuildMarkdown(ProblemFeedback feedback)
    {
        var sb = new StringBuilder();

        sb.AppendLine(Strings.T("kb.feedback.md.heading"));
        sb.AppendLine();
        sb.AppendLine(Strings.T("kb.feedback.md.conclusion",
            feedback.Solved ? Strings.T("kb.feedback.solved") : Strings.T("kb.feedback.unsolved")));
        // 单行化：标题里的换行 / 裸 --- / # 会破坏回执的 Markdown 结构
        sb.AppendLine(Strings.T("kb.feedback.md.problem", OneLine(feedback.Problem.Id), OneLine(feedback.Problem.Title)));
        sb.AppendLine(Strings.T("kb.feedback.md.kbVersion", feedback.KbVersion,
            feedback.UsingExternalKb ? Strings.T("kb.feedback.kb.external") : Strings.T("kb.feedback.kb.embedded")));
        sb.AppendLine(Strings.T("kb.feedback.md.appVersion", feedback.AppVersion));
        if (!string.IsNullOrWhiteSpace(feedback.ObsVersion))
            sb.AppendLine(Strings.T("kb.feedback.md.obsVersion", feedback.ObsVersion));
        if (!string.IsNullOrWhiteSpace(feedback.Platform))
            sb.AppendLine(Strings.T("kb.feedback.md.platform", feedback.Platform));

        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(feedback.UserNote))
        {
            sb.AppendLine(Strings.T("kb.feedback.md.noNote"));
        }
        else
        {
            sb.AppendLine(Strings.T("kb.feedback.md.note"));
            sb.AppendLine();
            // 用户写的内容原样引用，避免 Markdown 把多行说明吃成一团
            foreach (var line in feedback.UserNote.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
                sb.AppendLine("> " + line);
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(Strings.T("kb.feedback.md.footer"));
        return sb.ToString();
    }

    /// <summary>
    /// GitHub Issue 的「新建」链接。
    ///
    /// **只带标题，不带正文**：正文动辄上千字，URL 长度在很多浏览器/代理下会被截断，
    /// 而截断后的 Issue 比没有更糟（用户以为贴上了）。正文由用户从剪贴板粘贴。
    /// </summary>
    public static string BuildIssueUrl(string repository, ProblemFeedback feedback)
    {
        var title = Uri.EscapeDataString(BuildTitle(feedback));
        var body = Uri.EscapeDataString(Strings.T("kb.feedback.issueBodyHint"));

        // 标题与提示语都很短，放在 URL 里是安全的
        return $"https://github.com/{repository}/issues/new?title={title}&body={body}&labels=knowledge-base";
    }
}
