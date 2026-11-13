namespace Deventry.Core.Tickets;

/// <summary>
/// Where a tracker's server keeps its token in the OS credential store (DESIGN.md §18, "Tickets", "Signing in"):
/// <c>tickets/github/github.com</c>. Never in settings, state, an address, an argument or a log, and never synced.
/// </summary>
public static class TicketSecrets
{
    /// <summary>The credential store's key for <paramref name="server"/>'s token.</summary>
    public static string Key(string kind, string server) => $"tickets/{kind}/{TicketServers.Key(server)}";

    /// <summary>What the OS shows for it: <i>Deventry: GitHub token for github.com</i>.</summary>
    public static string Label(string providerName, string server) => $"Deventry: {providerName} token for {TicketServers.Key(server)}";
}
