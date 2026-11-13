using System.Text.Json.Nodes;

namespace Deventry.Core.Tickets;

/// <summary>How Deventry signs in to a tracker's server (DESIGN.md §18, "Tickets", "Signing in").</summary>
public enum TicketSignIn
{
    /// <summary>Not at all: GitHub's public repositories, YouTrack's guest.</summary>
    None,

    /// <summary>A token the user gives, kept in the OS credential store.</summary>
    Token,

    /// <summary>The GitHub CLI's: <c>gh auth token</c>, never stored.</summary>
    GitHubCli,

    /// <summary>Git Credential Manager's: <c>git credential fill</c>, never stored.</summary>
    GitCredentialManager,

    /// <summary>Sign in with GitHub: its device flow, the token kept as a token is.</summary>
    Device,
}

/// <summary>What a tracker is made with: Deventry's requests, its clock, and the server's token.</summary>
/// <param name="Http">For its requests: <c>AppServices.Http</c>, which tests and the demo replace.</param>
/// <param name="Server">The server, as <see cref="TicketServers.Normalize"/> writes it.</param>
/// <param name="Token">Its token; null to ask without signing in.</param>
public sealed record TicketTrackerContext(HttpClient Http, TimeProvider Time, string Server, string? Token, string UserAgent);

/// <summary>
/// A ticket provider (DESIGN.md §18, "Tickets"), as a project type is a kind of project: what Settings says about it,
/// how a project's files name its sources and what's found from git, and how to make a tracker for one of its servers.
/// Deventry lists the providers it knows, and Settings → Tickets has a page for each, drawn from what it declares.
/// GitHub's is <c>GitHubTicketsType</c> and YouTrack's <c>YouTrackTicketsType</c>.
/// </summary>
public interface ITicketProviderType
{
    /// <summary>A stable id, such as <c>github</c>: <c>provider</c> in <c>deventry.json</c>, its settings' key and its secrets'.</summary>
    string Kind { get; }

    /// <summary>"GitHub": its page in Settings, and <b>Open on GitHub</b>.</summary>
    string Name { get; }

    /// <summary>One line under its page's title.</summary>
    string Description { get; }

    /// <summary>What it calls a ticket, mid-sentence: "issue", for <i>GitHub issue #142</i>.</summary>
    string TicketWord { get; }

    /// <summary>Other words search finds its page by.</summary>
    IReadOnlyList<string> Keywords { get; }

    // ---- Its servers ----------------------------------------------------------------------------------------------

    /// <summary>The server it has without being added, <c>github.com</c>; null when every server is added (YouTrack).</summary>
    string? DefaultServer { get; }

    /// <summary>A server is written with its scheme and path (YouTrack's), rather than as a host (GitHub's).</summary>
    bool ServerHasScheme { get; }

    /// <summary>What an added server is, mid-sentence: "GitHub Enterprise server".</summary>
    string ServerWord { get; }

    /// <summary>The address box's placeholder: <c>github.studio.example</c>.</summary>
    string ServerPlaceholder { get; }

    /// <summary>The ways it signs in, in the order Settings offers them; the first is a new server's.</summary>
    IReadOnlyList<TicketSignIn> SignIns { get; }

    /// <summary>How to make a token, under its server's row.</summary>
    string TokenSteps { get; }

    /// <summary>The token box's placeholder: <c>github_pat_…</c>.</summary>
    string TokenPlaceholder { get; }

    // ---- Its sources -----------------------------------------------------------------------------------------------

    /// <summary>What a source's project is called: "Repository", "Project".</summary>
    string ProjectWord { get; }

    /// <summary>The project box's placeholder: <c>owner/name</c>.</summary>
    string ProjectPlaceholder { get; }

    /// <summary>A source can have a query that narrows its project's tickets (YouTrack's).</summary>
    bool HasQuery { get; }

    /// <summary>The filter box's tip: the tracker's search words, with examples.</summary>
    string FilterTip { get; }

    /// <summary>Why <paramref name="project"/> can't be a source's project, in words to show; null when it can.</summary>
    string? CheckProject(string project);

    /// <summary>
    /// A <c>tickets</c> entry of a project's files as a source of this provider, or null with why not in
    /// <paramref name="problem"/>, such as a GitHub entry with no repository.
    /// </summary>
    /// <param name="where">How the problem names the entry: <c>tickets[1]</c>.</param>
    TicketSource? ReadSource(JsonObject entry, TicketSourceOrigin origin, int index, string where, out string? problem);

    /// <summary>A source as a <c>tickets</c> entry: <paramref name="raw"/>'s other fields kept, its own written over them.</summary>
    JsonObject ToJson(TicketSource source, JsonNode? raw = null);

    /// <summary>
    /// The source a folder whose git remote is <paramref name="remoteUrl"/> has, when the remote is on one of
    /// <paramref name="servers"/>; null otherwise, and for a provider that finds none from git.
    /// </summary>
    TicketSource? FromRemote(string remoteUrl, IReadOnlyCollection<string> servers);

    /// <summary>Makes a tracker for one of its servers.</summary>
    ITicketTracker Connect(TicketTrackerContext context);
}

/// <summary>
/// One server of a provider's, signed in or not (DESIGN.md §18, "Tickets"): listing and reading its tickets, and
/// changing them for writing back. Every request has a deadline on the injected clock, and every failure is a
/// <see cref="TicketException"/> without the token in it.
/// </summary>
public interface ITicketTracker
{
    /// <summary>Who the token signs in as; null without one (GitHub), or as YouTrack's guest.</summary>
    Task<TicketAccount?> WhoAmIAsync(CancellationToken cancellationToken = default);

    /// <summary>A page of <paramref name="source"/>'s tickets: the first, or the one <paramref name="cursor"/> names.</summary>
    Task<TicketPage> ListAsync(TicketSource source, TicketQuery query, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>A ticket with its text, and its latest <paramref name="maxComments"/> comments.</summary>
    Task<TicketDetail> GetAsync(TicketSource source, string id, int maxComments, CancellationToken cancellationToken = default);

    /// <summary>The source's own list of tickets in the browser.</summary>
    string ListUrl(TicketSource source);

    /// <summary>Adds a comment, as Markdown.</summary>
    Task CommentAsync(TicketSource source, string id, string text, CancellationToken cancellationToken = default);

    /// <summary>The states <paramref name="ticket"/> can be put in now.</summary>
    Task<IReadOnlyList<TicketStateChoice>> StatesAsync(TicketSource source, Ticket ticket, CancellationToken cancellationToken = default);

    /// <summary>Puts a ticket in one of <see cref="StatesAsync"/>'s states.</summary>
    Task SetStateAsync(TicketSource source, string id, TicketStateChoice state, CancellationToken cancellationToken = default);

    /// <summary>Assigns a ticket to the account the tracker is signed in as.</summary>
    Task AssignToMeAsync(TicketSource source, string id, CancellationToken cancellationToken = default);
}
