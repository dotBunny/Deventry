using System.Text;
using System.Text.RegularExpressions;
using Deventry.Core.Remote.Chat;

namespace Deventry.Core.Remote.Discord;

/// <summary>
/// Discord's Markdown (DESIGN.md §18, "Discord's limits"): text escaped with backslashes where Discord would read
/// formatting, and a reply's own Markdown as Discord shows it, with the headings it can't show as bold lines.
/// </summary>
public sealed partial class DiscordFormat : ChatFormat
{
    public static DiscordFormat Instance { get; } = new();

    public override int MessageLimit => DiscordApi.MessageLimit;

    /// <summary>
    /// Escapes what Discord reads as formatting anywhere (<c>\ * _ ~ ` | &lt; [</c>), and at a line's start what makes
    /// a quote, a heading or a list.
    /// </summary>
    public override string Escape(string text)
    {
        var escaped = InlineMarkup().Replace(text, @"\$0");
        // A numbered list's dot is what's escaped: "1\. Then".
        return LineStart().Replace(escaped, m => m.Groups["number"].Success
            ? $"{m.Groups["indent"].Value}{m.Groups["number"].Value}\\."
            : $"{m.Groups["indent"].Value}\\{m.Groups["mark"].Value}");
    }

    public override string Bold(string text) => $"**{Escape(text)}**";

    public override string Italic(string text) => $"*{Escape(text)}*";

    /// <summary>A code span; one holding a backtick is fenced with two.</summary>
    public override string Code(string text) => text.Contains('`', StringComparison.Ordinal) ? $"`` {text} ``" : $"`{text}`";

    public override string CodeBlock(string text, string? language = null)
    {
        var lang = language is { Length: > 0 } && LanguageName().IsMatch(language) ? language : "";
        // A fence inside would end the block: a zero-width space breaks it.
        return $"```{lang}\n{text.Replace("```", "`​``", StringComparison.Ordinal)}\n```";
    }

    public override string Quote(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => $"> {Escape(line)}"));

    /// <summary>
    /// Discord shows Markdown itself: a reply goes as it is, but for headings below the third level, which it shows as
    /// written, and which become bold lines. A code block left open is closed.
    /// </summary>
    public override string FromMarkdown(string markdown)
    {
        var text = new StringBuilder();
        var inFence = false;
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                text.Append(trimmed.StartsWith('~') ? $"```{trimmed.TrimStart('~')}" : line).Append('\n');
                continue;
            }
            var heading = inFence ? Match.Empty : DeepHeading().Match(line);
            text.Append(heading.Success ? $"**{heading.Groups["text"].Value}**" : line).Append('\n');
        }
        if (inFence)
        {
            text.Append("```\n");
        }
        return text.ToString().TrimEnd('\n');
    }

    [GeneratedRegex(@"[\\*_~`|<\[]")]
    private static partial Regex InlineMarkup();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:(?<number>\d+)\.(?=\s)|(?<mark>>|#|-(?=[\s#])|\+(?=\s)))", RegexOptions.Multiline)]
    private static partial Regex LineStart();

    [GeneratedRegex(@"^\s{0,3}#{4,6}\s+(?<text>.+?)\s*#*\s*$")]
    private static partial Regex DeepHeading();

    [GeneratedRegex("^[A-Za-z0-9_+#-]{1,32}$")]
    private static partial Regex LanguageName();
}
