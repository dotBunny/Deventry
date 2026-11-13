using System.Net;
using System.Text.Json.Nodes;
using Deventry.Core.Tests.Support;
using Deventry.Core.Tickets;
using Deventry.Core.Tickets.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Tickets;

/// <summary>GitHub's issues (DESIGN.md §18, "Tickets", "Requests"), answered by a fake: nothing reaches GitHub.</summary>
public sealed class GitHubApiTests : IDisposable
{
    private static readonly TicketSource Source = new(GitHubTicketsType.KindId, GitHubTicketsType.GitHubCom, FakeGitHub.Repository, null, null, TicketSourceOrigin.Git);

    private readonly FakeGitHub _github = new();
    private readonly HttpClient _http;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));

    public GitHubApiTests() => _http = new HttpClient(_github);

    public void Dispose() => _http.Dispose();

    private GitHubApi Api(string? token = FakeGitHub.Token, string server = GitHubTicketsType.GitHubCom) =>
        new(new TicketTrackerContext(_http, _time, server, token, "Deventry/test"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_is_the_open_issues_newest_first_without_pull_requests()
    {
        _github.Add(1, "Old bug", labels: ["bug"], daysAgo: 9, comments: 2);
        _github.Add(2, "A pull request", pullRequest: true, daysAgo: 1);
        _github.Add(3, "New bug", labels: ["bug", "gameplay"], daysAgo: 2, assignees: ["kim"]);
        _github.Add(4, "Done", state: "closed", stateReason: "completed");

        var page = await Api().ListAsync(Source, TicketQuery.Default, cancellationToken: Token);

        Assert.Equal(["#3", "#1"], page.Tickets.Select(t => t.Key));
        var newest = page.Tickets[0];
        Assert.Equal(("3", "New bug", true, "Open"), (newest.Id, newest.Title, newest.IsOpen, newest.StateName));
        Assert.Equal(["bug", "gameplay"], newest.Labels);
        Assert.Equal(["kim"], newest.Assignees);
        Assert.Equal("sam", newest.Author);
        Assert.Equal("https://github.com/org/game/issues/3", newest.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero), newest.Created);
        Assert.Equal(2, page.Tickets[1].Comments);
        Assert.Null(page.Next);
        var request = Assert.Single(_github.RequestsCopy());
        Assert.Equal($"{FakeGitHub.Api}/repos/org/game/issues?state=open&sort=created&direction=desc&per_page=50", request.Url);
        Assert.Equal("application/vnd.github+json", request.Accept);
        Assert.Equal("2022-11-28", request.ApiVersion);
    }

    [Fact]
    public async Task The_token_goes_in_a_header_and_never_in_an_address()
    {
        _github.Add(1, "Bug");

        await Api().ListAsync(Source, TicketQuery.Default, cancellationToken: Token);

        var request = Assert.Single(_github.RequestsCopy());
        Assert.Equal($"Bearer {FakeGitHub.Token}", request.Authorization);
        Assert.DoesNotContain(FakeGitHub.Token, request.Url);
    }

    [Fact]
    public async Task Closed_and_all_and_the_other_orders_ask_for_them()
    {
        _github.Add(1, "Open one", daysAgo: 2);
        _github.Add(2, "Closed one", state: "closed", stateReason: "not_planned", daysAgo: 1);

        var closed = await Api().ListAsync(Source, new TicketQuery(TicketStateFilter.Closed), cancellationToken: Token);
        var all = await Api().ListAsync(Source, new TicketQuery(TicketStateFilter.All, Sort: TicketSort.Oldest), cancellationToken: Token);

        Assert.Equal("Closed as not planned", Assert.Single(closed.Tickets).StateName);
        Assert.False(closed.Tickets[0].IsOpen);
        Assert.Equal(["#1", "#2"], all.Tickets.Select(t => t.Key));
        Assert.Contains("state=all&sort=created&direction=asc", _github.RequestsCopy()[^1].Url);
    }

    [Fact]
    public async Task The_next_page_is_the_Link_headers_and_the_last_has_none()
    {
        _github.PageSize = 50;
        for (var i = 1; i <= 60; i++)
        {
            _github.Add(i, $"Issue {i}", daysAgo: i);
        }
        var api = Api();

        var first = await api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token);
        var second = await api.ListAsync(Source, TicketQuery.Default, first.Next, Token);

        Assert.Equal(50, first.Tickets.Count);
        Assert.NotNull(first.Next);
        Assert.StartsWith($"{FakeGitHub.Api}/repos/org/game/issues?", first.Next);
        Assert.Equal(10, second.Tickets.Count);
        Assert.Equal("#51", second.Tickets[0].Key);
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task A_next_page_on_another_server_is_never_asked_for()
    {
        var error = await Assert.ThrowsAsync<TicketException>(() =>
            Api().ListAsync(Source, TicketQuery.Default, "https://example.com/steal?after=50", Token));

        Assert.Contains("another server", error.Message);
        Assert.Empty(_github.RequestsCopy());
    }

    [Fact]
    public async Task Words_in_the_filter_and_Mine_search_the_repositorys_issues()
    {
        _github.Add(1, "Grappling hook detaches", assignees: ["kim"], daysAgo: 2);
        _github.Add(2, "Grappling hook sound", daysAgo: 1);
        _github.Add(3, "Grappling hook pull request", pullRequest: true);

        var found = await Api().ListAsync(Source, new TicketQuery(Mine: true, Text: "grappling"), cancellationToken: Token);

        Assert.Equal("#1", Assert.Single(found.Tickets).Key);
        var url = _github.RequestsCopy()[^1].Url;
        Assert.StartsWith($"{FakeGitHub.Api}/search/issues?q=", url);
        Assert.Equal("repo:org/game is:issue is:open assignee:@me grappling", System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["q"]);
        Assert.Contains("&sort=created&order=desc&per_page=50", url);
    }

    [Fact]
    public async Task An_unchanged_list_is_asked_for_with_its_ETag_and_a_304_gives_it_back()
    {
        _github.Add(1, "Bug");
        var api = Api();

        var first = await api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token);
        var again = await api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token);
        _github.Add(2, "Another");
        var changed = await api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token);

        var requests = _github.RequestsCopy();
        Assert.Null(requests[0].IfNoneMatch);
        Assert.Equal("W/\"v2\"", requests[1].IfNoneMatch);
        Assert.Equal(first.Tickets.Select(t => t.Key), again.Tickets.Select(t => t.Key));
        Assert.Equal(2, changed.Tickets.Count);
    }

    [Fact]
    public async Task A_used_up_limit_says_until_when()
    {
        var reset = new DateTimeOffset(2026, 11, 13, 15, 20, 0, TimeSpan.Zero);
        _github.Answer = _ => FakeGitHub.LimitUsedUp(reset);

        var error = await Assert.ThrowsAsync<TicketException>(() => Api(token: null).ListAsync(Source, TicketQuery.Default, cancellationToken: Token));

        Assert.Equal(TicketErrorKind.RateLimited, error.Kind);
        Assert.Equal(reset, error.Until);
        Assert.Contains("API rate limit exceeded", error.Message);
    }

    [Fact]
    public async Task Retry_After_is_a_limit_too()
    {
        _github.Answer = _ =>
        {
            var response = FakeGitHub.Json(HttpStatusCode.Forbidden, new JsonObject { ["message"] = "You have exceeded a secondary rate limit." });
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return response;
        };

        var error = await Assert.ThrowsAsync<TicketException>(() => Api().ListAsync(Source, TicketQuery.Default, cancellationToken: Token));

        Assert.Equal(TicketErrorKind.RateLimited, error.Kind);
        Assert.Equal(_time.GetUtcNow().AddSeconds(60), error.Until);
    }

    [Fact]
    public async Task A_repository_not_found_may_be_private_and_says_to_sign_in()
    {
        var missing = Source with { Project = "org/secret" };

        var anonymous = await Assert.ThrowsAsync<TicketException>(() => Api(token: null).ListAsync(missing, TicketQuery.Default, cancellationToken: Token));
        var signedIn = await Assert.ThrowsAsync<TicketException>(() => Api().ListAsync(missing, TicketQuery.Default, cancellationToken: Token));

        Assert.Equal(TicketErrorKind.NotFound, anonymous.Kind);
        Assert.Equal("GitHub didn't find org/secret. It may be private: sign in to GitHub in Settings → Tickets.", anonymous.Message);
        Assert.Equal("GitHub didn't find org/secret, or the token can't read it.", signedIn.Message);
    }

    [Fact]
    public async Task A_refused_token_is_refused_and_never_quoted()
    {
        _github.Answer = _ => FakeGitHub.Json(HttpStatusCode.Unauthorized, new JsonObject { ["message"] = $"Bad credentials for {FakeGitHub.Token}" });

        var error = await Assert.ThrowsAsync<TicketException>(() => Api().WhoAmIAsync(Token));

        Assert.Equal(TicketErrorKind.Refused, error.Kind);
        Assert.DoesNotContain(FakeGitHub.Token, error.ToString());
    }

    [Fact]
    public async Task Who_the_token_signs_in_as_and_nobody_without_one()
    {
        var me = await Api().WhoAmIAsync(Token);
        var nobody = await Api(token: null).WhoAmIAsync(Token);

        Assert.Equal(new TicketAccount("kim", "Kim Lee"), me);
        Assert.Null(nobody);
        Assert.Single(_github.RequestsCopy());
    }

    [Fact]
    public async Task A_ticket_has_its_text_fields_and_latest_comments()
    {
        _github.Add(142, "Grappling hook detaches", body: "When the player fires the hook…", milestone: "0.4", assignees: ["kim"]);
        _github.AddComments(142, 140);

        var detail = await Api().GetAsync(Source, "142", 100, Token);

        Assert.Equal("When the player fires the hook…", detail.Body);
        Assert.Equal([new TicketField("Milestone", "0.4")], detail.Fields);
        Assert.Equal(100, detail.Comments.Count);
        Assert.Equal("Comment 41", detail.Comments[0].Body);
        Assert.Equal("Comment 140", detail.Comments[^1].Body);
        Assert.Equal(140, detail.Ticket.Comments);
        Assert.Equal(40, detail.CommentsLeftOut);
    }

    [Fact]
    public async Task A_ticket_with_few_comments_reads_one_page()
    {
        _github.Add(7, "Bug");
        _github.AddComments(7, 3);

        var detail = await Api().GetAsync(Source, "7", 100, Token);

        Assert.Equal(["Comment 1", "Comment 2", "Comment 3"], detail.Comments.Select(c => c.Body));
        Assert.Equal(0, detail.CommentsLeftOut);
        Assert.Contains(_github.RequestsCopy(), r => r.Url.EndsWith("/issues/7/comments?per_page=100&page=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_GitHub_Enterprise_server_is_asked_at_its_own_api()
    {
        var api = Api(server: "github.studio.example");

        Assert.Equal("https://github.studio.example/api/v3", GitHubApi.BaseUrl("github.studio.example"));
        Assert.Equal("https://github.studio.example/org/game/issues", api.ListUrl(Source with { Server = "github.studio.example" }));
        Assert.Equal("https://github.com/org/game/issues", Api().ListUrl(Source));
        await Assert.ThrowsAsync<TicketException>(() => api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token));
        Assert.StartsWith("https://github.studio.example/api/v3/repos/org/game/issues?", _github.RequestsCopy()[^1].Url);
    }

    [Fact]
    public async Task Writing_back_comments_closes_reopens_and_assigns()
    {
        _github.Add(5, "Bug");
        var api = Api();
        var ticket = (await api.GetAsync(Source, "5", 100, Token)).Ticket;

        await api.CommentAsync(Source, "5", "On it.", Token);
        var states = await api.StatesAsync(Source, ticket, Token);
        await api.SetStateAsync(Source, "5", states.Single(s => s.Value == "not_planned"), Token);
        var closed = (await api.GetAsync(Source, "5", 100, Token)).Ticket;
        var reopen = Assert.Single(await api.StatesAsync(Source, closed, Token));
        await api.SetStateAsync(Source, "5", reopen, Token);
        await api.AssignToMeAsync(Source, "5", Token);

        Assert.Equal(["Close as completed", "Close as not planned"], states.Select(s => s.Label));
        Assert.Equal("Closed as not planned", closed.StateName);
        Assert.Equal("Reopen", reopen.Label);
        var issue = _github.Find(5)!;
        Assert.Equal("open", (string)issue["state"]!);
        Assert.Equal("reopened", (string)issue["state_reason"]!);
        Assert.Equal("kim", (string)issue["assignees"]![0]!["login"]!);
        Assert.Equal("On it.", (string)_github.Comments[5][0]["body"]!);
    }

    [Fact]
    public async Task Writing_back_needs_a_token()
    {
        _github.Add(5, "Bug");

        var error = await Assert.ThrowsAsync<TicketException>(() => Api(token: null).CommentAsync(Source, "5", "On it.", Token));

        Assert.Equal(TicketErrorKind.Refused, error.Kind);
        Assert.Empty(_github.RequestsCopy());
    }

    [Fact]
    public async Task A_token_that_may_only_read_is_told_what_it_needs()
    {
        _github.Add(5, "Bug");
        _github.Answer = r => r.Method == HttpMethod.Post
            ? FakeGitHub.Json(HttpStatusCode.Forbidden, new JsonObject { ["message"] = "Resource not accessible by personal access token" })
            : null;

        var error = await Assert.ThrowsAsync<TicketException>(() => Api().CommentAsync(Source, "5", "On it.", Token));

        Assert.Equal(TicketErrorKind.Forbidden, error.Kind);
        Assert.Equal("GitHub didn't let this token change the ticket. Give it Issues: Read and write.", error.Message);
    }

    [Fact]
    public async Task A_request_with_no_answer_runs_out_on_the_clock()
    {
        var handler = new HangingHandler();
        using var http = new HttpClient(handler);
        var api = new GitHubApi(new TicketTrackerContext(http, _time, GitHubTicketsType.GitHubCom, null, "Deventry/test"));

        var listing = api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token);
        await Waiting.UntilAsync(() => handler.Waiting > 0);
        _time.Advance(TicketHttp.RequestTimeout);

        var error = await Assert.ThrowsAsync<TicketException>(() => listing);
        Assert.Equal(TicketErrorKind.Unreachable, error.Kind);
        Assert.Equal("GitHub didn't answer in time.", error.Message);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        private int _waiting;

        public int Waiting => Volatile.Read(ref _waiting);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waiting);
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken);
            throw new InvalidOperationException("Never answers.");
        }
    }
}
