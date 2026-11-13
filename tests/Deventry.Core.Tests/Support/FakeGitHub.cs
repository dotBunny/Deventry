using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace Deventry.Core.Tests.Support;

/// <summary>
/// GitHub's REST API for one repository, <c>org/game</c>, answered here in the shapes GitHub answers in (DESIGN.md §18,
/// "Tickets", "Requests"): its issues and pull requests, search, comments, the account, <c>ETag</c>s, the next page's
/// <c>Link</c>, and writing back. Every request is kept to check. Nothing reaches GitHub.
/// </summary>
public sealed class FakeGitHub : HttpMessageHandler
{
    public const string Token = "github_pat_FAKE0123456789abcdefghijklmnopqrstuvwxyz";

    public const string Repository = "org/game";

    /// <summary>The repositories it answers for, each with the same issues: <see cref="Repository"/>, and any a test adds.</summary>
    public HashSet<string> Repositories { get; } = [Repository];

    public const string Api = "https://api.github.com";

    private readonly Lock _lock = new();
    private int _version = 1;

    /// <summary>The issues and pull requests, newest last.</summary>
    public List<JsonObject> Issues { get; } = [];

    /// <summary>Each issue's comments, oldest first, by its number.</summary>
    public Dictionary<long, List<JsonObject>> Comments { get; } = [];

    /// <summary>Who <see cref="Token"/> signs in as.</summary>
    public string Login { get; set; } = "kim";

    /// <summary>Answers a request before the fake does, such as a used-up limit; null leaves it to the fake.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Answer { get; set; }

    /// <summary>How many issues a list's page has when it isn't asked.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Every request: its method, address, body, <c>Authorization</c> and <c>If-None-Match</c>.</summary>
    public List<Request> Requests { get; } = [];

    public sealed record Request(HttpMethod Method, string Url, string? Body, string? Authorization, string? IfNoneMatch, string? Accept, string? ApiVersion);

    public IReadOnlyList<Request> RequestsCopy()
    {
        lock (_lock)
        {
            return [.. Requests];
        }
    }

    /// <summary>The <c>ETag</c> every answer has now; changing anything gives a new one.</summary>
    public string ETag => $"W/\"v{_version}\"";

