using System.Text.Json.Nodes;
using Deventry.Core.Git;
using Deventry.Core.ProjectTools;

namespace Deventry.Core.Tickets;

/// <summary>A project's ticket sources, and why entries of its files were skipped.</summary>
public sealed record ProjectTicketSources(IReadOnlyList<TicketSource> Sources, IReadOnlyList<string> Problems)
{
    public static ProjectTicketSources None { get; } = new([], []);
}

/// <summary>
/// Where a project's tickets come from (DESIGN.md §18, "Tickets", "Where a project's tickets come from"): the
/// <c>tickets</c> entries of its <c>deventry.json</c> and <c>deventry.local.json</c>, the shared file's first; and the
/// repository its git remote has on a server a provider knows, unless the files name one of that provider's or say
/// <c>"tickets": []</c>. Read from files only: no git runs, and nothing reaches a tracker.
/// </summary>
public static class TicketSources
{
    /// <summary>What a provider finds from git, and on which servers.</summary>
    /// <param name="Servers">Each provider's servers by its kind: GitHub's github.com and the Enterprise servers Settings has.</param>
    /// <param name="FromGit">Each provider finds a project's source from its git remote, by its kind.</param>
    public sealed record Detection(IReadOnlyDictionary<string, IReadOnlyCollection<string>> Servers, Func<string, bool> FromGit);

    /// <summary>The sources of <paramref name="folder"/>'s project.</summary>
    public static ProjectTicketSources Read(string folder, IReadOnlyList<ITicketProviderType> types, Detection detection)
    {
        var sources = new List<TicketSource>();
        var problems = new List<string>();
        var emptied = false;
        foreach (var (scope, origin) in new[] { (ProjectFileScope.Shared, TicketSourceOrigin.Shared), (ProjectFileScope.Local, TicketSourceOrigin.Local) })
        {
            var file = ProjectFile.ReadTickets(folder, scope);
            emptied |= file.HasKey && file.Entries.Count == 0;
            problems.AddRange(file.Problems);
            for (var i = 0; i < file.Entries.Count; i++)
            {
                if (ReadEntry(file.Entries[i], types, origin, i, $"{file.File}: tickets[{i}]", out var problem) is { } source)
                {
                    sources.Add(source);
                }
                else if (problem is not null)
                {
                    problems.Add(problem);
                }
            }
        }
        if (FromGitBeside(folder, sources, emptied, types, detection) is { } found)
        {
            sources.Add(found);
        }
        return new ProjectTicketSources(sources, problems);
    }

    /// <summary>
    /// The source found from git, beside <paramref name="named"/>, the files' own: a YouTrack project named keeps the
    /// GitHub repository found, but a GitHub repository named takes its place, and <c>"tickets": []</c>
    /// (<paramref name="emptied"/>) turns it off.
    /// </summary>
    public static TicketSource? FromGitBeside(string folder, IReadOnlyCollection<TicketSource> named, bool emptied, IReadOnlyList<ITicketProviderType> types,
        Detection detection) =>
        !emptied && FromGit(folder, types, detection) is { } found && !named.Any(s => string.Equals(s.Provider, found.Provider, StringComparison.OrdinalIgnoreCase))
            ? found
            : null;

    /// <summary>One <c>tickets</c> entry as a source, or null with why not.</summary>
    public static TicketSource? ReadEntry(JsonNode? node, IReadOnlyList<ITicketProviderType> types, TicketSourceOrigin origin, int index, string where, out string? problem)
    {
        problem = null;
        if (node is not JsonObject entry)
        {
            problem = $"{where} isn't an object, such as {{ \"provider\": \"github\", \"repository\": \"owner/name\" }}, so it was skipped.";
            return null;
        }
        if (LenientJson.String(entry, "provider")?.Trim() is not { Length: > 0 } provider)
        {
            problem = $"{where} has no provider, such as \"github\" or \"youtrack\", so it was skipped.";
            return null;
        }
        if (types.FirstOrDefault(t => string.Equals(t.Kind, provider, StringComparison.OrdinalIgnoreCase)) is not { } type)
        {
            problem = $"{where}: \"{provider}\" isn't a provider Deventry knows ({string.Join(", ", types.Select(t => t.Kind))}), so it was skipped.";
            return null;
        }
        return type.ReadSource(entry, origin, index, where, out problem);
    }

    /// <summary>The source found from <paramref name="folder"/>'s git remote, when a provider that finds one from git knows its server.</summary>
    public static TicketSource? FromGit(string folder, IReadOnlyList<ITicketProviderType> types, Detection detection)
    {
        if (ProjectIdentity.Read(folder)?.RemoteUrl is not { Length: > 0 } remote)
        {
            return null;
        }
        foreach (var type in types.Where(t => detection.FromGit(t.Kind)))
        {
            var servers = detection.Servers.TryGetValue(type.Kind, out var known) ? known : [];
            if (type.FromRemote(remote, servers) is { } source)
            {
                return source;
            }
        }
        return null;
    }
}
