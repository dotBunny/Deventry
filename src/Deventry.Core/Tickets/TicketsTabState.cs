namespace Deventry.Core.Tickets;

/// <summary>
/// A group's Tickets tab, as it's kept with the app state (DESIGN.md §18, "Tickets"): its source, its list's filter, the
/// ticket it showed, and when its tickets were last looked at.
/// </summary>
public sealed class TicketsTabState
{
    /// <summary>Its id among the tabs, for the selection and the panes it's kept in.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The group's folder.</summary>
    public string Folder { get; set; } = "";

    /// <summary>The source chosen, by its <see cref="TicketSource.Id"/>; null for the first, or <b>All sources</b> once there are several.</summary>
    public string? SourceId { get; set; }

    /// <summary><b>All sources</b> is chosen.</summary>
    public bool AllSources { get; set; }

    public TicketStateFilter State { get; set; } = TicketStateFilter.Open;

    public bool Mine { get; set; }

    public TicketSort Sort { get; set; } = TicketSort.Newest;

    /// <summary>What the filter box had.</summary>
    public string Filter { get; set; } = "";

    /// <summary>The ticket it showed: its source's id and its id.</summary>
    public string? SelectedSourceId { get; set; }

    public string? SelectedId { get; set; }

    /// <summary>When it last showed its tickets: those opened since are new.</summary>
    public DateTimeOffset? LastSeen { get; set; }
}
