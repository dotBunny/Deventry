using System.Text.Json.Nodes;
using Deventry.Core.ProjectTools;

namespace Deventry.Core.Tickets.GitHub;

/// <summary>
/// GitHub as a ticket provider (DESIGN.md §18, "Tickets"): a repository's issues, on github.com or a GitHub Enterprise
/// server, found from a folder's git remote unless its files name its sources.
/// <code>{ "provider": "github", "repository": "dotBunny/NightOwl", "server": "github.studio.example" }</code>
/// </summary>
public sealed class GitHubTicketsType : ITicketProviderType
{
    public const string KindId = "github";

    public const string GitHubCom = "github.com";

    public string Kind => KindId;

    public string Name => "GitHub";

    public string Description => "Issues from repositories on GitHub and GitHub Enterprise servers.";

    public string TicketWord => "issue";

    public IReadOnlyList<string> Keywords { get; } = ["github", "issues", "enterprise", "token", "gh", "credential manager", "repository"];

    public string? DefaultServer => GitHubCom;

    public bool ServerHasScheme => false;

    public string ServerWord => "GitHub Enterprise server";

    public string ServerPlaceholder => "github.studio.example";

    public IReadOnlyList<TicketSignIn> SignIns { get; } = GitHubDeviceFlow.IsAvailable
        ? [TicketSignIn.None, TicketSignIn.Token, TicketSignIn.GitHubCli, TicketSignIn.GitCredentialManager, TicketSignIn.Device]
        : [TicketSignIn.None, TicketSignIn.Token, TicketSignIn.GitHubCli, TicketSignIn.GitCredentialManager];

    public string TokenSteps =>
        "Make a fine-grained token on GitHub (Settings → Developer settings → Personal access tokens) with Issues: Read-only on the repositories you want, "
        + "or Read and write to change tickets from Deventry. A classic token needs repo for private repositories.";

    public string TokenPlaceholder => "github_pat_…";

    public string ProjectWord => "Repository";

    public string ProjectPlaceholder => "owner/name";

    public bool HasQuery => false;

    public string FilterTip => "Filter the issues, or search with GitHub's words: label:bug, author:kim, no:assignee, milestone:\"0.4\"";

    public string? CheckProject(string project) =>
        GitHubRemote.IsRepository(project) ? null : "A repository is its owner and name, such as dotBunny/NightOwl.";

    public TicketSource? ReadSource(JsonObject entry, TicketSourceOrigin origin, int index, string where, out string? problem)
    {
        problem = null;
        if (LenientJson.String(entry, "repository")?.Trim() is not { Length: > 0 } repository)
        {
            problem = $"{where} has no repository, such as \"dotBunny/NightOwl\", so it was skipped.";
            return null;
        }
        if (CheckProject(repository) is { } wrong)
        {
            problem = $"{where}: \"{repository}\" isn't a repository. {wrong}";
            return null;
        }
        var server = GitHubCom;
        if (LenientJson.String(entry, "server")?.Trim() is { Length: > 0 } written)
        {
            if (TicketServers.Normalize(written, withScheme: false) is not { } host)
            {
                problem = $"{where}: \"{written}\" isn't a server's address, such as \"github.studio.example\", so it was skipped.";
                return null;
            }
            server = host;
        }
        return new TicketSource(KindId, server, repository, null, LenientJson.String(entry, "name")?.Trim() is { Length: > 0 } name ? name : null, origin, index);
    }

    public JsonObject ToJson(TicketSource source, JsonNode? raw = null)
    {
        var entry = raw?.DeepClone() as JsonObject ?? [];
        entry["provider"] = KindId;
        entry["repository"] = source.Project;
        entry.Remove("project");
        entry.Remove("query");
        if (TicketServers.Same(source.Server, GitHubCom))
        {
            entry.Remove("server");
        }
        else
        {
            entry["server"] = source.Server;
        }
        if (source.Name is { Length: > 0 } name)
        {
            entry["name"] = name;
        }
        else
        {
            entry.Remove("name");
        }
        return entry;
    }

    public TicketSource? FromRemote(string remoteUrl, IReadOnlyCollection<string> servers) =>
        GitHubRemote.Parse(remoteUrl) is { } remote && servers.Any(s => TicketServers.Same(s, remote.Server))
            ? new TicketSource(KindId, remote.Server, remote.Repository, null, null, TicketSourceOrigin.Git)
            : null;

    public ITicketTracker Connect(TicketTrackerContext context) => new GitHubApi(context);
}
