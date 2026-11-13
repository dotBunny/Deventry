namespace Deventry.Core.Tickets;

/// <summary>What went wrong asking a tracker (DESIGN.md §18, "Tickets", "When something goes wrong").</summary>
public enum TicketErrorKind
{
    /// <summary>Anything else: the tracker's own words say what.</summary>
    Other,

    /// <summary>The tracker couldn't be reached, or didn't answer in time.</summary>
    Unreachable,

    /// <summary>The token was refused: <c>401</c>.</summary>
    Refused,

    /// <summary>The account may not do it: <c>403</c>, such as a token that may only read.</summary>
    Forbidden,

    /// <summary>The repository, project or ticket isn't there, or is private to this account: <c>404</c>.</summary>
    NotFound,

    /// <summary>The tracker's limit is used up until <see cref="TicketException.Until"/>.</summary>
    RateLimited,
}

/// <summary>
/// A tracker's request failed, in words to show (DESIGN.md §18, "Tickets"). Its message never holds a secret: a token
/// that an error would quote is taken out first.
/// </summary>
public sealed class TicketException(TicketErrorKind kind, string message, DateTimeOffset? until = null, Exception? inner = null) : Exception(message, inner)
{
    public TicketErrorKind Kind { get; } = kind;

    /// <summary>When a used-up limit is reset; null when it isn't one, or the tracker didn't say.</summary>
    public DateTimeOffset? Until { get; } = until;
}
