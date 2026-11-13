using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Deventry.Core.Demo;

/// <summary>
/// GitHub's API for the demo's Unreal project's issues (DESIGN.md §18, "Tickets", "The demo"), answered here so the
/// guide's pictures of the Tickets tab never reach the network: <see cref="Repository"/>'s list, search, an issue and its
/// comments. Everything else goes on to <see cref="DelegatingHandler.InnerHandler"/>. Deventry puts it in front of its
/// requests only with <see cref="Variable"/> set, as the screenshot scripts set it.
/// </summary>
public sealed class DemoTickets(TimeProvider time) : DelegatingHandler
{
    /// <summary>In the environment of a Deventry the demo's tickets are answered for.</summary>
    public const string Variable = "DEVENTRY_DEMO_TICKETS";

    /// <summary>The demo's repository: the starfall project's <c>tickets</c> source.</summary>
    public const string Repository = "starfall-studio/starfall";

    /// <summary>What the screenshot scripts write in starfall's <c>deventry.local.json</c> for its tickets.</summary>
    public const string ProjectFile = """{ "tickets": [ { "provider": "github", "repository": "starfall-studio/starfall" } ] }""";

    private const string Api = "https://api.github.com";

    private sealed record Issue(int Number, string Title, string[] Labels, string Author, double DaysAgo, string Body, (string Author, double DaysAgo, string Body)[] Comments,
        string[]? Assignees = null, string? Milestone = null, bool Closed = false);

    private static readonly Issue[] Issues =
    [
        new(142, "Grappling hook detaches on slopes", ["bug", "gameplay"], "kim", 2,
            """
            When the player fires the hook at a slope steeper than 30°, the rope attaches, then drops the player.

            **Steps**

            1. Open `Test_Slopes` and fire at the ramp.
            2. Hold the grapple key while the rope pulls.

            The rope lets go after about a second. On flat ground it holds.
            """,
            [("sam", 1.2, "Repro'd on 5.6.1. The anchor's normal flips on the second tick."), ("kim", 0.9, "Could it be the trace channel? `GrappleTrace` ignores `WorldStatic`."),
             ("ana", 0.3, "Saw it on the cliffs in `Map_Coast` too.")],
            Assignees: ["kim"], Milestone: "0.4"),
        new(139, "Save slots overwrite each other on console", ["bug", "save"], "ana", 5,
            "Saving to slot 2 writes slot 1's file as well. Only on the console build; PC is fine.",
            [("sam", 4, "`USaveSlots::Save` builds the name before the index is set.")], Milestone: "0.4"),
        new(137, "Stamina drains twice on clients", ["bug", "network"], "sam", 6,
            "In a listen server session, clients lose stamina twice as fast: the server and the client both spend it.", []),
        new(131, "Controller rumble stays on after the grapple lands", ["enhancement"], "ana", 14,
            "The rumble that starts with the pull never stops, until the next one.", []),
        new(127, "Crash on exit in PIE", ["bug", "crash"], "kim", 21,
            "Ending Play In Editor with the grapple attached crashes in `UGrappleComponent::EndPlay`.", [("kim", 20, "Call stack attached in the internal tracker.")]),
        new(118, "Coyote time for jumps off ledges", ["enhancement", "gameplay"], "sam", 30,
            "A few frames of grace after leaving a ledge, so a late jump still counts.", []),
        new(102, "Footstep sounds on metal", ["audio"], "ana", 45, "Metal surfaces play the concrete footsteps.", [], Closed: true),
    ];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null || !uri.ToString().StartsWith(Api + "/", StringComparison.OrdinalIgnoreCase) || Answer(request.Method, uri) is not { } answer)
        {
            return base.SendAsync(request, cancellationToken);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") });
    }

    private JsonNode? Answer(HttpMethod method, Uri uri)
    {
        if (method != HttpMethod.Get)
        {
            return null;
        }
        var path = uri.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var repo = $"/repos/{Repository}/issues";
        if (path.Equals(repo, StringComparison.OrdinalIgnoreCase))
        {
            var state = query["state"] ?? "open";
            return new JsonArray([.. Ordered(Issues.Where(i => state == "all" || (state == "closed") == i.Closed), query["sort"], query["direction"]).Select(Json)]);
        }
        if (path == "/search/issues" && (query["q"] ?? "").Contains($"repo:{Repository}", StringComparison.OrdinalIgnoreCase))
        {
            var words = (query["q"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = Issues
                .Where(i => !words.Contains("is:open") || !i.Closed)
                .Where(i => !words.Contains("is:closed") || i.Closed)
                .Where(i => !words.Contains("assignee:@me"))
                .Where(i => words.Where(w => w.StartsWith("label:", StringComparison.Ordinal)).All(w => i.Labels.Contains(w[6..])))
                .Where(i => words.Where(w => !w.Contains(':', StringComparison.Ordinal)).All(w => i.Title.Contains(w, StringComparison.OrdinalIgnoreCase)));
            var items = Ordered(found, query["sort"], query["order"]).Select(Json).ToArray();
            return new JsonObject { ["total_count"] = items.Length, ["incomplete_results"] = false, ["items"] = new JsonArray(items) };
        }
        if (path.StartsWith(repo + "/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path[(repo.Length + 1)..].Split('/');
            if (!int.TryParse(rest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || Issues.FirstOrDefault(i => i.Number == number) is not { } issue)
            {
                return null;
            }
            if (rest.Length == 1)
            {
                return Json(issue);
            }
            if (rest is [_, "comments"])
            {
                return (query["page"] ?? "1") == "1"
                    ? new JsonArray([.. issue.Comments.Select(c => (JsonNode)new JsonObject
                    {
                        ["user"] = new JsonObject { ["login"] = c.Author },
                        ["created_at"] = When(c.DaysAgo),
                        ["body"] = c.Body,
                    })])
                    : new JsonArray();
            }
        }
        return null;
    }

    private static IEnumerable<Issue> Ordered(IEnumerable<Issue> issues, string? sort, string? direction)
    {
        Func<Issue, double> age = sort == "updated" ? Updated : i => i.DaysAgo;
        return direction == "asc" ? issues.OrderByDescending(age) : issues.OrderBy(age);
    }

    /// <summary>How long ago it was last updated: its latest comment's time, else when it was opened.</summary>
    private static double Updated(Issue issue) => issue.Comments.Length > 0 ? issue.Comments.Min(c => c.DaysAgo) : issue.DaysAgo;

    private JsonNode Json(Issue issue) => new JsonObject
    {
        ["number"] = issue.Number,
        ["title"] = issue.Title,
        ["state"] = issue.Closed ? "closed" : "open",
        ["state_reason"] = issue.Closed ? "completed" : null,
        ["labels"] = new JsonArray([.. issue.Labels.Select(l => (JsonNode)new JsonObject { ["name"] = l })]),
        ["comments"] = issue.Comments.Length,
        ["user"] = new JsonObject { ["login"] = issue.Author },
        ["assignees"] = new JsonArray([.. (issue.Assignees ?? []).Select(a => (JsonNode)new JsonObject { ["login"] = a })]),
        ["milestone"] = issue.Milestone is { } milestone ? new JsonObject { ["title"] = milestone } : null,
        ["created_at"] = When(issue.DaysAgo),
        ["updated_at"] = When(Updated(issue)),
        ["html_url"] = $"https://github.com/{Repository}/issues/{issue.Number}",
        ["body"] = issue.Body,
    };

    private string When(double daysAgo) => (time.GetUtcNow() - TimeSpan.FromDays(daysAgo)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
