using System.Text.Json.Nodes;
using System.Web;
using Deventry.Core.Tests.Support;
using Deventry.Core.Tickets;
using Deventry.Core.Tickets.YouTrack;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Tickets;

/// <summary>YouTrack's issues (DESIGN.md §18, "Tickets", "Requests"), answered by a fake: nothing reaches YouTrack.</summary>
public sealed class YouTrackApiTests : IDisposable
{
    private static readonly TicketSource Source = new(YouTrackTicketsType.KindId, FakeYouTrack.Server, "NO", null, null, TicketSourceOrigin.Shared, 0);

    private readonly FakeYouTrack _youtrack = new();
    private readonly HttpClient _http;

    public YouTrackApiTests() => _http = new HttpClient(_youtrack);

    public void Dispose() => _http.Dispose();

    private YouTrackApi Api(string? token = FakeYouTrack.Token) =>
        new(new TicketTrackerContext(_http, new FakeTimeProvider(), FakeYouTrack.Server, token, "Deventry/test"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_lists_query_is_the_project_the_sources_query_the_state_Mine_the_words_and_the_order()
    {
        Assert.Equal("project: NO #Unresolved sort by: created desc", YouTrackApi.Query(Source, TicketQuery.Default));
        Assert.Equal("project: {Night Owl} Type: Bug #Resolved for: me crash sort by: updated desc",
            YouTrackApi.Query(Source with { Project = "Night Owl", Query = "Type: Bug" }, new TicketQuery(TicketStateFilter.Closed, true, " crash ", TicketSort.Updated)));
        Assert.Equal("project: NO sort by: created asc", YouTrackApi.Query(Source, new TicketQuery(TicketStateFilter.All, Sort: TicketSort.Oldest)));
    }

    [Fact]
    public async Task The_list_is_the_projects_unresolved_issues_with_their_fields()
    {
        _youtrack.Add(41, "Old one", daysAgo: 3);
        _youtrack.Add(42, "Rope drops the player", state: "In Progress", assignee: "Kim Lee", tags: ["regression"], daysAgo: 1);
        _youtrack.Add(40, "Done", state: "Fixed");

        var page = await Api().ListAsync(Source, TicketQuery.Default, cancellationToken: Token);

        Assert.Equal(["NO-42", "NO-41"], page.Tickets.Select(t => t.Key));
        var ticket = page.Tickets[0];
        Assert.Equal(("NO-42", "Rope drops the player", true, "In Progress"), (ticket.Id, ticket.Title, ticket.IsOpen, ticket.StateName));
        Assert.Equal(["regression"], ticket.Labels);
        Assert.Equal(["Kim Lee"], ticket.Assignees);
        Assert.Equal("Sam Park", ticket.Author);
        Assert.Equal("https://studio.youtrack.cloud/issue/NO-42", ticket.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero), ticket.Created);
        Assert.Null(page.Next);
        var (_, url, _, auth) = Assert.Single(_youtrack.RequestsCopy());
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("50", query["$top"]);
        Assert.Equal("0", query["$skip"]);
        Assert.Contains("customFields(name,value(name,login,fullName,presentation,text))", query["fields"]);
        Assert.Equal($"Bearer {FakeYouTrack.Token}", auth);
        Assert.DoesNotContain(FakeYouTrack.Token, url);
    }

    [Fact]
    public async Task A_resolved_issue_is_closed()
    {
        _youtrack.Add(40, "Done", state: "Fixed");

        var page = await Api().ListAsync(Source, new TicketQuery(TicketStateFilter.Closed), cancellationToken: Token);

        var ticket = Assert.Single(page.Tickets);
        Assert.False(ticket.IsOpen);
        Assert.Equal("Fixed", ticket.StateName);
    }

    [Fact]
    public async Task A_full_page_has_a_next_by_skip()
    {
        for (var i = 1; i <= 60; i++)
        {
            _youtrack.Add(i, $"Issue {i}", daysAgo: i);
        }
        var api = Api();

        var first = await api.ListAsync(Source, TicketQuery.Default, cancellationToken: Token);
        var second = await api.ListAsync(Source, TicketQuery.Default, first.Next, Token);

        Assert.Equal(50, first.Tickets.Count);
        Assert.Equal("50", first.Next);
        Assert.Equal(10, second.Tickets.Count);
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task A_project_that_isnt_there_says_which()
    {
        var error = await Assert.ThrowsAsync<TicketException>(() => Api().ListAsync(Source with { Project = "XX" }, TicketQuery.Default, cancellationToken: Token));

        Assert.Equal(TicketErrorKind.NotFound, error.Kind);
        Assert.Equal("YouTrack didn't find the project XX on studio.youtrack.cloud.", error.Message);
    }

    [Fact]
    public async Task A_ticket_has_its_description_fields_and_comments_but_not_deleted_ones()
    {
        _youtrack.Add(42, "Rope drops the player", assignee: "Kim Lee");
        _youtrack.Comment(42, "Repro'd on 5.4.2.");
        _youtrack.Comment(42, "Spam", deleted: true);
        _youtrack.Comment(42, "Not on 5.3.");

        var detail = await Api().GetAsync(Source, "NO-42", 100, Token);

        Assert.Equal("The rope **drops** the player.", detail.Body);
        Assert.Equal([new TicketField("Type", "Bug"), new TicketField("Priority", "Major")], detail.Fields);
        Assert.Equal(["Repro'd on 5.4.2.", "Not on 5.3."], detail.Comments.Select(c => c.Body));
        Assert.Equal("Kim Lee", detail.Comments[0].Author);
        Assert.Equal(2, detail.Ticket.Comments);
    }

    [Fact]
    public async Task Without_a_token_its_the_servers_guest()
    {
        Assert.Null(await Api(token: null).WhoAmIAsync(Token));
        Assert.Equal(new TicketAccount("kim", "Kim Lee"), await Api().WhoAmIAsync(Token));
    }

    [Fact]
    public async Task A_token_that_doesnt_work_is_refused_and_never_quoted()
    {
        var error = await Assert.ThrowsAsync<TicketException>(() => Api(token: "perm:wrong").WhoAmIAsync(Token));

        Assert.Equal(TicketErrorKind.Refused, error.Kind);
        Assert.Contains("Invalid token", error.Message);
        Assert.DoesNotContain("perm:wrong", error.ToString());
    }

    [Fact]
    public async Task The_states_are_the_projects_open_ones_first_without_the_current_or_archived_ones()
    {
        _youtrack.Add(42, "Rope drops the player", state: "Open");
        var ticket = (await Api().GetAsync(Source, "NO-42", 100, Token)).Ticket;

        var states = await Api().StatesAsync(Source, ticket, Token);
        await Api().SetStateAsync(Source, "NO-42", states[0], Token);

        Assert.Equal(["In Progress", "Fixed", "Won't fix"], states.Select(s => s.Label));
        Assert.Equal([false, true, true], states.Select(s => s.IsResolved));
        Assert.Equal("State {In Progress}", states[0].Value);
        var command = JsonNode.Parse(_youtrack.RequestsCopy()[^1].Body!)!;
        Assert.Equal("State {In Progress}", (string)command["query"]!);
        Assert.Equal("NO-42", (string)command["issues"]![0]!["idReadable"]!);
    }

    [Fact]
    public async Task Writing_back_comments_and_assigns_with_a_token_only()
    {
        _youtrack.Add(42, "Rope drops the player");

        await Api().CommentAsync(Source, "NO-42", "On it.", Token);
        await Api().AssignToMeAsync(Source, "NO-42", Token);
        var error = await Assert.ThrowsAsync<TicketException>(() => Api(token: null).CommentAsync(Source, "NO-42", "On it.", Token));

        var requests = _youtrack.RequestsCopy();
        Assert.Equal("On it.", (string)JsonNode.Parse(requests[0].Body!)!["text"]!);
        Assert.Equal("for me", (string)JsonNode.Parse(requests[1].Body!)!["query"]!);
        Assert.Equal(TicketErrorKind.Refused, error.Kind);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void The_browsers_list_is_the_projects_query()
    {
        Assert.Equal("https://studio.youtrack.cloud/issues?q=project%3A%20NO%20%23Unresolved",
            Api().ListUrl(Source with { Query = "#Unresolved" }));
    }

    [Theory]
    [InlineData("In Progress", "{In Progress}")]
    [InlineData("Fixed", "Fixed")]
    [InlineData("Won't fix", "{Won't fix}")]
    public void Words_with_spaces_go_in_braces(string word, string braced) => Assert.Equal(braced, YouTrackApi.Braced(word));
}
