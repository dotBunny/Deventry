namespace Deventry.Core.Remote;

/// <summary>Where a remote client stands.</summary>
public enum RemoteClientStatus
{
    Off,
    Connecting,
    Connected,
    /// <summary>It stopped trying: the reason says why, and what to do.</summary>
    Failed,
}

/// <summary>
/// Something the user can do about why a client can't connect, which Settings offers as a button: Telegram's <b>Use it
/// for Deventry anyway</b> for a bot that has a webhook.
/// </summary>
/// <param name="Explanation">What it does, and what it changes elsewhere.</param>
public sealed record RemoteClientFix(string Label, string Explanation, Func<CancellationToken, Task> ApplyAsync);

/// <summary>A client's status, with why it isn't connected when it says.</summary>
public sealed record RemoteClientState(RemoteClientStatus Status, string? Reason = null)
{
    /// <summary>What the user can do about it, when there's something.</summary>
    public RemoteClientFix? Fix { get; init; }

    public static RemoteClientState Off { get; } = new(RemoteClientStatus.Off);

    public static RemoteClientState Connected { get; } = new(RemoteClientStatus.Connected);
}

/// <summary>
/// One way into Deventry from a phone or another app (DESIGN.md §18, "Remote clients"): a Telegram bot first. It reads
/// the desk, listens to its events and acts through it, and reaches nothing else. A Discord bot or a browser page would
/// be another, with nothing else changing.
/// </summary>
public interface IRemoteClient : IAsyncDisposable
{
    /// <summary>What it is, for settings and logs: <c>telegram</c>.</summary>
    string Kind { get; }

    /// <summary>Its name, as the desk's cards say it: <i>"Answered from Telegram"</i>.</summary>
    string Name { get; }

    RemoteClientState State { get; }

    /// <summary>Raised on any thread when <see cref="State"/> changes.</summary>
    event Action<RemoteClientState>? StateChanged;

    /// <summary>Its own account's name in its service, once the service has said: a bot's <c>@username</c>, without the @.</summary>
    string? Identity { get; }

    /// <summary>Raised on any thread when its service says what its account is called.</summary>
    event Action<string>? Identified;

    /// <summary>Starts listening, in the background: it returns at once, and <see cref="State"/> says how it goes.</summary>
    Task StartAsync(IRemoteDesk desk, CancellationToken cancellationToken = default);

    /// <summary>Stops listening, once what it's sending is sent.</summary>
    Task StopAsync();
}
