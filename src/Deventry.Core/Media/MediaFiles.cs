using System.Text.RegularExpressions;
using Deventry.Core.Protocol;

namespace Deventry.Core.Media;

/// <summary>What a media file is (DESIGN.md §18, "Media").</summary>
public enum MediaKind
{
    Image,
    Video,
    Audio,
}

/// <summary>
/// The media files a conversation names, for the Media window (DESIGN.md §18, "Media"): images, video and sound, known by
/// their extension, and the paths to them in a message, a reply or a command. A path is as it was written; where it is on
/// disk, and whether looking for it is safe, is <see cref="ReplyFileFinder"/>'s to work out, as for a file a reply names.
/// </summary>
public static partial class MediaFiles
{
    /// <summary>At most this many paths are taken from one text, so a long paste of a log doesn't look for hundreds.</summary>
    public const int MaxPerText = 20;

    private static readonly Dictionary<string, MediaKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images: those Deventry's image window shows (ViewerImages), then those the OS's app does.
        [".png"] = MediaKind.Image, [".jpg"] = MediaKind.Image, [".jpeg"] = MediaKind.Image, [".gif"] = MediaKind.Image,
        [".webp"] = MediaKind.Image, [".bmp"] = MediaKind.Image, [".svg"] = MediaKind.Image, [".tif"] = MediaKind.Image,
        [".tiff"] = MediaKind.Image, [".ico"] = MediaKind.Image, [".heic"] = MediaKind.Image, [".heif"] = MediaKind.Image,
        [".avif"] = MediaKind.Image, [".psd"] = MediaKind.Image, [".tga"] = MediaKind.Image, [".exr"] = MediaKind.Image,
        // Video.
        [".mp4"] = MediaKind.Video, [".m4v"] = MediaKind.Video, [".mov"] = MediaKind.Video, [".webm"] = MediaKind.Video,
        [".mkv"] = MediaKind.Video, [".avi"] = MediaKind.Video, [".wmv"] = MediaKind.Video, [".mpg"] = MediaKind.Video,
        [".mpeg"] = MediaKind.Video, [".ogv"] = MediaKind.Video,
        // Sound.
        [".mp3"] = MediaKind.Audio, [".wav"] = MediaKind.Audio, [".ogg"] = MediaKind.Audio, [".oga"] = MediaKind.Audio,
        [".flac"] = MediaKind.Audio, [".m4a"] = MediaKind.Audio, [".aac"] = MediaKind.Audio, [".opus"] = MediaKind.Audio,
        [".aif"] = MediaKind.Audio, [".aiff"] = MediaKind.Audio,
    };

    /// <summary>What the file at <paramref name="path"/> is, by its extension; null for anything that isn't media.</summary>
    public static MediaKind? KindOf(string path) =>
        Kinds.TryGetValue(Path.GetExtension(Path.TrimEndingDirectorySeparator(path)), out var kind) ? kind : null;

    /// <summary>
    /// The media files a message or a reply names, as it wrote them, each once, in order: a word that's a path or a name
    /// with a media extension (<c>shots/bug.png</c>, <c>bug.png</c>, <c>C:\Shots\bug.png</c>, <c>~/Desktop/clip.mp4</c>),
    /// one in quotes or backticks (<c>"My Shots/bug.png"</c>), an <c>@</c> mention (<c>@shots/bug.png</c>,
    /// <c>@"My Shots/bug.png"</c>), a Markdown link's or image's address (<c>![chart](out/chart.png)</c>) and a
    /// <c>file:</c> address. Web addresses and network paths aren't. At most <see cref="MaxPerText"/>.
    /// </summary>
    public static IReadOnlyList<ReplyFileReference> InText(string text) => Find(text, isCommand: false);

    /// <summary>
    /// The media files a shell command names (<c>ffmpeg -i in.mp4 out.gif</c>, <c>python plot.py --out=chart.png</c>,
    /// <c>convert "a b.png" c.jpg</c>), as <see cref="InText"/> finds them, with an option's value after its <c>=</c>, and
    /// a quoted argument the whole file even with a space in its name, as the shell has it.
    /// </summary>
    public static IReadOnlyList<ReplyFileReference> InCommand(string command) => Find(command.Replace('=', ' '), isCommand: true);

    private static List<ReplyFileReference> Find(string text, bool isCommand)
    {
        var found = new List<ReplyFileReference>();
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // A quote that's a path ("My Shots/bug.png") is one; the words of one that isn't are looked at, since
        // "see shot.png" names shot.png.
        var quoted = new List<(int Start, int End)>();
        var candidates = new List<(int Index, ReplyFileReference Reference)>();
        foreach (Match match in QuotedPattern().Matches(text))
        {
            if (Reference(match.Groups["path"].Value, whole: isCommand) is { } reference)
            {
                candidates.Add((match.Index, reference));
                quoted.Add((match.Index, match.Index + match.Length));
            }
        }
        foreach (Match match in WordPattern().Matches(text))
        {
            if (!quoted.Any(q => match.Index >= q.Start && match.Index < q.End) && Reference(Word(match.Value)) is { } reference)
            {
                candidates.Add((match.Index, reference));
            }
        }
        foreach (var (_, reference) in candidates.OrderBy(c => c.Index))
        {
            if (found.Count >= MaxPerText)
            {
                break;
            }
            if (seen.Add(reference.Path))
            {
                found.Add(reference);
            }
        }
        return found;
    }

    /// <summary>
    /// The file <paramref name="candidate"/> names, when it's a path or a name with a media extension. A
    /// <paramref name="whole"/> one, a command's quoted argument, is a file even with a space in its name.
    /// </summary>
    private static ReplyFileReference? Reference(string candidate, bool whole = false) =>
        candidate.Length > 0 && KindOf(candidate) is not null
            && (whole ? ReplyFileReference.FromLink(candidate) : ReplyFileReference.FromCode(candidate)) is { } reference && KindOf(reference.Path) is not null
            ? reference
            : null;

    /// <summary>A word without the <c>@</c> of a mention or the punctuation of the sentence it ends.</summary>
    private static string Word(string word) => word.TrimStart('@').TrimEnd('.', ',', ';', ':', '!', '?');

    /// <summary>Text in double quotes, single quotes or backticks on one line, an <c>@</c> mention's among them.</summary>
    [GeneratedRegex("@?\"(?<path>[^\"\\r\\n]+)\"|'(?<path>[^'\\r\\n]+)'|`(?<path>[^`\\r\\n]+)`")]
    private static partial Regex QuotedPattern();

    /// <summary>A run of characters that can be in a path and aren't quotes, brackets or a pipe.</summary>
    [GeneratedRegex(@"[^\s""'`<>|()\[\]{}]+")]
    private static partial Regex WordPattern();
}
