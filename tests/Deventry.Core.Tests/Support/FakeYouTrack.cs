using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace Deventry.Core.Tests.Support;

/// <summary>
/// YouTrack's REST API on <c>https://studio.youtrack.cloud</c>, answered here in the shapes YouTrack answers in
/// (DESIGN.md §18, "Tickets", "Requests"): a project's issues, one with its description and comments, the account
/// (a guest without the token), the project's states, and writing back. Every request is kept. Nothing reaches YouTrack.
/// </summary>
public sealed class FakeYouTrack : HttpMessageHandler
{
    public const string Token = "perm:a2ltLmxlZQ==.REVW.FakeTokenForTestsOnly";

    public const string Server = "https://studio.youtrack.cloud";

    private readonly Lock _lock = new();

    public List<JsonObject> Issues { get; } = [];

    /// <summary>Every request: its method, address, body and <c>Authorization</c>.</summary>
    public List<(HttpMethod Method, string Url, string? Body, string? Authorization)> Requests { get; } = [];

    public IReadOnlyList<(HttpMethod Method, string Url, string? Body, string? Authorization)> RequestsCopy()
    {
        lock (_lock)
        {
            return [.. Requests];
        }
    }

    /// <summary>The <c>query</c> of the last list asked for.</summary>
    public string? LastQuery { get; private set; }

    /// <summary>Adds an issue to project NO: <c>NO-<paramref name="number"/></c>, resolved when <paramref name="state"/> is Fixed.</summary>
    public JsonObject Add(int number, string summary, string state = "Open", string? assignee = null, string[]? tags = null, int daysAgo = 0)
    {
        var created = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero).AddDays(-daysAgo).ToUnixTimeMilliseconds();
        var issue = new JsonObject
        {
            ["id"] = $"2-{number}",
            ["idReadable"] = $"NO-{number}",
            ["summary"] = summary,
            ["created"] = created,
            ["updated"] = created + 3_600_000,
            ["resolved"] = state == "Fixed" ? created + 7_200_000 : null,
            ["commentsCount"] = 0,
            ["reporter"] = new JsonObject { ["login"] = "sam", ["fullName"] = "Sam Park" },
            ["tags"] = new JsonArray([.. (tags ?? []).Select(t => (JsonNode)new JsonObject { ["name"] = t })]),
            ["customFields"] = new JsonArray(
                Field("Type", "SingleEnumIssueCustomField", new JsonObject { ["name"] = "Bug" }),
                Field("Priority", "SingleEnumIssueCustomField", new JsonObject { ["name"] = "Major" }),
                Field("State", "StateIssueCustomField", new JsonObject { ["name"] = state }),
                Field("Assignee", "SingleUserIssueCustomField", assignee is null ? null : new JsonObject { ["login"] = "kim", ["fullName"] = assignee }),
                Field("Fix versions", "MultiVersionIssueCustomField", new JsonArray())),
            ["description"] = "The rope **drops** the player.",
            ["comments"] = new JsonArray(),
        };
        lock (_lock)
        {
            Issues.Add(issue);
        }
        return issue;
    }

    /// <summary>Adds a comment, or a deleted one, to <c>NO-<paramref name="number"/></c>.</summary>
    public void Comment(int number, string text, bool deleted = false)
    {
        lock (_lock)
        {
            var issue = Issues.First(i => (string)i["idReadable"]! == $"NO-{number}");
            var comments = (JsonArray)issue["comments"]!;
            comments.Add(new JsonObject
            {
                ["text"] = text,
                ["created"] = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero).AddMinutes(comments.Count).ToUnixTimeMilliseconds(),
                ["author"] = new JsonObject { ["login"] = "kim", ["fullName"] = "Kim Lee" },
                ["deleted"] = deleted,
            });
            issue["commentsCount"] = comments.Count(c => !(bool)c!["deleted"]!);
        }
    }

    private static JsonObject Field(string name, string type, JsonNode? value) => new() { ["name"] = name, ["$type"] = type, ["value"] = value };

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode json) =>
        new(status) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_lock)
        {
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            return Respond(request, body);
        }
    }

    private HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        var query = HttpUtility.ParseQueryString(uri.Query);
        var auth = request.Headers.Authorization;
        if (auth is not null && auth.Parameter != Token)
        {
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "Unauthorized", ["error_description"] = "Invalid token" });
        }
        if (path == "/api/users/me")
        {
            return Json(HttpStatusCode.OK, auth is null
                ? new JsonObject { ["login"] = "guest", ["fullName"] = "Guest", ["guest"] = true }
                : new JsonObject { ["login"] = "kim", ["fullName"] = "Kim Lee", ["guest"] = false });
        }
        if (path == "/api/issues" && request.Method == HttpMethod.Get)
        {
            LastQuery = query["query"];
            var words = LastQuery ?? "";
            if (!words.Contains("project: NO", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = "Not Found", ["error_description"] = "Project not found" });
            }
            var listed = Issues
                .Where(i => !words.Contains("#Unresolved", StringComparison.Ordinal) || i["resolved"] is null)
                .Where(i => !words.Contains("#Resolved", StringComparison.Ordinal) || i["resolved"] is not null)
                .OrderByDescending(i => (long)i["created"]!).ToList();
            var top = int.Parse(query["$top"] ?? "50");
            var skip = int.Parse(query["$skip"] ?? "0");
            return Json(HttpStatusCode.OK, new JsonArray([.. listed.Skip(skip).Take(top).Select(i => (JsonNode)i.DeepClone())]));
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["api", "issues", var id, ..] && Issues.FirstOrDefault(i => (string)i["idReadable"]! == id) is { } issue)
        {
            if (parts.Length == 3)
            {
                return Json(HttpStatusCode.OK, issue.DeepClone());
            }
            if (parts[3] == "customFields")
            {
                var values = new JsonArray(
                    State("Open", false), State("In Progress", false), State("Fixed", true), State("Obsolete", false, archived: true), State("Won't fix", true));
                return Json(HttpStatusCode.OK, new JsonArray(
                    new JsonObject { ["name"] = "Type", ["$type"] = "SingleEnumIssueCustomField" },
                    new JsonObject
                    {
                        ["name"] = "State",
                        ["$type"] = "StateIssueCustomField",
                        ["projectCustomField"] = new JsonObject { ["field"] = new JsonObject { ["name"] = "State" }, ["bundle"] = new JsonObject { ["values"] = values } },
                    }));
            }
            if (parts[3] == "comments" && request.Method == HttpMethod.Post)
            {
                Comment(int.Parse(id[3..]), (string)JsonNode.Parse(body!)!["text"]!);
                return Json(HttpStatusCode.OK, new JsonObject { ["id"] = "4-1" });
            }
        }
        if (path == "/api/commands" && request.Method == HttpMethod.Post)
        {
            return Json(HttpStatusCode.OK, new JsonObject { ["query"] = JsonNode.Parse(body!)!["query"]!.DeepClone() });
        }
        return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = "Not Found" });
    }

    private static JsonObject State(string name, bool resolved, bool archived = false) => new() { ["name"] = name, ["isResolved"] = resolved, ["archived"] = archived };
}
