using System.Text;
using System.Text.RegularExpressions;

namespace Deventry.Core.Remote.Telegram;

/// <summary>
/// Telegram's HTML (DESIGN.md §18, "Telegram's limits"): text escaped (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>), and a
/// reply's Markdown with its code spans, code blocks, bold and links kept and the rest as plain text; the start of a
/// long reply; and the data behind a button, which carries the run it was sent in.
/// </summary>
public static partial class TelegramFormat
{
    /// <summary>How much of a reply a finished turn's message shows.</summary>
    public const int ReplyPreviewLength = 600;

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

    /// <summary>
    /// The start of a reply, for a finished turn's message: whole lines up to about <paramref name="max"/> characters, or
    /// the first line cut at a word, and whether anything was left out. A code block it cuts into is closed when it's
    /// made HTML.
    /// </summary>
    public static (string Text, bool IsCut) FirstLines(string markdown, int max = ReplyPreviewLength)
    {
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length <= max)
        {
            return (text, false);
        }
        var lines = text.Split('\n');
        var kept = new StringBuilder();
        foreach (var line in lines)
        {
            if (kept.Length + line.Length + 1 > max)
            {
                break;
            }
            kept.Append(line).Append('\n');
        }
        return (kept.Length > 0 ? kept.ToString().TrimEnd() : RemoteMessages.Shorten(lines[0], max), true);
    }

    /// <summary>A reply's whole Markdown fits one message, as HTML.</summary>
    public static bool FitsMessage(string html) => html.Length <= TelegramBotApi.MessageLimit;

    // ---- Buttons ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A button's data: the run it was sent in, then the key Deventry looks its action up by, well within Telegram's 64
    /// bytes. A button from another run is stale whatever its age.
    /// </summary>
    public static string ButtonData(string run, string key) => $"{run}:{key}";

    /// <summary>The run and key in a button's data; null for data Deventry didn't make.</summary>
    public static (string Run, string Key)? ParseButtonData(string? data)
    {
        var colon = data?.IndexOf(':') ?? -1;
        return colon > 0 && colon < data!.Length - 1 ? (data[..colon], data[(colon + 1)..]) : null;
    }

    /// <summary>A key in base 36, short however many buttons a run makes.</summary>
    public static string Key(long number)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (number <= 0)
        {
            return "0";
        }
        var key = new StringBuilder();
        while (number > 0)
        {
            key.Insert(0, digits[(int)(number % 36)]);
            number /= 36;
        }
        return key.ToString();
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
