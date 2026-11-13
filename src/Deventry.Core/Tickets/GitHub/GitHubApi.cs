using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace Deventry.Core.Tickets.GitHub;

/// <summary>
/// GitHub's REST API for a server's issues (DESIGN.md §18, "Tickets", "Requests"): <c>api.github.com</c>, or a GitHub
/// Enterprise server's <c>/api/v3</c>. Lists leave pull requests out; each list keeps its <c>ETag</c> and asks again
/// with <c>If-None-Match</c>, so an unchanged one costs a <c>304</c>; the next page is the <c>Link</c> header's, and only
/// ever on the same server, so the token never goes elsewhere.
/// </summary>
public sealed class GitHubApi : ITicketTracker
{
    /// <summary>How many tickets a page has.</summary>
    public const int PageSize = 50;

    private const int CommentsPerPage = 100;

    private readonly TicketHttp _http;
    private readonly string _server;
    private readonly string _base;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string ETag, JsonNode? Json)> _etags = new(StringComparer.Ordinal);
    private TicketAccount? _me;

    public GitHubApi(TicketTrackerContext context)
    {
        _server = context.Server;
        _base = BaseUrl(context.Server);
        _http = new TicketHttp(context.Http, context.Time, "GitHub", context.Token, context.UserAgent);
    }

    /// <summary>The API's address for a server: <c>https://api.github.com</c>, or <c>https://&lt;server&gt;/api/v3</c>.</summary>
    public static string BaseUrl(string server) => TicketServers.Same(server, GitHubTicketsType.GitHubCom) ? "https://api.github.com" : $"https://{server}/api/v3";

    private bool HasToken => _http.Token is { Length: > 0 };

    public async Task<TicketAccount?> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        if (!HasToken)
        {
            return null;
        }
        var user = await GetAsync($"{_base}/user", cancellationToken).ConfigureAwait(false);
        var login = Str(user, "login") ?? throw new TicketException(TicketErrorKind.Other, "GitHub's answer about the account couldn't be read.");
        var account = new TicketAccount(login, Str(user, "name") is { Length: > 0 } name && name != login ? name : null);
        lock (_lock)
        {
            _me = account;
        }
        return account;
    }

    public async Task<TicketPage> ListAsync(TicketSource source, TicketQuery query, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var search = query.Mine || query.Text.Trim().Length > 0;
        var url = cursor is { Length: > 0 } next ? CheckedNext(next) : search ? SearchUrl(source, query) : ListUrlFor(source, query);
        JsonNode? json;
        TicketResponse response;
        try
        {
            (response, json) = await GetWithHeadersAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (TicketException ex) when (ex.Kind == TicketErrorKind.NotFound)
        {
            throw new TicketException(TicketErrorKind.NotFound, HasToken
                ? $"GitHub didn't find {source.Project}, or the token can't read it."
                : $"GitHub didn't find {source.Project}. It may be private: sign in to GitHub in Settings → Tickets.", inner: ex);
        }
        var items = (search ? (json as JsonObject)?["items"] : json) as JsonArray ?? [];
        var tickets = items.OfType<JsonObject>().Where(i => i["pull_request"] is null).Select(Ticket).OfType<Ticket>().ToList();
        return new TicketPage(tickets, NextLink(response));
    }

    public async Task<TicketDetail> GetAsync(TicketSource source, string id, int maxComments, CancellationToken cancellationToken = default)
    {
        var issue = await GetAsync($"{_base}/repos/{source.Project}/issues/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new TicketException(TicketErrorKind.Other, $"GitHub's answer about #{id} couldn't be read.");
        var ticket = Ticket(issue) ?? throw new TicketException(TicketErrorKind.Other, $"GitHub's answer about #{id} couldn't be read.");
        var fields = new List<TicketField>();
        if (Str(issue["milestone"], "title") is { Length: > 0 } milestone)
        {
            fields.Add(new TicketField("Milestone", milestone));
        }
        if (Str(issue["type"], "name") is { Length: > 0 } type)
        {
            fields.Add(new TicketField("Type", type));
        }
        var comments = new List<TicketComment>();
        if (ticket.Comments > 0 && maxComments > 0)
        {
            // The latest: the last page, and the one before when it has too few.
            var last = (ticket.Comments + CommentsPerPage - 1) / CommentsPerPage;
            var first = Math.Max(1, last - ((maxComments + CommentsPerPage - 1) / CommentsPerPage));
            for (var page = first; page <= last; page++)
            {
                var url = $"{_base}/repos/{source.Project}/issues/{Uri.EscapeDataString(id)}/comments?per_page={CommentsPerPage}&page={page}";
                var list = await GetAsync(url, cancellationToken).ConfigureAwait(false) as JsonArray ?? [];
                comments.AddRange(list.OfType<JsonObject>().Select(c => new TicketComment(Str(c["user"], "login") ?? "someone", Time(c, "created_at"), Str(c, "body") ?? "")));
            }
        }
        var latest = comments.Count > maxComments ? comments[^maxComments..] : comments;
        return new TicketDetail(ticket with { Comments = Math.Max(ticket.Comments, latest.Count) }, Str(issue, "body") ?? "", fields, latest);
    }

    public string ListUrl(TicketSource source) => $"https://{(TicketServers.Same(_server, GitHubTicketsType.GitHubCom) ? "github.com" : _server)}/{source.Project}/issues";

    public async Task CommentAsync(TicketSource source, string id, string text, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Post, $"{_base}/repos/{source.Project}/issues/{Uri.EscapeDataString(id)}/comments", new JsonObject { ["body"] = text }, cancellationToken)
            .ConfigureAwait(false);

    public Task<IReadOnlyList<TicketStateChoice>> StatesAsync(TicketSource source, Ticket ticket, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TicketStateChoice>>(ticket.IsOpen
            ? [new("Close as completed", "completed", true), new("Close as not planned", "not_planned", true)]
            : [new("Reopen", "reopened", false)]);

    public async Task SetStateAsync(TicketSource source, string id, TicketStateChoice state, CancellationToken cancellationToken = default) =>
        await SendAsync(new HttpMethod("PATCH"), $"{_base}/repos/{source.Project}/issues/{Uri.EscapeDataString(id)}",
            new JsonObject { ["state"] = state.IsResolved ? "closed" : "open", ["state_reason"] = state.Value }, cancellationToken).ConfigureAwait(false);

    public async Task AssignToMeAsync(TicketSource source, string id, CancellationToken cancellationToken = default)
    {
        TicketAccount? me;
        lock (_lock)
        {
            me = _me;
        }
        me ??= await WhoAmIAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new TicketException(TicketErrorKind.Refused, "Sign in to GitHub in Settings → Tickets to assign tickets to yourself.");
        await SendAsync(HttpMethod.Post, $"{_base}/repos/{source.Project}/issues/{Uri.EscapeDataString(id)}/assignees",
            new JsonObject { ["assignees"] = new JsonArray(me.Login) }, cancellationToken).ConfigureAwait(false);
    }

    // ---- The addresses --------------------------------------------------------------------------------------------

    private string ListUrlFor(TicketSource source, TicketQuery query)
    {
        var state = query.State switch { TicketStateFilter.Closed => "closed", TicketStateFilter.All => "all", _ => "open" };
        var (sort, direction) = Order(query.Sort);
        return $"{_base}/repos/{source.Project}/issues?state={state}&sort={sort}&direction={direction}&per_page={PageSize}";
    }

    private string SearchUrl(TicketSource source, TicketQuery query)
    {
        var words = new List<string> { $"repo:{source.Project}", "is:issue" };
        if (query.State != TicketStateFilter.All)
        {
            words.Add(query.State == TicketStateFilter.Closed ? "is:closed" : "is:open");
        }
        if (query.Mine)
        {
            words.Add("assignee:@me");
        }
        if (query.Text.Trim() is { Length: > 0 } text)
        {
            words.Add(text);
        }
        var (sort, direction) = Order(query.Sort);
        return $"{_base}/search/issues?q={Uri.EscapeDataString(string.Join(' ', words))}&sort={sort}&order={direction}&per_page={PageSize}";
    }

    private static (string Sort, string Direction) Order(TicketSort sort) => sort switch
    {
        TicketSort.Updated => ("updated", "desc"),
        TicketSort.Oldest => ("created", "asc"),
        _ => ("created", "desc"),
    };

    /// <summary>A next page's address, only on this server's API: anything else would take the token elsewhere.</summary>
    private string CheckedNext(string next) => next.StartsWith(_base + "/", StringComparison.OrdinalIgnoreCase)
        ? next
        : throw new TicketException(TicketErrorKind.Other, "GitHub's next page was on another server, so it wasn't read.");

    /// <summary>The <c>Link</c> header's <c>rel="next"</c>, on this server's API; null at the end.</summary>
    private string? NextLink(TicketResponse response)
    {
        foreach (var part in (TicketHttp.Header(response.Headers, "Link") ?? "").Split(','))
        {
            var pieces = part.Split(';');
            if (pieces.Length >= 2 && pieces.Skip(1).Any(p => p.Trim().Equals("rel=\"next\"", StringComparison.OrdinalIgnoreCase)))
            {
                var url = pieces[0].Trim().TrimStart('<').TrimEnd('>');
                return url.StartsWith(_base + "/", StringComparison.OrdinalIgnoreCase) ? url : null;
            }
        }
        return null;
    }

    // ---- The requests ----------------------------------------------------------------------------------------------

    private async Task<JsonNode?> GetAsync(string url, CancellationToken cancellationToken) =>
        (await GetWithHeadersAsync(url, cancellationToken).ConfigureAwait(false)).Json;

    /// <summary>A GET, asked with the <c>ETag</c> of its last answer: a <c>304</c> gives that answer back.</summary>
    private async Task<(TicketResponse Response, JsonNode? Json)> GetWithHeadersAsync(string url, CancellationToken cancellationToken)
    {
        (string ETag, JsonNode? Json)? cached;
        lock (_lock)
        {
            cached = _etags.TryGetValue(url, out var kept) ? kept : null;
        }
        var response = await _http.SendAsync(HttpMethod.Get, url, null, headers =>
        {
            AddHeaders(headers);
            if (cached is { } known)
            {
                headers.IfNoneMatch.ParseAdd(known.ETag);
            }
        }, Message, cancellationToken).ConfigureAwait(false);
        if (response.Status == HttpStatusCode.NotModified && cached is { } same)
        {
            return (response, same.Json?.DeepClone());
        }
        if (response.Headers.ETag is { } etag)
        {
            lock (_lock)
            {
                _etags[url] = (etag.ToString(), response.Json?.DeepClone());
            }
        }
        return (response, response.Json);
    }

    /// <summary>A change, which needs a token, worded for one that may only read when GitHub refuses it.</summary>
    private async Task<TicketResponse> SendAsync(HttpMethod method, string url, JsonNode body, CancellationToken cancellationToken)
    {
        if (!HasToken)
        {
            throw new TicketException(TicketErrorKind.Refused, "Sign in to GitHub in Settings → Tickets to change tickets.");
        }
        try
        {
            return await _http.SendAsync(method, url, body, AddHeaders, Message, cancellationToken).ConfigureAwait(false);
        }
        catch (TicketException ex) when (ex.Kind is TicketErrorKind.Forbidden or TicketErrorKind.NotFound)
        {
            throw new TicketException(TicketErrorKind.Forbidden, "GitHub didn't let this token change the ticket. Give it Issues: Read and write.", inner: ex);
        }
    }

    private static void AddHeaders(System.Net.Http.Headers.HttpRequestHeaders headers)
    {
        headers.Accept.Clear();
        headers.Accept.ParseAdd("application/vnd.github+json");
        headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>GitHub's reason, from its error's <c>message</c>.</summary>
    private static string? Message(JsonNode? json) => Str(json, "message");

    // ---- Reading its answers --------------------------------------------------------------------------------------

    /// <summary>An issue as a ticket; null when it can't be read.</summary>
    internal static Ticket? Ticket(JsonObject issue)
    {
        if (issue["number"] is not JsonValue number || !number.TryGetValue<long>(out var n) || Str(issue, "title") is not { } title)
        {
            return null;
        }
        var open = Str(issue, "state") != "closed";
        var state = open ? "Open" : Str(issue, "state_reason") switch
        {
            "completed" => "Closed as completed",
            "not_planned" => "Closed as not planned",
            "duplicate" => "Closed as a duplicate",
            _ => "Closed",
        };
        var labels = (issue["labels"] as JsonArray ?? []).Select(l => Str(l, "name")).OfType<string>().ToArray();
        var assignees = (issue["assignees"] as JsonArray ?? []).Select(a => Str(a, "login")).OfType<string>().ToArray();
        var comments = issue["comments"] is JsonValue c && c.TryGetValue<int>(out var count) ? count : 0;
        var created = Time(issue, "created_at");
        return new Ticket(
            $"#{n}", n.ToString(CultureInfo.InvariantCulture), title, open, state, labels, Str(issue["user"], "login"),
            created, issue["updated_at"] is null ? created : Time(issue, "updated_at"), comments, Str(issue, "html_url") ?? "", assignees);
    }

    private static string? Str(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static DateTimeOffset Time(JsonNode? node, string name) =>
        DateTimeOffset.TryParse(Str(node, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : DateTimeOffset.UnixEpoch;
}
