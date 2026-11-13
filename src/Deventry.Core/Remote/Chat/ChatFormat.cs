using System.Text;

namespace Deventry.Core.Remote.Chat;

/// <summary>
/// How a chat service writes a bot's messages (DESIGN.md §18, "What the bot sends"): Telegram's HTML or Discord's
/// Markdown, each escaping text its own way, and the longest message it takes. What every service shares is here too:
/// the start of a long reply, and the data behind a button, which carries the run it was sent in.
/// </summary>
public abstract class ChatFormat
{
    /// <summary>How much of a reply a finished turn's message shows.</summary>
    public const int ReplyPreviewLength = 600;

    /// <summary>The longest a message's text can be.</summary>
    public abstract int MessageLimit { get; }

    /// <summary>Text as the service shows it, with nothing in it read as formatting.</summary>
    public abstract string Escape(string text);

    public abstract string Bold(string text);

    public abstract string Italic(string text);

    /// <summary>A code span: a path, say.</summary>
    public abstract string Code(string text);

    /// <summary>A block of code, such as a command to allow, colored as <paramref name="language"/> when the service can.</summary>
    public abstract string CodeBlock(string text, string? language = null);

    /// <summary>Text set apart as a quotation, such as a thread's message.</summary>
    public abstract string Quote(string text);

    /// <summary>A reply's Markdown, as the service shows it.</summary>
    public abstract string FromMarkdown(string markdown);

    /// <summary>The text fits one message.</summary>
    public bool FitsMessage(string text) => text.Length <= MessageLimit;

    /// <summary>
    /// The start of a reply, for a finished turn's message: whole lines up to about <paramref name="max"/> characters, or
    /// the first line cut at a word, and whether anything was left out. A code block it cuts into is closed when it's
    /// formatted.
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

    // ---- Buttons ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A button's data: the run it was sent in, then the key Deventry looks its action up by, well within Telegram's 64
    /// bytes and Discord's 100 characters. A button from another run is stale whatever its age.
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
}