    /// <summary>Adds an issue (or a pull request), opened <paramref name="daysAgo"/> days before 2026-11-13.</summary>
    public JsonObject Add(long number, string title, string state = "open", string[]? labels = null, int comments = 0, bool pullRequest = false,
        int daysAgo = 0, string? stateReason = null, string body = "", string[]? assignees = null, string? milestone = null)
    {
        var created = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero).AddDays(-daysAgo);
        var issue = new JsonObject
        {
            ["number"] = number,
            ["title"] = title,
            ["state"] = state,
            ["state_reason"] = stateReason,
            ["labels"] = new JsonArray([.. (labels ?? []).Select(l => (JsonNode)new JsonObject { ["name"] = l, ["color"] = "d73a4a" })]),
            ["comments"] = comments,
            ["user"] = new JsonObject { ["login"] = "sam" },
            ["assignees"] = new JsonArray([.. (assignees ?? []).Select(a => (JsonNode)new JsonObject { ["login"] = a })]),
            ["created_at"] = created.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["updated_at"] = created.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["html_url"] = $"https://github.com/{Repository}/issues/{number}",
            ["body"] = body,
            ["milestone"] = milestone is null ? null : new JsonObject { ["title"] = milestone },
        };
        if (pullRequest)
        {
            issue["pull_request"] = new JsonObject { ["url"] = $"{Api}/repos/{Repository}/pulls/{number}" };
        }
        lock (_lock)
        {
            Issues.Add(issue);
            _version++;
        }
        return issue;
    }

    /// <summary>Gives issue <paramref name="number"/> <paramref name="count"/> comments, numbered from 1, a day apart.</summary>
    public void AddComments(long number, int count)
    {
        lock (_lock)
        {
            var list = Comments.TryGetValue(number, out var known) ? known : Comments[number] = [];
            for (var i = 1; i <= count; i++)
            {
                list.Add(Comment($"Comment {list.Count + 1}", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero).AddHours(list.Count)));
            }
            Find(number)!["comments"] = list.Count;
            _version++;
        }
    }

    private static JsonObject Comment(string body, DateTimeOffset at) => new()
    {
        ["user"] = new JsonObject { ["login"] = "sam" },
        ["created_at"] = at.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["body"] = body,
    };

    public JsonObject? Find(long number) => Issues.FirstOrDefault(i => (long)i["number"]! == number);

    /// <summary>A used-up limit, as GitHub answers it: a <c>403</c> with no requests left until <paramref name="reset"/>.</summary>
    public static HttpResponseMessage LimitUsedUp(DateTimeOffset reset)
    {
        var response = Json(HttpStatusCode.Forbidden, new JsonObject { ["message"] = "API rate limit exceeded for 203.0.113.7." });
        response.Headers.Add("X-RateLimit-Remaining", "0");
        response.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode json) =>
        new(status) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_lock)
        {
            Requests.Add(new Request(request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString(),
                request.Headers.IfNoneMatch.FirstOrDefault()?.ToString(), request.Headers.Accept.FirstOrDefault()?.ToString(),
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.First() : null));
        }
        if (Answer?.Invoke(request) is { } answered)
        {
            return answered;
        }
        lock (_lock)
        {
            return Respond(request, body);
        }
    }

    private HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        var query = HttpUtility.ParseQueryString(uri.Query);
        var auth = request.Headers.Authorization;
        if (auth is not null && (auth.Scheme != "Bearer" || auth.Parameter != Token))
        {
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["message"] = "Bad credentials" });
        }
        var signedIn = auth is not null;
        if (request.Method == HttpMethod.Get && request.Headers.IfNoneMatch.FirstOrDefault()?.ToString() == ETag)
        {
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }
        if (path == "/user" && request.Method == HttpMethod.Get)
        {
            return signedIn ? Tagged(new JsonObject { ["login"] = Login, ["name"] = "Kim Lee" })
                : Json(HttpStatusCode.Unauthorized, new JsonObject { ["message"] = "Requires authentication" });
        }
        var repo = Repositories.Select(r => $"/repos/{r}/issues").FirstOrDefault(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal))
            ?? $"/repos/{Repository}/issues";
        if (path == repo && request.Method == HttpMethod.Get)
        {
            var state = query["state"] ?? "open";
            var listed = Ordered(Issues.Where(i => state == "all" || (string)i["state"]! == state), query["sort"], query["direction"]);
            return Page(listed, uri, query);
        }
        if (path == "/search/issues" && request.Method == HttpMethod.Get)
        {
            var words = (query["q"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = Issues.Where(i => i["pull_request"] is null || !words.Contains("is:issue"))
                .Where(i => !words.Contains("is:open") || (string)i["state"]! == "open")
                .Where(i => !words.Contains("is:closed") || (string)i["state"]! == "closed")
                .Where(i => !words.Contains("assignee:@me") || ((JsonArray)i["assignees"]!).Any(a => (string)a!["login"]! == Login))
                .Where(i => words.Where(w => !w.Contains(':', StringComparison.Ordinal)).All(w => ((string)i["title"]!).Contains(w, StringComparison.OrdinalIgnoreCase)));
            var items = Ordered(found, query["sort"], query["order"]).ToList();
            return Tagged(new JsonObject { ["total_count"] = items.Count, ["incomplete_results"] = false, ["items"] = new JsonArray([.. items.Select(i => (JsonNode)i.DeepClone())]) });
        }
        if (path.StartsWith(repo + "/", StringComparison.Ordinal))
        {
            var rest = path[(repo.Length + 1)..].Split('/');
            if (!long.TryParse(rest[0], out var number) || Find(number) is not { } issue)
            {
                return Json(HttpStatusCode.NotFound, new JsonObject { ["message"] = "Not Found" });
            }
            if (rest.Length == 1 && request.Method == HttpMethod.Get)
            {
                return Tagged(issue.DeepClone());
            }
            if (rest.Length == 1 && request.Method.Method == "PATCH")
            {
                var change = JsonNode.Parse(body!)!;
                issue["state"] = (string)change["state"]!;
                issue["state_reason"] = (string?)change["state_reason"];
                _version++;
                return Json(HttpStatusCode.OK, issue.DeepClone());
            }
            if (rest is [_, "comments"] && request.Method == HttpMethod.Get)
            {
                var all = Comments.TryGetValue(number, out var list) ? list : [];
                var perPage = int.TryParse(query["per_page"], out var per) ? per : 30;
                var page = int.TryParse(query["page"], out var p) ? p : 1;
                return Tagged(new JsonArray([.. all.Skip((page - 1) * perPage).Take(perPage).Select(c => (JsonNode)c.DeepClone())]));
            }
            if (rest is [_, "comments"] && request.Method == HttpMethod.Post)
            {
                var list = Comments.TryGetValue(number, out var known) ? known : Comments[number] = [];
                list.Add(Comment((string)JsonNode.Parse(body!)!["body"]!, new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));
                issue["comments"] = list.Count;
                _version++;
                return Json(HttpStatusCode.Created, list[^1].DeepClone());
            }
            if (rest is [_, "assignees"] && request.Method == HttpMethod.Post)
            {
                foreach (var login in (JsonArray)JsonNode.Parse(body!)!["assignees"]!)
                {
                    ((JsonArray)issue["assignees"]!).Add(new JsonObject { ["login"] = (string)login! });
                }
                _version++;
                return Json(HttpStatusCode.Created, issue.DeepClone());
            }
        }
        return Json(HttpStatusCode.NotFound, new JsonObject { ["message"] = "Not Found" });
    }

    private static IEnumerable<JsonObject> Ordered(IEnumerable<JsonObject> issues, string? sort, string? direction)
    {
        var key = sort == "updated" ? "updated_at" : "created_at";
        return direction == "asc" ? issues.OrderBy(i => (string)i[key]!) : issues.OrderByDescending(i => (string)i[key]!);
    }

    /// <summary>A list's page, by <c>after</c> as GitHub's cursor, with the next page's <c>Link</c>.</summary>
    private HttpResponseMessage Page(IEnumerable<JsonObject> issues, Uri uri, System.Collections.Specialized.NameValueCollection query)
    {
        var all = issues.ToList();
        var perPage = int.TryParse(query["per_page"], out var per) ? per : PageSize;
        var after = int.TryParse(query["after"], out var a) ? a : 0;
        var page = all.Skip(after).Take(perPage).ToList();
        var response = Tagged(new JsonArray([.. page.Select(i => (JsonNode)i.DeepClone())]));
        if (after + perPage < all.Count)
        {
            var next = HttpUtility.ParseQueryString(uri.Query);
            next["after"] = (after + perPage).ToString(System.Globalization.CultureInfo.InvariantCulture);
            response.Headers.TryAddWithoutValidation("Link", $"<{Api}{uri.AbsolutePath}?{next}>; rel=\"next\"");
        }
        return response;
    }

    private HttpResponseMessage Tagged(JsonNode json)
    {
        var response = Json(HttpStatusCode.OK, json);
        response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(ETag);
        response.Headers.Add("X-RateLimit-Remaining", "4999");
        return response;
    }
}
