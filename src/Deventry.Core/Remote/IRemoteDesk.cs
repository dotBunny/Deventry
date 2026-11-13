namespace Deventry.Core.Remote;

/// <summary>What became of something a client asked the desk to do: done, or why not, in words to show.</summary>
/// <param name="TabId">The tab it was done in, for a tab it opened.</param>
public sealed record RemoteResult(bool Done, string? Reason = null, string? TabId = null)
{
    public static RemoteResult Ok { get; } = new(true);

    public static RemoteResult No(string reason) => new(false, reason);
}

/// <summary>How much of what happened a client's messages say (Settings → Remote clients → <b>What they say</b>).</summary>
public enum RemoteDetail
{
    /// <summary>The replies, commands, paths and files, as the desk shows them.</summary>
    Everything,

    /// <summary>Only the tab's name and what happened: "Grappling hook needs you."</summary>
    JustWhatHappened,
}

/// <summary>
/// What Deventry offers a remote client, and all a client can reach (DESIGN.md §18, "Remote clients"): the tabs as
/// snapshots, what can be done to them, and what happens at the desk. The App implements it over the shell's tabs; a
/// client never touches a tab, a session or Claude Code. Every method may be called from any thread, and the desk
/// checks what a client may do (<see cref="RemoteAccess"/>), so every client follows the same rule.
/// </summary>
public interface IRemoteDesk
{
    /// <summary>This machine's name, as Settings → Sessions gives it: "DESK".</summary>
    string MachineName { get; }

    /// <summary>What remote clients may do, as Settings → Remote clients says.</summary>
    RemoteAccessLevel Access { get; }

    /// <summary>Clients may open new tabs (<b>Open new tabs</b>).</summary>
    bool CanOpenTabs { get; }

    /// <summary>How much the messages say.</summary>
    RemoteDetail Detail { get; }

    /// <summary>Something happened. Raised on the UI thread: handlers queue their work and return.</summary>
    event Action<RemoteEvent>? EventRaised;

    /// <summary>The tabs, in the sidebar's order.</summary>
    Task<IReadOnlyList<RemoteTab>> GetTabsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="text"/> to a tab, as typing it at the desk would, from <paramref name="client"/> ("Telegram").</summary>
    Task<RemoteResult> SendAsync(string tabId, string text, string client, CancellationToken cancellationToken = default);

    /// <summary>Stops the tab's agent, as its <b>Stop</b> does.</summary>
    Task<RemoteResult> StopAsync(string tabId, string client, CancellationToken cancellationToken = default);

    /// <summary>Answers a waiting prompt with one of its options, and the user's words when the option takes them.</summary>
    Task<RemoteResult> AnswerAsync(string tabId, string promptId, string optionId, string? text, string client, CancellationToken cancellationToken = default);

    /// <summary>Starts a tab that stopped or exited again.</summary>
    Task<RemoteResult> RestartAsync(string tabId, string client, CancellationToken cancellationToken = default);

    /// <summary>The tab's last reply, in full; null when it has none or is gone.</summary>
    Task<RemoteReply?> GetLastReplyAsync(string tabId, CancellationToken cancellationToken = default);

    /// <summary>The files the tab's session changed; null when the tab is gone.</summary>
    Task<IReadOnlyList<RemoteFileChange>?> GetChangedFilesAsync(string tabId, CancellationToken cancellationToken = default);

    /// <summary>The plan's usage and the accounts' spend today.</summary>
    Task<RemoteUsage> GetUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>The favorite folders, then the recent ones, for opening a tab in.</summary>
    Task<IReadOnlyList<RemoteFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a tab in <paramref name="folder"/>, as the folder picker does; its id is the result's.</summary>
    Task<RemoteResult> OpenTabAsync(string folder, string client, CancellationToken cancellationToken = default);
}
