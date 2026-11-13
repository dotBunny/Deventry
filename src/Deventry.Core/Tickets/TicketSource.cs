namespace Deventry.Core.Tickets;

/// <summary>Where a project's ticket source was found (DESIGN.md §18, "Tickets", "Where a project's tickets come from").</summary>
public enum TicketSourceOrigin
{
    /// <summary><c>tickets</c> in <c>deventry.json</c>, for everyone who opens the project.</summary>
    Shared,

    /// <summary><c>tickets</c> in <c>deventry.local.json</c>, the user's own.</summary>
    Local,

    /// <summary>The folder's git remote, with neither file naming any.</summary>
    Git,
}

/// <summary>
/// One place a project's tickets come from (DESIGN.md §18, "Tickets"): a GitHub repository, or a YouTrack project with
/// a query.
/// </summary>
/// <param name="Provider">Its provider's kind: <c>github</c>, <c>youtrack</c>.</param>
/// <param name="Server">Its server, as <see cref="TicketServers.Normalize"/> writes it: <c>github.com</c>, <c>https://dotbunny.youtrack.cloud</c>.</param>
/// <param name="Project">GitHub's <c>owner/name</c>, YouTrack's project's short name.</param>
/// <param name="Query">What narrows the project's tickets, in its tracker's words; null for all of them.</param>
/// <param name="Name">What the source menu calls it, when the entry names it.</param>
/// <param name="Index">Its place among its file's entries, for the project's settings; -1 for one found from git.</param>
public sealed record TicketSource(string Provider, string Server, string Project, string? Query, string? Name, TicketSourceOrigin Origin, int Index = -1)
{
    /// <summary>
    /// What it's known by from one launch to the next, whichever file it's in: <c>github:github.com/dotBunny/NightOwl</c>,
    /// <c>youtrack:https://dotbunny.youtrack.cloud/NO?#Unresolved</c>.
    /// </summary>
    public string Id => $"{Provider}:{Server}/{Project}{(Query is { Length: > 0 } q ? "?" + q : "")}";

    /// <summary>What the source menu and the project's settings call it: its name, else what it reads.</summary>
    public string DisplayName => Name is { Length: > 0 } name ? name : Query is { Length: > 0 } q ? $"{Project} · {q}" : Project;

    /// <summary>Its server, without its scheme, to say which one: <c>github.com</c>, <c>dotbunny.youtrack.cloud</c>.</summary>
    public string ServerName => TicketServers.Key(Server);
}

/// <summary>
/// The ticket a tab was started on (DESIGN.md §18, "Tickets", "Tabs remember their ticket"), kept with the tab through
/// restarts.
/// </summary>
public sealed class TicketRef
{
    /// <summary>Its provider's kind: <c>github</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Its source's <see cref="TicketSource.Id"/>.</summary>
    public string SourceId { get; set; } = "";

    /// <summary>As its tracker shows it: <c>#142</c>.</summary>
    public string Key { get; set; } = "";

    /// <summary>What its tracker's requests name it by: <c>142</c>.</summary>
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Its page in the browser.</summary>
    public string Url { get; set; } = "";

    /// <summary>What it's called with its tracker's word for it: <i>GitHub issue #142</i>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The same ticket: its source and its id, whatever its title is now.</summary>
    public bool Is(string sourceId, string id) =>
        string.Equals(SourceId, sourceId, StringComparison.OrdinalIgnoreCase) && string.Equals(Id, id, StringComparison.OrdinalIgnoreCase);

    public TicketRef Copy() => (TicketRef)MemberwiseClone();
}

/// <summary>A tracker's server as Deventry writes it, and keeps its secret and settings by.</summary>
public static class TicketServers
{
    /// <summary>
    /// <paramref name="text"/> as a server: with a scheme when <paramref name="withScheme"/> (YouTrack's
    /// <c>https://dotbunny.youtrack.cloud</c>, a path kept, no slash at the end), else only the host (GitHub's
    /// <c>github.com</c>), lowercase. Null when it isn't one: empty, with spaces, or not <c>https</c> or <c>http</c>.
    /// </summary>
    public static string? Normalize(string? text, bool withScheme)
    {
        var trimmed = text?.Trim().TrimEnd('/') ?? "";
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
        {
            return null;
        }
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Host.Length == 0
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }
        if (!withScheme)
        {
            return uri.IsDefaultPort ? uri.Host.ToLowerInvariant() : $"{uri.Host.ToLowerInvariant()}:{uri.Port}";
        }
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme}://{uri.Authority.ToLowerInvariant()}{path}";
    }

    /// <summary>A server without its scheme, as its secret and settings are kept by: <c>dotbunny.youtrack.cloud</c>.</summary>
    public static string Key(string server)
    {
        var scheme = server.IndexOf("://", StringComparison.Ordinal);
        return (scheme < 0 ? server : server[(scheme + 3)..]).TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>The same server, however it's written.</summary>
    public static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
}
