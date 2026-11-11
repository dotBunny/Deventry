using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using Deventry.App.Services;
using Deventry.Core.Media;
using Deventry.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.Conversation;

/// <summary>Whose a piece of media is in the Media window (DESIGN.md §18, "Media").</summary>
public enum MediaSource
{
    /// <summary>The user's: attached to a message, or named in one.</summary>
    User,

    /// <summary>Claude's, or a subagent's: an image a tool handed it, a file a tool wrote or read, or one a reply names.</summary>
    Agent,
}

/// <summary>
/// An image, video or sound in the Media window (DESIGN.md §18, "Media"): an image in the conversation itself (attached,
/// or in a tool's result), or a media file the conversation named that's on disk.
/// </summary>
public sealed partial class MediaEntry : ObservableObject
{
    internal MediaEntry(MediaKind kind, string name, string origin, string? path, ToolImage? image, ConversationItem? item, DateTimeOffset? at, string key, long order)
    {
        Kind = kind;
        Name = name;
        Origin = origin;
        Path = path;
        Image = image;
        Item = item;
        At = at;
        Key = key;
        Order = order;
    }

    public MediaKind Kind { get; }

    public bool IsImage => Kind == MediaKind.Image;

    public bool IsVideo => Kind == MediaKind.Video;

    public bool IsAudio => Kind == MediaKind.Audio;

    /// <summary>The icon its tile shows without a thumbnail: a picture, a film or a note.</summary>
    public string KindIcon => Kind switch
    {
        MediaKind.Video => "IconVideo",
        MediaKind.Audio => "IconAudio",
        _ => "IconImage",
    };

    /// <summary>What it's called: its file's name, or the name its tool's image has (<i>report.pdf, page 3</i>, <i>Image</i>).</summary>
    public string Name { get; }

    /// <summary>Where it came from: <i>Attached</i>, <i>Named in your message</i>, the tool (<i>Read</i>, <i>Bash</i>) or <i>Named in a reply</i>.</summary>
    public string Origin { get; }

    /// <summary>Its file's full path; null for an image that's only in the conversation, such as an attachment or a screenshot.</summary>
    public string? Path { get; }

    public bool IsFile => Path is not null;

    /// <summary>The image itself, as the conversation has it, for one that's in it; null for a file named.</summary>
    public ToolImage? Image { get; }

    /// <summary>The message, tool call or reply it came from, which <b>Show in conversation</b> goes to.</summary>
    public ConversationItem? Item { get; }

    public bool CanShowInConversation => Item is not null;

    /// <summary>When it came: the message's or the call's time.</summary>
    public DateTimeOffset? At { get; }

    /// <summary>What makes it the same as another in its section: its file, or its bytes.</summary>
    internal string Key { get; }

    /// <summary>Where it came in the conversation: a later one is newer, and shows first.</summary>
    internal long Order { get; }

    /// <summary>A small image for its tile, made off the UI thread; null until then, and for video and sound.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail), nameof(HasNoThumbnail))]
    public partial byte[]? Thumbnail { get; internal set; }

    public bool HasThumbnail => Thumbnail is not null;

    public bool HasNoThumbnail => Thumbnail is null;

    /// <summary>"Open bug.png", its tile's name for a screen reader.</summary>
    public string OpenName => $"Open {Name}";

    /// <summary>Its tile's tooltip: its name, where it came from and when, and its file.</summary>
    public string Tip { get; internal set; } = "";
}

/// <summary>A section of the Media window, the user's or the agent's (DESIGN.md §18, "Media"), which folds to its header.</summary>
public sealed partial class MediaSection : ObservableObject
{
    private readonly Dictionary<string, MediaEntry> _byKey = new(StringComparer.Ordinal);

    internal MediaSection(MediaSource source, string title, string emptyText)
    {
        Source = source;
        Title = title;
        EmptyText = emptyText;
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowsEmptyText));
        };
    }

    public MediaSource Source { get; }

    /// <summary><i>User</i> or <i>Agent</i>.</summary>
    public string Title { get; }

    /// <summary>What it says while it has nothing.</summary>
    public string EmptyText { get; }

    /// <summary>Newest first.</summary>
    public ObservableCollection<MediaEntry> Entries { get; } = [];

    public int Count => Entries.Count;

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    public bool IsEmpty => Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyText), nameof(ToggleName))]
    public partial bool IsExpanded { get; set; } = true;

    public bool ShowsEmptyText => IsExpanded && IsEmpty;

    /// <summary>Its header's name for a screen reader: <i>Collapse User</i>, <i>Expand Agent</i>.</summary>
    public string ToggleName => $"{(IsExpanded ? "Collapse" : "Expand")} {Title}";

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>
    /// Puts <paramref name="entry"/> where its order says, newest first. The same file or image seen again takes the place
    /// of the one before, so a file Claude made again shows once, as it is now; one older than what's there is dropped.
    /// Returns whether it went in.
    /// </summary>
    internal bool Put(MediaEntry entry)
    {
        if (_byKey.TryGetValue(entry.Key, out var known))
        {
            if (known.Order > entry.Order)
            {
                return false;
            }
            Entries.Remove(known);
        }
        _byKey[entry.Key] = entry;
        var at = 0;
        while (at < Entries.Count && Entries[at].Order > entry.Order)
        {
            at++;
        }
        Entries.Insert(at, entry);
        return true;
    }

    internal void Clear()
    {
        _byKey.Clear();
        Entries.Clear();
    }
}

