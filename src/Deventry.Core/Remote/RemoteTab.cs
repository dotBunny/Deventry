namespace Deventry.Core.Remote;

/// <summary>What a tab is doing, as a remote client shows it (DESIGN.md §18, "Remote clients").</summary>
public enum RemoteTabStatus
{
    NotStarted,
    Starting,
    Idle,
    Working,
    /// <summary>A prompt, question or plan is waiting for an answer.</summary>
    NeedsInput,
    /// <summary>Finished its turn, and nobody has looked at it since.</summary>
    Unread,
    /// <summary>Its agent stopped with an error, or couldn't start.</summary>
    Error,
    Exited,
}

/// <summary>Whether a tab is a thread, one of a thread's sub-threads, or neither (DESIGN.md §18, "Threads").</summary>
public enum RemoteThreadRole
{
    None,
    Thread,
    SubThread,
}

/// <summary>Where a tab's task list stands: how many of its tasks are done.</summary>
public sealed record RemoteTaskProgress(int Done, int Total);

/// <summary>
/// One tab as a remote client sees it: a snapshot, taken on the UI thread, that a client may keep and read on any
/// thread.
/// </summary>
public sealed record RemoteTab
{
    public required string Id { get; init; }

    /// <summary>The tab's name, as its row in the sidebar shows it.</summary>
    public required string Name { get; init; }

    /// <summary>Its group's name: the project, or the folder's name.</summary>
    public required string Project { get; init; }

    /// <summary>The git branch it's on, when it's in a repository.</summary>
    public string? Branch { get; init; }

    /// <summary>The agent running in it, as the messages name it: "Claude".</summary>
    public string Agent { get; init; } = "Claude";

    /// <summary>The model it runs, as the composer names it; null before it's known.</summary>
    public string? Model { get; init; }

    public RemoteTabStatus Status { get; init; }

    /// <summary>What it's doing now, as its working line says it: "Reading Player.cs". Null when it isn't working.</summary>
    public string? Doing { get; init; }

    /// <summary>When its turn started, while it's working.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>How full its context is, from 0 to 1; null before it's known.</summary>
    public double? Context { get; init; }

    /// <summary>Its task list's progress, when it has one.</summary>
    public RemoteTaskProgress? Tasks { get; init; }

    public RemoteThreadRole Thread { get; init; }

    /// <summary>The prompt waiting in it, oldest first; null when none is.</summary>
    public RemotePrompt? Prompt { get; init; }

    /// <summary>The session continued on another machine: it can be read, not used.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>It stopped or exited, and can be started again.</summary>
    public bool CanRestart { get; init; }
}

/// <summary>A file a turn, or the session, changed, with its lines added and removed.</summary>
/// <param name="Path">Its path, relative to the tab's folder when it's inside it.</param>
public sealed record RemoteFileChange(string Path, int Added, int Removed, bool IsNew = false, bool IsDeleted = false);

/// <summary>A tab's last reply, in full, as Markdown.</summary>
public sealed record RemoteReply(string TabId, string TabName, string Markdown, DateTimeOffset? At);

/// <summary>A folder a tab can be opened in: a favorite, or one used lately (DESIGN.md §4, "Opening a tab").</summary>
public sealed record RemoteFolder(string Path, string Name, bool IsFavorite);

/// <summary>One of the plan's usage meters (DESIGN.md §6): "Session", 42% used, resetting at 15:00.</summary>
public sealed record RemoteUsageMeter(string Name, double Percent, DateTimeOffset? ResetsAt);

/// <summary>An account's spend today, in its currency's words (DESIGN.md §4, "Accounts").</summary>
public sealed record RemoteSpend(string Account, string Today);

/// <summary>The plan's usage and the accounts' spend, for <c>/usage</c>.</summary>
public sealed record RemoteUsage(IReadOnlyList<RemoteUsageMeter> Meters, IReadOnlyList<RemoteSpend> Spend)
{
    public static RemoteUsage None { get; } = new([], []);
}
