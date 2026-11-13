using System.Text.Json.Nodes;
using Deventry.Core.ProjectTools;

namespace Deventry.Core.Tickets.YouTrack;

/// <summary>
/// YouTrack as a ticket provider (DESIGN.md §18, "Tickets"): a project's issues, narrowed by a query, in YouTrack Cloud
/// or on a server of the user's own. Every server is added in Settings; a project's files name its sources.
/// <code>{ "provider": "youtrack", "server": "https://dotbunny.youtrack.cloud", "project": "NO", "query": "#Unresolved" }</code>
/// </summary>
public sealed class YouTrackTicketsType : ITicketProviderType
{
    public const string KindId = "youtrack";

    public string Kind => KindId;

    public string Name => "YouTrack";

    public string Description => "Issues from YouTrack projects, in YouTrack Cloud or on a server of your own.";

    public string TicketWord => "issue";

    public IReadOnlyList<string> Keywords { get; } = ["youtrack", "jetbrains", "issues", "token", "server"];

    public string? DefaultServer => null;

    public bool ServerHasScheme => true;

    public string ServerWord => "YouTrack server";

    public string ServerPlaceholder => "https://dotbunny.youtrack.cloud";

    public IReadOnlyList<TicketSignIn> SignIns { get; } = [TicketSignIn.Token];

    public string TokenSteps =>
        "Make a permanent token in YouTrack: your profile → Account Security → New token…, with YouTrack's scope. Without one, Deventry reads what the server shows its guests.";

    public string TokenPlaceholder => "perm:…";

    public string ProjectWord => "Project";

    public string ProjectPlaceholder => "NO";

    public bool HasQuery => true;

    public string FilterTip => "Filter the issues, or search with YouTrack's query words: Type: Bug, #Critical, tag: regression, has: attachments";

    public string? CheckProject(string project) =>
        project.Trim().Length > 0 && !project.Contains('{', StringComparison.Ordinal) && !project.Contains('}', StringComparison.Ordinal)
            ? null
            : "A project is its short name, such as NO.";

    public TicketSource? ReadSource(JsonObject entry, TicketSourceOrigin origin, int index, string where, out string? problem)
    {
        problem = null;
        if (LenientJson.String(entry, "server")?.Trim() is not { Length: > 0 } written)
        {
            problem = $"{where} has no server, such as \"https://dotbunny.youtrack.cloud\", so it was skipped.";
            return null;
        }
        if (TicketServers.Normalize(written, withScheme: true) is not { } server)
        {
            problem = $"{where}: \"{written}\" isn't a server's address, such as \"https://dotbunny.youtrack.cloud\", so it was skipped.";
            return null;
        }
        if (LenientJson.String(entry, "project")?.Trim() is not { Length: > 0 } project || CheckProject(project) is not null)
        {
            problem = $"{where} has no project, such as \"NO\", so it was skipped.";
            return null;
        }
        var query = LenientJson.String(entry, "query")?.Trim() is { Length: > 0 } q ? q : null;
        return new TicketSource(KindId, server, project, query, LenientJson.String(entry, "name")?.Trim() is { Length: > 0 } name ? name : null, origin, index);
    }

    public JsonObject ToJson(TicketSource source, JsonNode? raw = null)
    {
        var entry = raw?.DeepClone() as JsonObject ?? [];
        entry["provider"] = KindId;
        entry["server"] = source.Server;
        entry["project"] = source.Project;
        entry.Remove("repository");
        Set("query", source.Query);
        Set("name", source.Name);
        return entry;

        void Set(string key, string? value)
        {
            if (value is { Length: > 0 })
            {
                entry[key] = value;
            }
            else
            {
                entry.Remove(key);
            }
        }
    }

    public bool FindsFromGit => false;

    /// <summary>YouTrack isn't found from git: a project's files name its sources.</summary>
    public TicketSource? FromRemote(string remoteUrl, IReadOnlyCollection<string> servers) => null;

    public ITicketTracker Connect(TicketTrackerContext context) => new YouTrackApi(context);
}
