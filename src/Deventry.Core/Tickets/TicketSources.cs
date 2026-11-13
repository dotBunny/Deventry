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
/// <c>tickets</c> entries of its <c>deventry.json</c> and <c>deventry.local.json</c>, the shared file's first; or, with
/// neither naming any, the repository its git remote has on a server a provider knows. Read from files only: no git
/// runs, and nothing reaches a tracker.
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
        var named = false;
        foreach (var (scope, origin) in new[] { (ProjectFileScope.Shared, TicketSourceOrigin.Shared), (ProjectFileScope.Local, TicketSourceOrigin.Local) })
        {
            var file = ProjectFile.ReadTickets(folder, scope);
            named |= file.HasKey;
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
        if (!named && FromGit(folder, types, detection) is { } found)
        {
            sources.Add(found);
        }
        return new ProjectTicketSources(sources, problems);
    }

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