/// <summary>
/// The Media window's gallery (DESIGN.md §18, "Media"): the images, video and sound in a session's conversation, in two
/// sections. <b>User</b>: images attached to a message, and the media files a message names. <b>Agent</b>: the images in
/// a tool's result (a Read of an image, a PDF's pages, an MCP tool's screenshot), and the media files a tool wrote, read or
/// ran a command on, or a reply names, Claude's and its subagents'. <see cref="ConversationBuilder"/> tells it as the
/// conversation is built, read back, cleared and rewound. A file named is looked for off the UI thread, only where that
/// can't reach another machine (<see cref="ReplyFileFinder"/>), and counts once it's there.
/// </summary>
public sealed partial class MediaGallery : ObservableObject
{
    // The conversation it belongs to was cleared since a file was looked for, or a thumbnail made: drop what comes back.
    private int _generation;
    private long _order;
    private Task _thumbnails = Task.CompletedTask;

    public MediaGallery()
    {
        User = new MediaSection(MediaSource.User, "User", "Images you attach, and the media files your messages name.");
        Agent = new MediaSection(MediaSource.Agent, "Agent", "Images Claude's tools see, and the media files Claude makes, reads or names.");
        Sections = [User, Agent];
        foreach (var section in Sections)
        {
            section.Entries.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAnything));
                OnPropertyChanged(nameof(Tip));
            };
        }
    }

    public MediaSection User { get; }

    public MediaSection Agent { get; }

    /// <summary>The user's, then the agent's.</summary>
    public IReadOnlyList<MediaSection> Sections { get; }

    /// <summary>The window's icon shows while there's anything (DESIGN.md §3, "Tool windows").</summary>
    public bool HasAnything => User.Count + Agent.Count > 0;

    /// <summary>What the icon's tip says after the title: <i>User 2 · Agent 5</i>.</summary>
    public string Tip => string.Create(CultureInfo.CurrentCulture, $"User {User.Count} · Agent {Agent.Count}");

    /// <summary>Says what "today" is in a tile's tooltip.</summary>
    public TimeProvider? Time { get; set; }

    /// <summary>Looks for the file a reference names, where that can't reach another machine; the likeliest first.</summary>
    public Func<ReplyFileReference, Task<IReadOnlyList<string>>>? FindFile { get; set; }

    /// <summary>Makes a tile's thumbnail from an image's bytes, off the UI thread; none, and the image is its own.</summary>
    public Func<byte[], byte[]?>? ThumbnailOf { get; set; }

    /// <summary>Makes a tile's thumbnail from an image file, off the UI thread; none, and a file's tile has its kind's icon.</summary>
    public Func<string, byte[]?>? ThumbnailOfFile { get; set; }

    /// <summary>Where what's found off the UI thread goes back to it.</summary>
    public IUiDispatcher? Dispatcher { get; set; }

    /// <summary>Shows a path in a tile's tooltip: inside the tab's folder from there, else in full.</summary>
    public Func<string, string>? ShownPath { get; set; }

    /// <summary>The section for <paramref name="source"/>.</summary>
    public MediaSection SectionOf(MediaSource source) => source == MediaSource.User ? User : Agent;

    /// <summary>
    /// The user's message: the images attached to it (DESIGN.md §5, "Attachments"), then the media files it names, in
    /// <paramref name="named"/>.
    /// </summary>
    internal void AddMessage(UserMessageItem message, IReadOnlyList<ReplyFileReference> named, DateTimeOffset? at)
    {
        var images = message.Images;
        var first = Reserve(images.Count + named.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var name = images.Count == 1 ? "Attached image" : string.Create(CultureInfo.InvariantCulture, $"Attached image {i + 1}");
            var image = new ToolImage(images[i], "Your message", name, ToolImages.SaveName("attached-image", images[i].MediaType), null, null);
            AddImage(MediaSource.User, image, "Attached", message, at, first - i);
        }
        AddNamed(MediaSource.User, named, "Named in your message", message, at, first - images.Count);
    }

    /// <summary>
    /// A tool call whose result is back: the images in it (DESIGN.md §18, "Images from Claude's tools"), then the media
    /// files in <paramref name="named"/>, which it read, wrote or ran a command on.
    /// </summary>
    internal void AddToolResult(ToolUseItem tool, IReadOnlyList<ReplyFileReference> named)
    {
        var first = Reserve(tool.Images.Count + named.Count);
        for (var i = 0; i < tool.Images.Count; i++)
        {
            AddImage(MediaSource.Agent, tool.Images[i], tool.Name, tool, tool.StartedAt, first - i);
        }
        AddNamed(MediaSource.Agent, named, tool.Name, tool, tool.StartedAt, first - tool.Images.Count);
    }

    /// <summary>A whole reply, Claude's or a subagent's: the media files it names, in <paramref name="named"/>.</summary>
    internal void AddReply(AssistantTextItem reply, IReadOnlyList<ReplyFileReference> named) =>
        AddNamed(MediaSource.Agent, named, "Named in a reply", reply, reply.SentAt, Reserve(named.Count));

    /// <summary>
    /// Places for <paramref name="count"/> entries from one message, call or reply, after everything before it: the first
    /// is the one returned, and the rest count down from it, so they show in the order they came, above what's older.
    /// </summary>
    private long Reserve(int count)
    {
        _order += count;
        return _order;
    }

    /// <summary>
    /// The media files <paramref name="references"/> name, each once it's found on disk, at the places from
    /// <paramref name="first"/> down, however long the looking takes. <paramref name="origin"/> says where they're from.
    /// </summary>
    private void AddNamed(MediaSource source, IReadOnlyList<ReplyFileReference> references, string origin, ConversationItem item, DateTimeOffset? at, long first)
    {
        if (references.Count == 0 || FindFile is not { } find)
        {
            return;
        }
        var generation = _generation;
        for (var i = 0; i < references.Count; i++)
        {
            Task<IReadOnlyList<string>> finding;
            try
            {
                finding = find(references[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            _ = AddFoundAsync(finding, source, origin, item, at, first - i, generation);
        }
    }

    private async Task AddFoundAsync(Task<IReadOnlyList<string>> finding, MediaSource source, string origin, ConversationItem item, DateTimeOffset? at, long order, int generation)
    {
        string? path;
        try
        {
            // A folder named like a file isn't one.
            path = (await finding.ConfigureAwait(false)).FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return;
        }
        if (path is null || MediaFiles.KindOf(path) is not { } kind)
        {
            return;
        }
        Post(() =>
        {
            if (generation != _generation)
            {
                return;
            }
            var entry = new MediaEntry(kind, System.IO.Path.GetFileName(path), origin, path, null, item, at, PathKey(path), order);
            if (Put(source, entry) && kind == MediaKind.Image && ViewerImages.HasExtension(path) && ThumbnailOfFile is { } make)
            {
                MakeThumbnail(entry, () => make(path));
            }
        });
    }

    private void AddImage(MediaSource source, ToolImage image, string origin, ConversationItem item, DateTimeOffset? at, long order)
    {
        var key = image.FilePath is { } path ? PathKey(path) : BytesKey(image.Image.Data);
        var entry = new MediaEntry(MediaKind.Image, image.Name, origin, image.FilePath, image, item, at, key, order);
        if (!Put(source, entry))
        {
            return;
        }
        if (ThumbnailOf is { } make)
        {
            MakeThumbnail(entry, () => make(image.Image.Data));
        }
        else
        {
            entry.Thumbnail = image.Image.Data;
        }
    }

    private bool Put(MediaSource source, MediaEntry entry)
    {
        entry.Tip = TipOf(entry);
        return SectionOf(source).Put(entry);
    }

    /// <summary>"bug.png", then "Read · 14:02", then where its file is.</summary>
    private string TipOf(MediaEntry entry)
    {
        var when = entry.At is { } at && Time is { } clock ? $" · {MessageTimes.Short(at, clock)}" : "";
        var where = entry.Path is { } path ? $"\n{ShownPath?.Invoke(path) ?? path}" : "";
        return $"{entry.Name}\n{entry.Origin}{when}{where}";
    }

    /// <summary>
    /// Makes <paramref name="entry"/>'s thumbnail with the others, one at a time off the UI thread, so a transcript of many
    /// screenshots read back doesn't take every thread the pool has.
    /// </summary>
    private void MakeThumbnail(MediaEntry entry, Func<byte[]?> make)
    {
        var generation = _generation;
        _thumbnails = _thumbnails.ContinueWith(_ =>
        {
            byte[]? thumbnail;
            try
            {
                thumbnail = make();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return;
            }
            if (thumbnail is not null)
            {
                Post(() =>
                {
                    if (generation == _generation)
                    {
                        entry.Thumbnail = thumbnail;
                    }
                });
            }
        }, CancellationToken.None, TaskContinuationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>The conversation was cleared, or rewound and read back: it starts again.</summary>
    internal void Clear()
    {
        _generation++;
        foreach (var section in Sections)
        {
            section.Clear();
        }
    }

    private void Post(Action action)
    {
        if (Dispatcher is { } dispatcher)
        {
            dispatcher.Post(action);
        }
        else
        {
            action();
        }
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>A file's key: its full path, as the OS compares them.</summary>
    private static string PathKey(string path)
    {
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = path;
        }
        return "file:" + (PathComparer == StringComparer.OrdinalIgnoreCase ? full.ToUpperInvariant() : full);
    }

    /// <summary>An image's key when it has no file: its bytes, so the same screenshot or attachment twice shows once.</summary>
    private static string BytesKey(byte[] data) => "image:" + Convert.ToHexString(SHA256.HashData(data));
}
