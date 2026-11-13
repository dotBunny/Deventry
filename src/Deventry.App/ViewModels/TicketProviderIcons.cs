using Deventry.Core.Tickets.GitHub;
using Deventry.Core.Tickets.YouTrack;

namespace Deventry.App.ViewModels;

/// <summary>Each ticket provider's icon in Settings' sidebar (DESIGN.md §18, "Tickets"): Deventry's own drawings, not the providers' logos.</summary>
public static class TicketProviderIcons
{
    /// <summary>All providers', the Tickets tab's, and a provider's without an icon of its own: a ticket.</summary>
    public const string Any = "IconTicket";

    public static string For(string kind) => kind switch
    {
        GitHubTicketsType.KindId => "IconTicketGitHub",
        YouTrackTicketsType.KindId => "IconTicketYouTrack",
        _ => Any,
    };
}
