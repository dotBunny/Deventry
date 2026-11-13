using System.Globalization;
using System.Text.Json.Nodes;

namespace Deventry.Core.Tickets.YouTrack;

/// <summary>
/// YouTrack's REST API for a server's issues (DESIGN.md §18, "Tickets", "Requests"): <c>&lt;server&gt;/api</c>, with
/// <c>fields</c> naming what each answer has. A source's project and query, the list's state, <b>Mine</b>, the filter
/// box's words and the order are one YouTrack query. Without a token, Deventry is the server's guest.
/// </summary>
public sealed class YouTrackApi : ITicketTracker
{
    /// <summary>How many tickets a page has.</summary>
    public const int PageSize = 50;

    private const string ListFields =
        "id,idReadable,summary,created,updated,resolved,commentsCount,reporter(login,fullName),tags(name),customFields(name,value(name,login,fullName,presentation,text))";

    private readonly TicketHttp _http;
    private readonly string _server;
    private readonly string _base;

    public YouTrackApi(TicketTrackerContext context)
    {
        _server = context.Server.TrimEnd('/');
        _base = _server + "/api";
        _http = new TicketHttp(context.Http, context.Time, "YouTrack", context.Token, context.UserAgent);
    }

    public async Task<TicketAccount?> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        var me = (await SendAsync(HttpMethod.Get, $"{_base}/users/me?fields=login,fullName,guest", null, cancellationToken).ConfigureAwait(false)).Json;
        if (me is JsonObject obj && obj["guest"] is JsonValue guest && guest.TryGetValue<bool>(out var isGuest) && isGuest)
        {
            return null;
        }
        var login = Str(me, "login") ?? throw new TicketException(TicketErrorKind.Other, "YouTrack's answer about the account couldn't be read.");
        return new TicketAccount(login, Str(me, "fullName") is { Length: > 0 } name && name != login ? name : null);
    }

    /// <summary>The one query a list asks for: the project, the source's query, the state, <b>Mine</b>, the words typed and the order.</summary>
    public static string Query(TicketSource source, TicketQuery query)
    {
        var words = new List<string> { $"project: {Braced(source.Project)}" };
        if (source.Query is { Length: > 0 } narrowed)
        {
            words.Add(narrowed);
        }
        if (query.State != TicketStateFilter.All)
        {
            words.Add(query.State == TicketStateFilter.Closed ? "#Resolved" : "#Unresolved");
        }
        if (query.Mine)
        {
            words.Add("for: me");
        }
        if (query.Text.Trim() is { Length: > 0 } text)
        {
            words.Add(text);
        }
        words.Add(query.Sort switch
        {
            TicketSort.Updated => "sort by: updated desc",
            TicketSort.Oldest => "sort by: created asc",
            _ => "sort by: created desc",
        });
        return string.Join(' ', words);
    }

    public async Task<TicketPage> ListAsync(TicketSource source, TicketQuery query, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var skip = int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ? Math.Max(0, from) : 0;
        var url = $"{_base}/issues?query={Uri.EscapeDataString(Query(source, query))}&fields={Uri.EscapeDataString(ListFields)}&$top={PageSize}&$skip={skip}";
        JsonNode? json;
        try
        {
            json = (await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false)).Json;
        }
        catch (TicketException ex) when (ex.Kind == TicketErrorKind.NotFound)
        {
            throw new TicketException(TicketErrorKind.NotFound, $"YouTrack didn't find the project {source.Project} on {source.ServerName}.", inner: ex);
        }
        var items = (json as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var tickets = items.Select(Ticket).OfType<Ticket>().ToList();
        return new TicketPage(tickets, items.Length >= PageSize ? (skip + items.Length).ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<TicketDetail> GetAsync(TicketSource source, string id, int maxComments, CancellationToken cancellationToken = default)
    {
        var fields = ListFields + ",description,comments(text,created,author(login,fullName),deleted)";
        var url = $"{_base}/issues/{Uri.EscapeDataString(id)}?fields={Uri.EscapeDataString(fields)}";
        var issue = (await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false)).Json as JsonObject
            ?? throw new TicketException(TicketErrorKind.Other, $"YouTrack's answer about {id} couldn't be read.");
        var ticket = Ticket(issue) ?? throw new TicketException(TicketErrorKind.Other, $"YouTrack's answer about {id} couldn't be read.");
        var others = (issue["customFields"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(f => Str(f, "name") is { } name && name is not ("State" or "Assignee"))
            .Select(f => (Name: Str(f, "name")!, Value: ValueText(f["value"])))
            .Where(f => f.Value.Length > 0)
            .Select(f => new TicketField(f.Name, f.Value))
            .ToList();
        var comments = (issue["comments"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(c => !(c["deleted"] is JsonValue deleted && deleted.TryGetValue<bool>(out var gone) && gone))
            .Select(c => new TicketComment(Person(c["author"]) ?? "someone", Millis(c, "created"), Str(c, "text") ?? ""))
            .OrderBy(c => c.Created)
            .ToList();
        var latest = comments.Count > maxComments ? comments[^Math.Max(0, maxComments)..] : comments;
        return new TicketDetail(ticket with { Comments = Math.Max(ticket.Comments, comments.Count) }, Str(issue, "description") ?? "", others, latest);
    }

    public string ListUrl(TicketSource source)
    {
        var query = source.Query is { Length: > 0 } narrowed ? $"project: {Braced(source.Project)} {narrowed}" : $"project: {Braced(source.Project)}";
        return $"{_server}/issues?q={Uri.EscapeDataString(query)}";
    }

    public async Task CommentAsync(TicketSource source, string id, string text, CancellationToken cancellationToken = default) =>
        await ChangeAsync(HttpMethod.Post, $"{_base}/issues/{Uri.EscapeDataString(id)}/comments?fields=id", new JsonObject { ["text"] = text }, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The states its project's State field has, but the one it's in: the open ones first, then the resolved.</summary>
    public async Task<IReadOnlyList<TicketStateChoice>> StatesAsync(TicketSource source, Ticket ticket, CancellationToken cancellationToken = default)
    {
        var url = $"{_base}/issues/{Uri.EscapeDataString(ticket.Id)}/customFields?fields={Uri.EscapeDataString("name,$type,projectCustomField(field(name),bundle(values(name,isResolved,archived)))")}";
        var fields = (await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false)).Json as JsonArray ?? [];
        var state = fields.OfType<JsonObject>().FirstOrDefault(f => Str(f, "$type") == "StateIssueCustomField")
            ?? fields.OfType<JsonObject>().FirstOrDefault(f => Str(f, "name") == "State");
        if (state is null)
        {
            return [];
        }
        var field = Str(state, "name") ?? "State";
        return [.. (state["projectCustomField"]?["bundle"]?["values"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(v => !(v["archived"] is JsonValue archived && archived.TryGetValue<bool>(out var old) && old))
            .Select(v => (Name: Str(v, "name"), Resolved: v["isResolved"] is JsonValue r && r.TryGetValue<bool>(out var done) && done))
            .Where(v => v.Name is { Length: > 0 } name && !string.Equals(name, ticket.StateName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Resolved)
            .Select(v => new TicketStateChoice(v.Name!, $"{Braced(field)} {Braced(v.Name!)}", v.Resolved))];
    }

    /// <summary>Runs the state's command, <c>State {In Progress}</c>.</summary>
    public Task SetStateAsync(TicketSource source, string id, TicketStateChoice state, CancellationToken cancellationToken = default) =>
        CommandAsync(id, state.Value, cancellationToken);

    public Task AssignToMeAsync(TicketSource source, string id, CancellationToken cancellationToken = default) => CommandAsync(id, "for me", cancellationToken);

    private async Task CommandAsync(string id, string command, CancellationToken cancellationToken) =>
        await ChangeAsync(HttpMethod.Post, $"{_base}/commands", new JsonObject
        {
            ["query"] = command,
            ["issues"] = new JsonArray(new JsonObject { ["idReadable"] = id }),
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>A word of a query or a command, in braces when it has spaces: <c>{In Progress}</c>.</summary>
    public static string Braced(string word) => word.Any(c => char.IsWhiteSpace(c) || c is ':' or '#' or ',') ? $"{{{word}}}" : word;

    // ---- The requests ----------------------------------------------------------------------------------------------

    private Task<TicketResponse> SendAsync(HttpMethod method, string url, JsonNode? body, CancellationToken cancellationToken) =>
        _http.SendAsync(method, url, body, null, Message, cancellationToken);

    /// <summary>A change, which needs a token, worded for one that may only read when YouTrack refuses it.</summary>
    private async Task<TicketResponse> ChangeAsync(HttpMethod method, string url, JsonNode body, CancellationToken cancellationToken)
    {
        if (_http.Token is not { Length: > 0 })
        {
            throw new TicketException(TicketErrorKind.Refused, $"Set a token for {TicketServers.Key(_server)} in Settings → Tickets → YouTrack to change tickets.");
        }
        try
        {
            return await SendAsync(method, url, body, cancellationToken).ConfigureAwait(false);
        }
        catch (TicketException ex) when (ex.Kind == TicketErrorKind.Forbidden)
        {
            throw new TicketException(TicketErrorKind.Forbidden, $"YouTrack didn't let this account change the ticket: {ex.Message}", inner: ex);
        }
    }

    /// <summary>YouTrack's reason, from its error's <c>error_description</c> or <c>error</c>.</summary>
    private static string? Message(JsonNode? json) => Str(json, "error_description") is { Length: > 0 } said ? said : Str(json, "error");

    // ---- Reading its answers --------------------------------------------------------------------------------------

    private Ticket? Ticket(JsonObject issue)
    {
        if (Str(issue, "idReadable") is not { Length: > 0 } key)
        {
            return null;
        }
        // A JSON null reads as no node.
        var open = issue["resolved"] is null;
        var fields = (issue["customFields"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var state = fields.FirstOrDefault(f => Str(f, "name") == "State") is { } stateField ? ValueText(stateField["value"]) : "";
        var assignee = fields.FirstOrDefault(f => Str(f, "name") == "Assignee") is { } assigneeField ? Values(assigneeField["value"]) : [];
        var labels = (issue["tags"] as JsonArray ?? []).Select(t => Str(t, "name")).OfType<string>().ToArray();
        var created = Millis(issue, "created");
        return new Ticket(
            key, key, Str(issue, "summary") ?? "", open, state.Length > 0 ? state : open ? "Open" : "Resolved", labels, Person(issue["reporter"]),
            created, issue["updated"] is null ? created : Millis(issue, "updated"), Count(issue, "commentsCount"), $"{_server}/issue/{key}", assignee);
    }

    /// <summary>A custom field's value as text: a name, a person, a period's presentation, or several of them.</summary>
    private static string ValueText(JsonNode? value) => string.Join(", ", Values(value));

    private static IReadOnlyList<string> Values(JsonNode? value) => value switch
    {
        JsonArray list => [.. list.Select(One).OfType<string>()],
        _ => One(value) is { } one ? [one] : [],
    };

    private static string? One(JsonNode? value) => value switch
    {
        JsonObject obj => Str(obj, "name") ?? Person(obj) ?? Str(obj, "presentation") ?? Str(obj, "text"),
        JsonValue scalar when scalar.GetValueKind() is System.Text.Json.JsonValueKind.String => scalar.GetValue<string>(),
        JsonValue scalar when scalar.GetValueKind() is System.Text.Json.JsonValueKind.Number => scalar.ToJsonString(),
        _ => null,
    };

    private static string? Person(JsonNode? user) => Str(user, "fullName") is { Length: > 0 } name ? name : Str(user, "login");

    private static string? Str(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Count(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;

    private static DateTimeOffset Millis(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<long>(out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.UnixEpoch;
}
