using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.ProjectTools;
using Deventry.Core.Settings;
using Deventry.Core.Tests.Support;
using Deventry.Core.Tickets;

namespace Deventry.App.Tests;

/// <summary>
/// A project's Tickets tab (DESIGN.md §18, "Tickets"): the group's button and row, the list and a ticket from a fake
/// GitHub, Work on it into a new tab and a worktree, the tabs remembering their ticket, writing back, and Settings' and
/// the project settings' pages. Nothing reaches GitHub.
/// </summary>
public class TicketsTests
{
    private static FakeGitHub GitHub()
    {
        var github = new FakeGitHub();
        github.Add(142, "Grappling hook detaches on slopes", labels: ["bug", "gameplay"], comments: 0, daysAgo: 2, body: "The rope **drops** the player.", milestone: "0.4");
        github.AddComments(142, 2);
        github.Add(139, "Save slots overwrite each other", labels: ["bug"], daysAgo: 5);
        github.Add(140, "A pull request", pullRequest: true, daysAgo: 1);
        github.Add(120, "Footsteps on metal", state: "closed", stateReason: "completed", daysAgo: 30);
        return github;
    }

    private static void Sources(TabTestHarness h, string json = """{ "tickets": [ { "provider": "github", "repository": "org/game" } ] }""") =>
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName), json);

    /// <summary>A tab in the work folder, whose group's Tickets tab is opened from its button and has read its tickets.</summary>
    private static async Task<(TabViewModel Tab, TicketsViewModel Tickets)> OpenTicketsAsync(TabTestHarness h)
    {
        var tab = await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        await TabTestHarness.Eventually(() => group.HasTicketSources, "the Tickets button");
        InlineDispatcher.Run(() => h.Shell.OpenTicketsCommand.Execute(group));
        var tickets = group.Tickets!;
        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.HasRows, "the tickets read");
        return (tab, tickets);
    }

    private static async Task ChooseAsync(TicketsViewModel tickets, string key)
    {
        InlineDispatcher.Run(() => tickets.SelectedRow = tickets.Rows.Single(r => r.Key == key));
        await TabTestHarness.Eventually(() => tickets.Detail?.Key == key && !tickets.IsDetailLoading, $"{key} read");
    }

    [Fact]
    public async Task A_group_has_the_Tickets_button_only_while_its_folder_has_a_source()
    {
        await using var h = new TabTestHarness(http: GitHub());
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        await Waiting.NeverAsync(() => InlineDispatcher.Read(() => group.HasTicketSources), "a Tickets button without a source");

        Sources(h);
        InlineDispatcher.Run(h.Shell.OnAppActivated);

        await TabTestHarness.Eventually(() => group.HasTicketSources, "the Tickets button");
    }

    [Fact]
    public async Task The_Tickets_tab_lists_the_open_issues_without_pull_requests_after_the_Files_tab()
    {
        var github = GitHub();
        await using var h = new TabTestHarness(http: github);
        // The fake's issues are dated before 2026-11-13: the clock is after them, so none is new.
        h.Time.Advance(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero) - h.Time.GetUtcNow());
        Sources(h);

        var (_, tickets) = await OpenTicketsAsync(h);

        Assert.Same(tickets, h.Shell.Selected);
        Assert.Contains(tickets, h.Shell.OpenTabs);
        Assert.Equal(["#142", "#139"], tickets.Rows.Select(r => r.Key));
        Assert.Equal("bug · gameplay · 2 days ago · 2 comments", tickets.Rows[0].Detail);
        Assert.Equal("2 open", tickets.RowDetail);
        Assert.Equal("Tickets", tickets.DisplayName);
        Assert.False(tickets.HasSeveralSources);
        Assert.Equal("2 shown", tickets.ShownText);
        Assert.All(github.RequestsCopy(), r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task Choosing_a_ticket_reads_it_with_its_comments()
    {
        await using var h = new TabTestHarness(http: GitHub());
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);

        await ChooseAsync(tickets, "#142");

        var detail = tickets.Detail!;
        Assert.Equal("Grappling hook detaches on slopes", detail.Title);
        Assert.Equal("The rope **drops** the player.", detail.Body.ToString());
        Assert.Equal("Milestone: 0.4", detail.FieldsLine);
        Assert.Equal("2 comments", detail.CommentsHeader);
        Assert.Equal(["Comment 1", "Comment 2"], detail.Comments.Select(c => c.Markdown.ToString()));
        Assert.Equal("Open on GitHub", detail.OpenOnText);
        Assert.Equal("GitHub issue #142", detail.KindName);
        Assert.False(tickets.CanChange);
    }

    [Fact]
    public async Task Closed_and_the_filters_words_read_the_list_again()
    {
        var github = GitHub();
        await using var h = new TabTestHarness(http: github);
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);

        InlineDispatcher.Run(() => tickets.StateFilter = TicketStateFilter.Closed);
        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.Rows.Select(r => r.Key).SequenceEqual(["#120"]), "the closed tickets");

        InlineDispatcher.Run(() => tickets.StateFilter = TicketStateFilter.Open);
        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.Rows.Count == 2, "the open tickets again");
        InlineDispatcher.Run(() => tickets.FilterText = "grappling");
        Assert.DoesNotContain(github.RequestsCopy(), r => r.Url.Contains("/search/issues", StringComparison.Ordinal));
        h.Time.Advance(TimeSpan.FromSeconds(0.5));

        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.Rows.Select(r => r.Key).SequenceEqual(["#142"]), "the search");
        Assert.Contains(github.RequestsCopy(), r => r.Url.Contains("/search/issues", StringComparison.Ordinal));
        InlineDispatcher.Run(() => tickets.FilterText = "nothing like it");
        h.Time.Advance(TimeSpan.FromSeconds(0.5));
        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.Rows.Count == 0, "no match");
        Assert.Equal("Nothing matches \"nothing like it\".", tickets.EmptyText);
    }

    [Fact]
    public async Task A_private_repository_says_to_sign_in()
    {
        var github = GitHub();
        await using var h = new TabTestHarness(http: github);
        Sources(h, """{ "tickets": [ { "provider": "github", "repository": "org/secret" } ] }""");
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        await TabTestHarness.Eventually(() => group.HasTicketSources, "the Tickets button");

        InlineDispatcher.Run(() => h.Shell.OpenTicketsCommand.Execute(group));
        var tickets = group.Tickets!;
        await TabTestHarness.Eventually(() => tickets.HasListError, "the error");

        Assert.Equal("GitHub didn't find org/secret. It may be private: sign in to GitHub in Settings → Tickets.", tickets.ListError);
        Assert.True(tickets.ErrorOffersSettings);
        Assert.True(tickets.RowIsWarning);
        Assert.Equal(tickets.ListError, tickets.RowDetail);
    }

    [Fact]
    public async Task No_source_says_so_and_offers_to_add_one()
    {
        await using var h = new TabTestHarness(http: GitHub());
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();

        InlineDispatcher.Run(() => h.Shell.OpenTicketsCommand.Execute(group));
        var tickets = group.Tickets!;
        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.HasEmptyText, "the empty tab");

        Assert.Equal("No ticket sources for this project.", tickets.EmptyText);
        Assert.True(tickets.OffersAddSource);
        Assert.Equal("No ticket sources", tickets.RowDetail);
    }

    [Fact]
    public async Task Work_on_it_opens_a_tab_with_the_ticket_in_its_message_and_sends_nothing()
    {
        await using var h = new TabTestHarness(http: GitHub());
        Sources(h);
        var (first, tickets) = await OpenTicketsAsync(h);
        await ChooseAsync(tickets, "#142");

        await InlineDispatcher.RunAsync(() => tickets.WorkOnItCommand.ExecuteAsync(null));

        var tab = h.Shell.SelectedTab!;
        Assert.NotSame(first, tab);
        Assert.Equal(2, h.Shell.Groups.Single().Tabs.Count);
        Assert.Equal("#142 Grappling hook detaches on slopes", tab.DisplayName);
        Assert.Equal(TicketPrompt.Typed, tab.ComposerText);
        var paste = Assert.Single(tab.PastedTexts);
        Assert.True(TicketPrompt.IsTicket(paste.Text));
        Assert.StartsWith("GitHub issue #142 · 2 comments · ", paste.Label);
        Assert.Contains("## Comments (2)", paste.Text);
        Assert.Equal("#142", tab.State.Ticket!.Key);
        Assert.Equal("github:github.com/org/game", tab.State.Ticket.SourceId);
        Assert.True(tab.HasTicket);
        Assert.Contains(tab.InfoRows, r => r.Label == "Ticket" && r.Value == "GitHub issue #142: Grappling hook detaches on slopes");
        // The draft keeps it, as it keeps a paste.
        Assert.Equal([paste.Text], tab.Draft!.PastedTexts);
        Assert.Empty(h.Transport.SentUserTexts);
        // The ticket's row and its Tab on it follow.
        await TabTestHarness.Eventually(() => tickets.Rows.Single(r => r.Key == "#142").TabCount == 1, "the row's tab");
        Assert.EndsWith("· 1 tab", tickets.Rows.Single(r => r.Key == "#142").Detail);
        Assert.Same(tab, Assert.Single(tickets.Detail!.Tabs));
    }

    [Fact]
    public async Task Without_naming_or_comments_the_tab_keeps_its_name_and_the_ticket_has_no_comments()
    {
        await using var h = new TabTestHarness(configure: s =>
        {
            s.Tickets.NameTabs = false;
            s.Tickets.IncludeComments = false;
        }, http: GitHub());
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);
        await ChooseAsync(tickets, "#142");

        await InlineDispatcher.RunAsync(() => tickets.WorkOnItCommand.ExecuteAsync("tab"));

        var tab = h.Shell.SelectedTab!;
        Assert.False(tab.HasUserName);
        Assert.DoesNotContain("## Comments", Assert.Single(tab.PastedTexts).Text);
    }

    [Fact]
    public async Task Open_ticket_in_a_tabs_menu_goes_back_to_its_ticket()
    {
        await using var h = new TabTestHarness(http: GitHub());
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);
        await ChooseAsync(tickets, "#142");
        await InlineDispatcher.RunAsync(() => tickets.WorkOnItCommand.ExecuteAsync("tab"));
        var tab = h.Shell.SelectedTab!;
        InlineDispatcher.Run(() => tickets.SelectedRow = tickets.Rows.Single(r => r.Key == "#139"));
        InlineDispatcher.Run(() => h.Shell.CloseTickets(tickets));
        Assert.Null(h.Shell.Groups.Single().Tickets);

        InlineDispatcher.Run(() => h.Shell.OpenTabTicketCommand.Execute(tab));

        var back = h.Shell.Groups.Single().Tickets!;
        Assert.Same(back, h.Shell.Selected);
        await TabTestHarness.Eventually(() => back.Detail?.Key == "#142", "the tab's ticket");
    }

    [Fact]
    public async Task A_tickets_worktree_is_named_for_it()
    {
        Assert.Equal("issue-142", ShellViewModel.WorktreeNameFor(new Ticket("#142", "142", "t", true, "Open", [], null, default, default, 0, "", [])));
        Assert.Equal("no-42", ShellViewModel.WorktreeNameFor(new Ticket("NO-42", "NO-42", "t", true, "Open", [], null, default, default, 0, "", [])));

        Assert.SkipWhen(!WorktreeTabTests.GitInstalled, "git isn't on PATH.");
        await using var h = new TabTestHarness(http: GitHub());
        await WorktreeTabTests.InitRepositoryAsync(h);
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);
        await ChooseAsync(tickets, "#142");
        Directory.CreateDirectory(Core.Git.GitWorktrees.PathFor(h.WorkFolder, "issue-142"));

        await InlineDispatcher.RunAsync(() => tickets.WorkOnItCommand.ExecuteAsync("worktree"));

        var tab = h.Shell.SelectedTab!;
        Assert.Equal("issue-142-2", tab.State.NewWorktree);
        Assert.Equal("#142", tab.State.Ticket!.Key);
    }

    [Fact]
    public async Task Writing_back_is_off_until_turned_on_and_then_comments_and_closes()
    {
        var github = GitHub();
        var store = new FakeCredentialStore();
        await using var h = new TabTestHarness(http: github);
        h.Services.Tickets.Credentials = store;
        store.Secrets[TicketSecrets.Key("github", "github.com")] = FakeGitHub.Token;
        InlineDispatcher.Run(() => h.Services.Tickets.SetSignIn("github", "github.com", TicketSignIn.Token));
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);
        await ChooseAsync(tickets, "#139");
        Assert.False(tickets.CanChange);

        h.Services.Settings.Tickets.For("github").AllowChanges = true;
        InlineDispatcher.Run(h.Services.SaveSettings);
        await ChooseAsync(tickets, "#142");
        await TabTestHarness.Eventually(() => tickets.CanChange && tickets.HasStates, "the states");

        InlineDispatcher.Run(() => tickets.CommentText = "On it.");
        await InlineDispatcher.RunAsync(() => tickets.PostCommentCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => tickets.Detail!.Comments.Count == 3, "the comment shown");

        var close = tickets.States.Single(s => s.Value == "not_planned");
        await InlineDispatcher.RunAsync(() => tickets.ChooseStateCommand.ExecuteAsync(close));
        Assert.True(tickets.StateChange.IsOpen);
        Assert.Equal("open", (string)github.Find(142)!["state"]!);
        await InlineDispatcher.RunAsync(() => tickets.StateChange.ConfirmCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => tickets.Detail?.Ticket.IsOpen == false, "the ticket closed");

        Assert.Equal("Closed as not planned", tickets.Detail!.Ticket.StateName);
        Assert.Equal("On it.", (string)github.Comments[142][^1]["body"]!);
        Assert.All(github.RequestsCopy().Where(r => r.Method != HttpMethod.Get), r => Assert.Equal($"Bearer {FakeGitHub.Token}", r.Authorization));
    }

    [Fact]
    public async Task The_Tickets_tab_comes_back_after_a_restart_with_its_filter_and_ticket()
    {
        await using var h = new TabTestHarness(http: GitHub());
        Sources(h);
        var (_, tickets) = await OpenTicketsAsync(h);
        await ChooseAsync(tickets, "#139");
        InlineDispatcher.Run(() => tickets.Sort = TicketSort.Oldest);
        await TabTestHarness.Eventually(() => !tickets.IsLoading && tickets.Rows[0].Key == "#139", "the oldest first");

        var snapshot = h.Shell.CaptureForRestart();
        await h.Shell.CloseTabsForRestartAsync();
        InlineDispatcher.Run(() => h.Shell.Restore(null, snapshot));

        var back = Assert.IsType<TicketsViewModel>(h.Shell.Selected);
        await TabTestHarness.Eventually(() => back.Detail?.Key == "#139", "the ticket read again");
        Assert.Equal(TicketSort.Oldest, back.Sort);
        Assert.Same(back, h.Shell.Groups.Single().Tickets);
    }

    [Fact]
    public async Task Settings_keep_a_token_only_once_GitHub_takes_it_and_only_in_the_credential_store()
    {
        var github = GitHub();
        var store = new FakeCredentialStore();
        await using var h = new TabTestHarness(http: github);
        h.Services.Tickets.Credentials = store;
        using var settings = new SettingsViewModel(h.Services, null);
        var page = settings.TicketProvider("github");
        var row = Assert.Single(page.Servers);
        Assert.Equal("github.com", row.Name);

        InlineDispatcher.Run(() => row.SelectedSignIn = row.SignInChoices.Single(c => c.Value == TicketSignIn.Token));
        Assert.True(row.UsesToken);
        InlineDispatcher.Run(() => row.NewToken = "github_pat_wrong");
        await InlineDispatcher.RunAsync(() => row.SaveTokenCommand.ExecuteAsync(null));
        Assert.StartsWith("It wasn't kept: GitHub didn't accept the token", row.Error);
        Assert.Empty(store.Secrets);

        InlineDispatcher.Run(() => row.NewToken = FakeGitHub.Token);
        await InlineDispatcher.RunAsync(() => row.SaveTokenCommand.ExecuteAsync(null));

        Assert.Null(row.Error);
        Assert.Equal(FakeGitHub.Token, store.Secrets["tickets/github/github.com"]);
        Assert.Equal("Deventry: GitHub token for github.com", store.Labels["tickets/github/github.com"]);
        Assert.Equal("Signed in as kim (Kim Lee)", row.StateText);
        Assert.DoesNotContain(FakeGitHub.Token, System.Text.Json.JsonSerializer.Serialize(h.Services.Settings, JsonFileStore<AppSettings>.Options));
        Assert.DoesNotContain(FakeGitHub.Token, System.Text.Json.JsonSerializer.Serialize(h.Services.State, JsonFileStore<AppState>.Options));
    }

    [Fact]
    public async Task Settings_add_a_YouTrack_server_and_refuse_an_address_that_isnt_one()
    {
        await using var h = new TabTestHarness(http: GitHub());
        using var settings = new SettingsViewModel(h.Services, null);
        var page = settings.TicketProvider("youtrack");
        Assert.True(page.HasNoServers);

        InlineDispatcher.Run(() =>
        {
            page.NewServer = "ftp://nope";
            page.AddServerCommand.Execute(null);
        });
        Assert.StartsWith("That isn't a server's address", page.ServerError);

        InlineDispatcher.Run(() =>
        {
            page.NewServer = "dotbunny.youtrack.cloud";
            page.AddServerCommand.Execute(null);
        });
        await TabTestHarness.Eventually(() => page.Servers.Count == 1, "the server");
        Assert.Equal("https://dotbunny.youtrack.cloud", page.Servers[0].Server);
        Assert.True(page.Servers[0].CanRemove);
        Assert.Equal("https://dotbunny.youtrack.cloud", Assert.Single(h.Services.Settings.Tickets.For("youtrack").Servers).Server);
    }

    [Fact]
    public async Task The_project_settings_add_a_source_and_removing_it_brings_the_one_from_git_back()
    {
        Assert.SkipWhen(!WorktreeTabTests.GitInstalled, "git isn't on PATH.");
        await using var h = new TabTestHarness(http: GitHub());
        await WorktreeTabTests.InitRepositoryAsync(h);
        await WorktreeTabTests.GitAsync(h.WorkFolder, "remote", "add", "origin", "https://github.com/org/game.git");
        await h.OpenTabAsync();
        var project = h.Shell.ProjectSettingsFor(h.WorkFolder)!;
        var found = Assert.Single(project.TicketSources);
        Assert.True(found.IsFromGit);
        Assert.Equal("Found from the git remote", found.FileText);

        InlineDispatcher.Run(() => project.AddTicketSourceCommand.Execute(null));
        var editor = Assert.IsType<TicketSourceEditorViewModel>(project.Editor);
        Assert.Equal("org/game", editor.Project);
        InlineDispatcher.Run(() =>
        {
            editor.SelectedType = editor.Types.Single(t => t.Kind == "youtrack");
            editor.Server = "studio.youtrack.cloud";
            editor.Project = "NO";
            editor.Query = "#Unresolved";
            editor.SaveToShared = true;
            editor.SaveCommand.Execute(null);
        });

        var row = Assert.Single(project.TicketSources);
        Assert.Equal("youtrack", row.Source!.Provider);
        var written = JsonNode.Parse(File.ReadAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName)))!;
        Assert.Equal("https://studio.youtrack.cloud", (string)written["tickets"]![0]!["server"]!);
        Assert.Equal("#Unresolved", (string)written["tickets"]![0]!["query"]!);

        InlineDispatcher.Run(() =>
        {
            project.SelectedTicketSource = row;
            project.RemoveTicketSourceCommand.Execute(null);
        });
        InlineDispatcher.Run(() => project.Dispose());
        var again = h.Shell.ProjectSettingsFor(h.WorkFolder)!;
        Assert.True(Assert.Single(again.TicketSources).IsFromGit);
        Assert.Null(JsonNode.Parse(File.ReadAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName)))!["tickets"]);
        again.Dispose();
    }

    [Fact]
    public void All_providers_says_where_Work_on_it_opens()
    {
        var settings = new AppSettings();
        Assert.Equal(TicketWorkIn.Tab, settings.Tickets.WorkIn);
        Assert.True(settings.Tickets.NameTabs);
        Assert.True(settings.Tickets.IncludeComments);
        Assert.Null(settings.Tickets.Find("github"));
        Assert.Equal(["Deventry", "Claude", "Your work", "Project tools", "Source control", "Tickets", "Remote clients"],
            SettingsSidebar.Groups([], [], Services.TicketsService.AllTypes).Select(g => g.Heading));
        Assert.Equal([SettingsCategory.AllTicketProviders, "GitHub", "YouTrack"], SettingsSidebar.Groups([], [], Services.TicketsService.AllTypes)[5].Categories);
    }
}
