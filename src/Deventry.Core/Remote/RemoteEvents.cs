namespace Deventry.Core.Remote;

/// <summary>
/// Whether a client sends a message for an event, as Settings → Remote clients → <b>Messages</b> says (DESIGN.md §18,
/// "When remote clients send messages"). The desk decides, so every client follows the same rule.
/// </summary>
public enum RemoteNotice
{
    /// <summary>No message: the event is off, or the user is at the desk. A client may still update what it sent before.</summary>
    None,

    /// <summary>A message that doesn't make the phone buzz: <b>Always</b>, while the user is at the desk.</summary>
    Silent,

    /// <summary>A message, with the phone's sound: the user is away.</summary>
    Sound,
}

/// <summary>Something that happened at the desk, for the remote clients. Raised on the UI thread: a client mustn't block it.</summary>
public abstract record RemoteEvent
{
    public RemoteNotice Notice { get; init; }
}

/// <summary>A tab's status, what it's doing or its name changed.</summary>
public sealed record RemoteTabChanged(RemoteTab Tab) : RemoteEvent;

/// <summary>A tab closed.</summary>
public sealed record RemoteTabClosed(string TabId, string Name) : RemoteEvent;

/// <summary>
/// A prompt is waiting. Raised again for the prompts already waiting when the user goes away, so walking off with one
/// open still reaches the phone (<see cref="Again"/>).
/// </summary>
public sealed record RemotePromptWaiting(RemoteTab Tab, RemotePrompt Prompt) : RemoteEvent
{
    /// <summary>It was waiting before: the user just went away. A client that already sent it doesn't again.</summary>
    public bool Again { get; init; }
}

/// <summary>How a prompt closed.</summary>
public enum RemotePromptEnd
{
    AnsweredAtDesk,
    AnsweredFromClient,
    /// <summary>Its agent withdrew it, or the tab stopped or closed.</summary>
    Withdrawn,
}

/// <summary>A prompt closed: answered here, from a client, or withdrawn.</summary>
/// <param name="Outcome">What its card says now: "Allowed", "Denied: use the staging database".</param>
/// <param name="Client">The client that answered it, by name ("Telegram"), when one did.</param>
public sealed record RemotePromptClosed(string TabId, string PromptId, RemotePromptEnd How, string Outcome, string? Client, DateTimeOffset At) : RemoteEvent;

/// <summary>A turn finished.</summary>
/// <param name="Took">How long the turn took, when it's known.</param>
/// <param name="Tasks">Where the task list stands, when the turn changed it: "3 of 5 tasks done".</param>
/// <param name="Reply">The reply, as Markdown; null when it had none.</param>
/// <param name="Files">The files the turn changed.</param>
public sealed record RemoteTurnFinished(RemoteTab Tab, TimeSpan? Took, string? Tasks, string? Reply, IReadOnlyList<RemoteFileChange> Files) : RemoteEvent;

/// <summary>A tab's agent stopped with an error, or couldn't start, or needs a sign-in.</summary>
public sealed record RemoteTabFailed(RemoteTab Tab, string Message, bool NeedsSignIn = false) : RemoteEvent;

/// <summary>A usage alert (DESIGN.md §6, "Alerts") or an account's spend warning (§4, "Accounts").</summary>
public sealed record RemoteUsageAlert(string Title, string Body) : RemoteEvent;

/// <summary>A project action finished or failed (DESIGN.md §18, "Project tools").</summary>
public sealed record RemoteProjectAction(string? TabId, string Title, string Body) : RemoteEvent;

/// <summary>Deventry is closing: its tabs are kept, and it answers again when it's back.</summary>
public sealed record RemoteDeskClosing : RemoteEvent;
