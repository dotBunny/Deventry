using System.Text;
using System.Text.RegularExpressions;

namespace Deventry.Core.Remote.Telegram;

/// <summary>
/// Telegram's HTML (DESIGN.md §18, "Telegram's limits"): text escaped (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>), and a
/// reply's Markdown with its code spans, code blocks, bold and links kept and the rest as plain text. The bot writes
/// through <see cref="TelegramHtml"/>.
/// </summary>
public static partial class TelegramFormat
{
    /// <summary>Escapes text for Telegram's HTML.</summary>
    public static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    public static string Bold(string text) => $"<b>{Escape(text)}</b>";

    public static string Italic(string text) => $"<i>{Escape(text)}</i>";

    public static string Code(string text) => $"<code>{Escape(text)}</code>";

    /// <summary>A block of code, such as a command to allow.</summary>
    public static string CodeBlock(string text, string? language = null) => language is { Length: > 0 } && LanguageName().IsMatch(language)
        ? $"<pre><code class=\"language-{language}\">{Escape(text)}</code></pre>"
        : $"<pre>{Escape(text)}</pre>";

    /// <summary>
    /// Markdown as Telegram's HTML: fenced code blocks, code spans, bold, http and https links, and headings as bold
    /// lines; everything else is plain text, escaped. A code block left open runs to the end.
    /// </summary>
    public static string FromMarkdown(string markdown)
    {
        var html = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? fence = null;
        string? language = null;
        var code = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (fence is null && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                fence = trimmed[..3];
                language = trimmed[3..].Trim().Split(' ')[0];
                code.Clear();
                continue;
            }
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.Trim('`', '~').Trim().Length == 0)
                {
                    html.Append(CodeBlock(code.ToString().TrimEnd('\n'), language)).Append('\n');
                    fence = null;
                    continue;
                }
                code.Append(line).Append('\n');
                continue;
            }
            html.Append(Line(line)).Append('\n');
        }
        if (fence is not null)
        {
            html.Append(CodeBlock(code.ToString().TrimEnd('\n'), language)).Append('\n');
        }
        return html.ToString().TrimEnd('\n');
    }

    private static string Line(string line)
    {
        var heading = Heading().Match(line);
        if (heading.Success)
        {
            return $"<b>{Inline(heading.Groups["text"].Value)}</b>";
        }
        // A bullet reads better as one.
        var bullet = Bullet().Match(line);
        return bullet.Success ? $"{bullet.Groups["indent"].Value}• {Inline(bullet.Groups["text"].Value)}" : Inline(line);
    }

    /// <summary>One line's code spans, bold and links; the rest escaped.</summary>
    private static string Inline(string text)
    {
        var html = new StringBuilder();
        var at = 0;
        foreach (Match match in InlineMarkup().Matches(text))
        {
            html.Append(Escape(text[at..match.Index]));
            if (match.Groups["code"].Success)
            {
                html.Append(Code(match.Groups["code"].Value));
            }
            else if (match.Groups["bold"].Success)
            {
                html.Append("<b>").Append(Inline(match.Groups["bold"].Value)).Append("</b>");
            }
            else
            {
                html.Append("<a href=\"").Append(Escape(match.Groups["url"].Value).Replace("\"", "&quot;", StringComparison.Ordinal)).Append("\">")
                    .Append(Escape(match.Groups["label"].Value)).Append("</a>");
            }
            at = match.Index + match.Length;
        }
        html.Append(Escape(text[at..]));
        return html.ToString();
    }

    [GeneratedRegex(@"`(?<code>[^`\n]+)`|\*\*(?<bold>[^*\n]+)\*\*|__(?<bold>[^_\n]+)__|\[(?<label>[^\]\n]+)\]\((?<url>https?://[^)\s]+)\)")]
    private static partial Regex InlineMarkup();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(?<text>.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+]\s+(?<text>.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex("^[A-Za-z0-9_+#-]{1,32}$")]
    private static partial Regex LanguageName();
}
