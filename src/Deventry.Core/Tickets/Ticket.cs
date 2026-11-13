namespace Deventry.Core.Tickets;

/// <summary>
/// One ticket in a list (DESIGN.md §18, "Tickets"): a GitHub issue, a YouTrack issue. What a row of the Tickets tab
/// shows, and what's enough to read the rest with.
/// </summary>
/// <param name="Key">As its tracker shows it: <c>#142</c>, <c>NO-42</c>.</param>
/// <param name="Id">What its tracker's requests name it by: <c>142</c>, <c>NO-42</c>.</param>
/// <param name="IsOpen">Not closed or resolved.</param>
/// <param name="StateName">Its state in its tracker's words: <i>Open</i>, <i>Closed as not planned</i>, <i>In Progress</i>.</param>
/// <param name="Labels">GitHub's labels, YouTrack's tags.</param>
/// <param name="Author">Who opened it, by the name its tracker shows; null when it doesn't say.</param>
/// <param name="Comments">How many comments it has.</param>
/// <param name="Url">Its page in the browser.</param>
/// <param name="Assignees">Who it's assigned to, by their names.</param>
public sealed record Ticket(
    string Key,
    string Id,
    string Title,
    bool IsOpen,
    string StateName,
    IReadOnlyList<string> Labels,
    string? Author,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    int Comments,
    string Url,
    IReadOnlyList<string> Assignees);

/// <summary>A ticket with its text and comments, as the Tickets tab shows one and <b>Work on it</b> sends it.</summary>
/// <param name="Body">Its text, as Markdown; empty when it has none.</param>
/// <param name="Fields">Its other fields, each with one value: GitHub's milestone, YouTrack's Type or Priority.</param>
/// <param name="Comments">Its comments, oldest first: at most the number asked for, the latest.</param>
public sealed record TicketDetail(Ticket Ticket, string Body, IReadOnlyList<TicketField> Fields, IReadOnlyList<TicketComment> Comments)
{
    /// <summary>Comments it has that weren't read, the oldest.</summary>
    public int CommentsLeftOut => Math.Max(0, Ticket.Comments - Comments.Count);
}

/// <summary>A field of a ticket's, with its value as text: <c>Milestone: 0.4</c>, <c>Type: Bug</c>.</summary>
public sealed record TicketField(string Name, string Value);

/// <summary>A comment on a ticket, as Markdown.</summary>
public sealed record TicketComment(string Author, DateTimeOffset Created, string Body);

/// <summary>Which tickets a list has, by whether they're open.</summary>
public enum TicketStateFilter
{
    Open,
    Closed,
    All,
}

/// <summary>The order a list is in.</summary>
public enum TicketSort
{
    /// <summary>The newest first, by when they were opened.</summary>
    Newest,

    /// <summary>The most recently updated first.</summary>
    Updated,

    /// <summary>The oldest first.</summary>
    Oldest,
}

/// <summary>What a list asks its tracker for.</summary>
/// <param name="Mine">Only those assigned to the account it's signed in as.</param>
/// <param name="Text">The filter box: its tracker's own search words. Empty for the tracker's list rather than a search.</param>
public sealed record TicketQuery(TicketStateFilter State = TicketStateFilter.Open, bool Mine = false, string Text = "", TicketSort Sort = TicketSort.Newest)
{
    public static TicketQuery Default { get; } = new();
}

/// <summary>A page of a list, with how to ask for the next; <paramref name="Next"/> is null at the end.</summary>
/// <param name="Next">What its tracker needs for the next page: GitHub's <c>Link</c> address, YouTrack's <c>$skip</c>.</param>
public sealed record TicketPage(IReadOnlyList<Ticket> Tickets, string? Next);

/// <summary>Who a tracker's server is signed in as: <c>@kim</c>.</summary>
/// <param name="Login">What it's known by in requests: <c>kim</c>.</param>
/// <param name="Name">What it's called, when it has a name besides its login.</param>
public sealed record TicketAccount(string Login, string? Name);

/// <summary>
/// A state a ticket can be put in (DESIGN.md §18, "Tickets", "Writing back"): GitHub's <b>Close as completed</b>,
/// <b>Close as not planned</b> and <b>Reopen</b>; a YouTrack project's states.
/// </summary>
/// <param name="Label">What the menu says: <i>Close as completed</i>, <i>In Progress</i>.</param>
/// <param name="Value">What the tracker is sent: GitHub's <c>completed</c>, YouTrack's state name.</param>
/// <param name="IsResolved">It closes or resolves the ticket.</param>
public sealed record TicketStateChoice(string Label, string Value, bool IsResolved);
