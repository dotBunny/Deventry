using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core;
using Deventry.Core.Development;
using Deventry.Core.Diffs;
using Deventry.Core.Library;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>The session library, leases and History, wired into tabs (DESIGN.md §9).</summary>
public class LibraryAndHistoryTests
{
    [Fact]
    public async Task A_finished_turn_is_copied_to_the_library()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        tab.SetSyncToLibrary(true);
        h.WriteTranscript("s1", UserLine("s1", "Fix the login bug", h.WorkFolder));
        tab.State.UserName = "Login fix";

        h.Transport.EmitTurn();

        // The lease is taken last, after the transcript and the record are written: wait for the whole save.
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the library copy");
        Assert.NotNull(h.Services.Library.Library.GetTranscriptPath("s1"));
        var entry = Assert.Single(h.Services.Library.Library.List());
        Assert.Equal("Login fix", entry.Record.Name);
        Assert.Equal(h.Services.Library.MachineName, entry.Record.Machine);
        Assert.Equal(120, entry.Record.Tokens.Total);
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("s1"));
    }

    [Fact]
    public async Task History_lists_local_sessions_and_resumes_one()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("old-1", UserLine("old-1", "Refactor the parser", h.WorkFolder), """{"type":"ai-title","aiTitle":"Parser refactor","sessionId":"old-1"}""");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        var entry = Assert.Single(Assert.Single(history.Groups).Entries);
        Assert.Equal("Parser refactor", entry.Title);
        Assert.Equal("This machine", entry.Machine);

        await history.OpenCommand.ExecuteAsync(entry);

        Assert.False(h.Shell.IsHistoryOpen);
        var tab = h.Shell.SelectedTab!;
        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.Equal("old-1", h.Factory.Launches[0].Resume);
        // It only ever lived on this machine, so it stays here.
        Assert.False(tab.SyncToLibrary);
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem { Text: "Refactor the parser" });
        // When the session began, not when this tab started its process.
        var started = DateTimeOffset.Parse("2026-09-28T10:00:00Z", CultureInfo.InvariantCulture);
        Assert.Equal(started, tab.State.SessionStartedAt);
        Assert.Contains(tab.InfoRows, r => r.Label == "Started" && r.Value == started.ToLocalTime().ToString("g"));
    }

    [Fact]
    public async Task Searching_Claudes_replies_adds_sessions_only_a_reply_matches()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("s1", UserLine("s1", "Why does the build fail?", h.WorkFolder), ReplyLine("s1", "Set VULKAN_SDK before you build."));
        h.WriteTranscript("s2", UserLine("s2", "Add Vulkan support", h.WorkFolder), ReplyLine("s2", "Done."));
        h.WriteTranscript("s3", UserLine("s3", "Rename the parser", h.WorkFolder), ReplyLine("s3", "Renamed."));
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await SearchAsync(h, history, "vulkan");
        Assert.Equal(["s2"], history.Groups.SelectMany(g => g.Entries).Select(e => e.SessionId));
        // The view's one list: the folder's heading, then its sessions.
        Assert.Equal(["work", "s2"], history.Rows.Select(r => r is HistoryHeading heading ? heading.Label : ((HistoryEntry)r).SessionId));

        await history.SearchRepliesCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => history.Groups.SelectMany(g => g.Entries).Count() == 2, "the reply's session");

        var fromReply = history.Groups.SelectMany(g => g.Entries).Single(e => e.SessionId == "s1");
        Assert.Equal("Set VULKAN_SDK before you build.", fromReply.MatchedReply);
        Assert.Null(history.Groups.SelectMany(g => g.Entries).Single(e => e.SessionId == "s2").MatchedReply);
        Assert.Equal("Found 1 more session in Claude's replies.", history.ReplySearchText);

        // A new search starts again from the prompts.
        history.Search = "vulkan rename";
        Assert.Null(history.ReplySearchText);
        Assert.Null(fromReply.MatchedReply);
        await SearchAsync(h, history, "vulkan rename");
        Assert.Empty(history.Groups);
        Assert.True(history.IsEmpty);
    }

    [Fact]
    public async Task History_shows_sessions_as_it_reads_them_the_first_time_it_opens()
    {
        var dispatcher = new HistoryHoldingDispatcher();
        await using var h = new TabTestHarness(dispatcher: dispatcher);
        try
        {
            WriteSession(h, "old", "2026-09-28T08:00:00Z");
            WriteSession(h, "newest", "2026-09-28T10:00:00Z");
            WriteSession(h, "middle", "2026-09-28T09:00:00Z");
            h.OpenHistory();
            var history = h.Shell.History!;

            // The most recently written is read first, and shows while the rest are still to read.
            await TabTestHarness.Eventually(() => history.Rows.Count > 0, "the first session");
            Assert.Equal(["work", "newest"], InlineDispatcher.Read(() => RowNames(history)));
            Assert.True(history.IsLoading);
            Assert.False(history.IsFirstLoad);

            dispatcher.Release();
            await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
            Assert.Equal(["work", "newest", "middle", "old"], RowNames(history));
        }
        finally
        {
            dispatcher.Release();
        }
    }

    [Fact]
    public async Task A_refresh_replaces_History_once_the_new_list_is_whole()
    {
        var dispatcher = new HistoryHoldingDispatcher();
        dispatcher.Release();
        await using var h = new TabTestHarness(dispatcher: dispatcher);
        WriteSession(h, "s1", "2026-09-28T08:00:00Z");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
        var shown = dispatcher.HistoryPosts;
        Assert.True(shown > 0);

        WriteSession(h, "s2", "2026-09-28T09:00:00Z");
        await history.RefreshCommand.ExecuteAsync(null);

        // Nothing shown part way: the list stayed as it was until the new one replaced it.
        Assert.Equal(shown, dispatcher.HistoryPosts);
        Assert.Equal(["work", "s2", "s1"], RowNames(history));
    }

    [Fact]
    public async Task History_chips_narrow_the_list_to_a_project_with_worktrees_under_their_repository()
    {
        await using var h = new TabTestHarness();
        var api = Path.Combine(Path.GetDirectoryName(h.WorkFolder)!, "api");
        var worktree = Path.Combine(h.WorkFolder, ".claude", "worktrees", "brisk-otter");
        WriteSession(h, "w1", "2026-09-28T08:00:00Z", prompt: "Fix the parser");
        WriteSession(h, "a1", "2026-09-28T09:00:00Z", api, prompt: "Add retries");
        WriteSession(h, "a2", "2026-09-28T07:00:00Z", api, prompt: "Rename the parser");
        WriteSession(h, "wt", "2026-09-28T10:00:00Z", worktree, prompt: "Try a new parser", branch: "worktree-brisk-otter");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        // Every project, by its latest session: the worktree's session is the repository's.
        Assert.True(history.IsAllSelected);
        Assert.Equal(["work", "api"], history.Chips.Select(c => c.Label));
        Assert.Equal([2, 2], history.Chips.Select(c => c.Count));
        Assert.Equal(4, history.AllCount);
        Assert.False(history.HasMoreProjects);
        Assert.Equal(["work", "wt", "w1", "api", "a1", "a2"], RowNames(history));
        var fromWorktree = history.Groups[0].Entries[0];
        Assert.Equal(worktree, fromWorktree.Folder);
        // Its branch is the worktree's own, so the worktree says both.
        Assert.EndsWith("worktree brisk-otter", fromWorktree.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("worktree-brisk-otter", fromWorktree.Details, StringComparison.Ordinal);

        var apiChip = history.Chips[1];
        history.SelectProjectCommand.Execute(apiChip);
        await history.Filtering;
        Assert.Same(apiChip, history.SelectedProject);
        Assert.True(apiChip.IsSelected);
        Assert.Equal(["api", "a1", "a2"], RowNames(history));

        // The counts follow the search; the chips hold their places.
        await SearchAsync(h, history, "parser");
        Assert.Equal(["api", "a2"], RowNames(history));
        Assert.Equal(["work", "api"], history.Chips.Select(c => c.Label));
        Assert.Equal([2, 1], history.Chips.Select(c => c.Count));
        Assert.Equal(3, history.AllCount);

        // The selected chip again, like All, lists every project.
        history.SelectProjectCommand.Execute(apiChip);
        await history.Filtering;
        Assert.True(history.IsAllSelected);
        Assert.False(apiChip.IsSelected);
        Assert.Equal(["work", "wt", "w1", "api", "a2"], RowNames(history));
    }

    [Fact]
    public async Task Projects_past_the_chips_are_under_more_and_one_picked_from_there_takes_the_last_chip()
    {
        await using var h = new TabTestHarness();
        var parent = Path.GetDirectoryName(h.WorkFolder)!;
        for (var i = 1; i <= 7; i++)
        {
            WriteSession(h, $"s{i}", $"2026-09-28T{10 - i:00}:00:00Z", Path.Combine(parent, $"project{i}"));
        }
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        Assert.Equal(["project1", "project2", "project3", "project4", "project5"], history.Chips.Select(c => c.Label));
        Assert.Equal(["project6", "project7"], history.MoreProjects.Select(c => c.Label));
        Assert.Equal("+2 more", history.MoreText);

        var seventh = history.MoreProjects[1];
        history.SelectProjectCommand.Execute(seventh);
        await history.Filtering;
        Assert.Equal(["project1", "project2", "project3", "project4", "project7"], history.Chips.Select(c => c.Label));
        Assert.Equal(["project5", "project6"], history.MoreProjects.Select(c => c.Label));
        Assert.Equal(["project7", "s7"], RowNames(history));

        history.SelectProjectCommand.Execute(null);
        await history.Filtering;
        Assert.Equal(["project1", "project2", "project3", "project4", "project5"], history.Chips.Select(c => c.Label));
    }

    [Fact]
    public async Task History_for_a_group_lists_only_its_project_and_says_when_it_has_no_sessions()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        var api = Path.Combine(Path.GetDirectoryName(h.WorkFolder)!, "api");
        WriteSession(h, "a1", "2026-09-28T09:00:00Z", api);

        h.Shell.OpenHistoryForGroupCommand.Execute(h.Shell.Groups.Single());
        var history = h.Shell.History!;
        // Its chip is there before anything is read.
        Assert.Equal("work", history.SelectedProject?.Label);
        Assert.Equal(["work"], history.Chips.Select(c => c.Label));
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        Assert.True(history.IsEmpty);
        Assert.Equal("No sessions in work yet.", history.EmptyText);
        Assert.Equal(["api", "work"], history.Chips.Select(c => c.Label));
        Assert.Equal([1, 0], history.Chips.Select(c => c.Count));

        history.SelectProjectCommand.Execute(null);
        await history.Filtering;
        Assert.Equal(["api", "a1"], RowNames(history));
        Assert.Equal(["api"], history.Chips.Select(c => c.Label));
    }

    /// <summary>A session written when it was last active, as Claude Code writes them.</summary>
    private static void WriteSession(TabTestHarness h, string sessionId, string time, string? folder = null, string? prompt = null, string? branch = null)
    {
        var line = JsonNode.Parse(UserLine(sessionId, prompt ?? $"Prompt for {sessionId}", folder ?? h.WorkFolder, time))!;
        if (branch is not null)
        {
            line["gitBranch"] = branch;
        }
        var path = h.WriteTranscript(sessionId, line.ToJsonString());
        File.SetLastWriteTimeUtc(path, DateTimeOffset.Parse(time, CultureInfo.InvariantCulture).UtcDateTime);
    }

    /// <summary>History's list as the view shows it: each folder's heading, then its sessions.</summary>
    private static string[] RowNames(HistoryViewModel history) =>
        [.. history.Rows.Select(r => r is HistoryHeading heading ? heading.Label : ((HistoryEntry)r).SessionId)];

    /// <summary>
    /// Runs work at once, as <see cref="InlineDispatcher"/> does, and counts the shows History posts as it reads
    /// sessions. Until <see cref="Release"/>, each holds the scan that posted it, so a test sees History part read.
    /// </summary>
    private sealed class HistoryHoldingDispatcher : IUiDispatcher
    {
        private readonly InlineDispatcher _inner = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _historyPosts;

        public int HistoryPosts => Volatile.Read(ref _historyPosts);

        public void Post(Action action)
        {
            _inner.Post(action);
            // Its lambdas' closures are nested in the view model.
            if (action.Method.DeclaringType is { } type && (type == typeof(HistoryViewModel) || type.DeclaringType == typeof(HistoryViewModel)))
            {
                Interlocked.Increment(ref _historyPosts);
                _released.Task.Wait();
            }
        }

        public void Release() => _released.TrySetResult();
    }

    [Fact]
    public async Task A_library_session_from_another_machine_resumes_from_a_local_copy()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-1", "DESKTOP-01");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
        var entry = history.Groups.SelectMany(g => g.Entries).Single();
        Assert.Equal("DESKTOP-01", entry.Machine);
        Assert.False(entry.IsLocal);

        await history.OpenCommand.ExecuteAsync(entry);

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        var resume = h.Factory.Launches[0].Resume!;
        Assert.StartsWith(h.Services.Paths.LocalSessionsDirectory, resume, StringComparison.Ordinal);
        Assert.EndsWith("lib-1.jsonl", resume, StringComparison.Ordinal);
        Assert.False(h.Factory.Launches[0].ForkSession);
        // Someone synced it, so it keeps syncing here.
        Assert.True(h.Shell.SelectedTab!.SyncToLibrary);
    }

    [Fact]
    public async Task A_session_open_on_another_machine_can_be_opened_as_a_copy()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-2", "DESKTOP-01");
        var lease = Path.Combine(h.Services.Library.Library.GetSessionFolder("lib-2"), LeaseManager.FileName);
        await File.WriteAllTextAsync(lease, new JsonObject { ["machine"] = "DESKTOP-01", ["owner"] = "other", ["updatedAt"] = h.Time.GetUtcNow().ToString("O") }.ToJsonString(), TestContext.Current.CancellationToken);
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());

        var confirmation = Assert.IsType<ConfirmationViewModel>(h.Shell.Confirmation);
        Assert.Contains("DESKTOP-01", confirmation.Title, StringComparison.Ordinal);
        Assert.Equal("Take over", confirmation.ConfirmText);
        Assert.Equal("Open a copy", confirmation.SecondaryText);

        await confirmation.SecondaryCommand.ExecuteAsync(null);

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.True(h.Factory.Launches[0].ForkSession);
        // A copy of a library session syncs too, under its own id once it has one.
        Assert.True(h.Shell.SelectedTab!.SyncToLibrary);
    }

    [Fact]
    public async Task A_tab_taken_over_elsewhere_becomes_read_only()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        tab.SetSyncToLibrary(true);
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.Leases.HeldSessions.Contains("s1");
        }, "the lease");

        var lease = Path.Combine(h.Services.Library.Library.GetSessionFolder("s1"), LeaseManager.FileName);
        await File.WriteAllTextAsync(lease, new JsonObject { ["machine"] = "LAPTOP-02", ["owner"] = "other", ["updatedAt"] = h.Time.GetUtcNow().ToString("O") }.ToJsonString(), TestContext.Current.CancellationToken);
        h.Services.Library.Leases.RefreshAll();

        await TabTestHarness.Eventually(() => tab.IsReadOnly, "read-only");
        Assert.Equal("LAPTOP-02", tab.TakenOverBy);
        tab.ComposerText = "more";
        Assert.False(tab.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_turn_that_ends_after_a_take_over_doesnt_copy_over_it()
    {
        // Leases are refreshed once a minute; the copy after a turn must not wait for that, or it would overwrite what
        // the other machine wrote and take the session back.
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        tab.SetSyncToLibrary(true);
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.Leases.HeldSessions.Contains("s1");
        }, "the lease");
        var library = h.Services.Library.Library.GetTranscriptPath("s1")!;
        var copied = await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken);
        var lease = Path.Combine(h.Services.Library.Library.GetSessionFolder("s1"), LeaseManager.FileName);
        var theirs = new JsonObject { ["machine"] = "LAPTOP-02", ["owner"] = "other", ["updatedAt"] = h.Time.GetUtcNow().ToString("O") }.ToJsonString();
        await File.WriteAllTextAsync(lease, theirs, TestContext.Current.CancellationToken);

        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder), UserLine("s1", "written after the take-over", h.WorkFolder));
        h.Transport.EmitTurn();

        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return tab.IsReadOnly;
        }, "read-only");
        Assert.Equal("LAPTOP-02", tab.TakenOverBy);
        Assert.Equal(copied, await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken));
        Assert.Equal(theirs, await File.ReadAllTextAsync(lease, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Opening_a_library_session_takes_its_lease_as_it_starts()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-3", "DESKTOP-01");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("lib-3"));
    }

    [Fact]
    public async Task Opening_a_library_session_counts_as_using_it_for_retention_but_not_for_History()
    {
        await using var h = new TabTestHarness();
        var lastUsed = h.Time.GetUtcNow().AddDays(-20);
        await SaveToLibraryAsync(h, "lib-4", "DESKTOP-01", lastUsed);
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        var record = h.Services.Library.Library.List().Single().Record;
        Assert.Equal(h.Time.GetUtcNow(), record.LastOpened);
        // History still says which machine last used it, and when.
        Assert.Equal(("DESKTOP-01", lastUsed), (record.Machine, record.LastUsed));
        // Not kept as an open tab here, but opened since its last turn: retention leaves it.
        h.Services.Library.Prune(new HashSet<string>());
        Assert.NotNull(h.Services.Library.Library.GetTranscriptPath("lib-4"));
    }

    [Fact]
    public async Task Library_sessions_unused_for_the_days_chosen_go_at_launch_and_once_a_day_but_open_tabs_keep_theirs()
    {
        await using var h = new TabTestHarness();
        var now = h.Time.GetUtcNow();
        await SaveToLibraryAsync(h, "old", "DESKTOP-01", now.AddDays(-20));
        await SaveToLibraryAsync(h, "recent", "DESKTOP-01", now.AddDays(-10));
        await SaveToLibraryAsync(h, "open-1", "DESKTOP-01", now.AddDays(-20));
        h.WriteTranscript("open-1", UserLine("open-1", "Add the export button", h.WorkFolder));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "open-1", SyncToLibrary = true }];
        bool InLibrary(string id) => h.Services.Library.Library.GetTranscriptPath(id) is not null;

        h.Shell.Restore(null);

        // 15 days by default.
        await TabTestHarness.Eventually(() => !InLibrary("old"), "the pass at launch");
        Assert.True(InLibrary("recent"));
        Assert.True(InLibrary("open-1"));

        // Deventry stays open: the next day's pass goes by the days chosen then.
        h.Services.Settings.Sessions.KeepLibrarySessionDays = 7;
        h.Time.Advance(LibraryService.PruneInterval);
        await TabTestHarness.Eventually(() => !InLibrary("recent"), "the next day's pass");
        Assert.True(InLibrary("open-1"));

        // Off keeps everything.
        h.Services.Settings.Sessions.DeleteOldLibrarySessions = false;
        h.Services.Library.Prune(new HashSet<string>());
        Assert.True(InLibrary("open-1"));
    }

    [Fact]
    public async Task A_session_on_this_machine_that_another_machine_has_open_asks_first()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("both-1", UserLine("both-1", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "both-1", "DESKTOP-01", lastUsed: DateTimeOffset.Parse("2026-09-28T09:00:00Z", CultureInfo.InvariantCulture));
        var lease = Path.Combine(h.Services.Library.Library.GetSessionFolder("both-1"), LeaseManager.FileName);
        await File.WriteAllTextAsync(lease, new JsonObject { ["machine"] = "DESKTOP-01", ["owner"] = "other", ["updatedAt"] = h.Time.GetUtcNow().ToString("O") }.ToJsonString(), TestContext.Current.CancellationToken);
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
        var entry = history.Groups.SelectMany(g => g.Entries).Single();
        Assert.True(entry.IsLocal);
        Assert.False(entry.ContinuedElsewhere);

        await history.OpenCommand.ExecuteAsync(entry);

        var confirmation = Assert.IsType<ConfirmationViewModel>(h.Shell.Confirmation);
        Assert.Empty(h.Factory.Launches);
        await confirmation.ConfirmCommand.ExecuteAsync(null);

        // Taken over: resumed from this machine's own transcript, with the lease now held here.
        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.Equal("both-1", h.Factory.Launches[0].Resume);
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("both-1"));
        Assert.True(h.Shell.SelectedTab!.SyncToLibrary);
    }

    [Fact]
    public async Task A_session_whose_lease_an_earlier_run_here_left_opens_without_asking()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("crash-1", UserLine("crash-1", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "crash-1", h.Services.Library.MachineName, lastUsed: DateTimeOffset.Parse("2026-09-28T09:00:00Z", CultureInfo.InvariantCulture));
        await LeaveLeaseAsync(h, "crash-1");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());

        // Nothing has it open: it isn't open "on" this machine, and opening it takes the lease.
        Assert.Null(h.Shell.Confirmation);
        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.Equal("crash-1", h.Factory.Launches[0].Resume);
        Assert.False(h.Factory.Launches[0].ForkSession);
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("crash-1"));
    }

    [Fact]
    public async Task A_session_continued_on_another_machine_opens_from_the_library()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("both-2", UserLine("both-2", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "both-2", "DESKTOP-01", lastUsed: DateTimeOffset.Parse("2026-09-28T12:00:00Z", CultureInfo.InvariantCulture));
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
        var entry = history.Groups.SelectMany(g => g.Entries).Single();
        Assert.True(entry.ContinuedElsewhere);

        await history.OpenCommand.ExecuteAsync(entry);

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.StartsWith(h.Services.Paths.LocalSessionsDirectory, h.Factory.Launches[0].Resume!, StringComparison.Ordinal);
        Assert.True(h.Shell.SelectedTab!.SyncToLibrary);
    }

    [Fact]
    public async Task A_restored_tab_whose_session_carried_on_elsewhere_becomes_read_only_instead_of_starting()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("pinned-1", UserLine("pinned-1", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "pinned-1", "LAPTOP-02");
        var lease = Path.Combine(h.Services.Library.Library.GetSessionFolder("pinned-1"), LeaseManager.FileName);
        await File.WriteAllTextAsync(lease, new JsonObject { ["machine"] = "LAPTOP-02", ["owner"] = "other", ["updatedAt"] = h.Time.GetUtcNow().ToString("O") }.ToJsonString(), TestContext.Current.CancellationToken);
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "pinned-1", SyncToLibrary = true }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();

        await tab.EnsureStartedAsync();

        Assert.Equal("LAPTOP-02", tab.TakenOverBy);
        Assert.Empty(h.Factory.Launches);
    }

    [Fact]
    public async Task A_restored_tab_whose_lease_an_earlier_run_here_left_starts_and_takes_it()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("pinned-3", UserLine("pinned-3", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "pinned-3", h.Services.Library.MachineName);
        await LeaveLeaseAsync(h, "pinned-3");
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "pinned-3", SyncToLibrary = true }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();

        await tab.EnsureStartedAsync();

        // Not read-only, as if the session had carried on somewhere: it carries on here.
        Assert.Null(tab.TakenOverBy);
        Assert.Equal("pinned-3", Assert.Single(h.Factory.Launches).Resume);
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("pinned-3"));
    }

    [Fact]
    public async Task A_restored_tab_that_does_not_sync_ignores_leases()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("pinned-2", UserLine("pinned-2", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "pinned-2", "LAPTOP-02");
        await HoldLeaseAsync(h, "pinned-2", "LAPTOP-02");
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "pinned-2" }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();

        await tab.EnsureStartedAsync();

        // It carries on with this machine's own transcript, and leaves the other machine's lease alone.
        Assert.Null(tab.TakenOverBy);
        Assert.Equal("pinned-2", Assert.Single(h.Factory.Launches).Resume);
        Assert.IsType<LeaseStatus.HeldByOther>(h.Services.Library.CheckLease("pinned-2"));
        Assert.Empty(h.Services.Library.Leases.HeldSessions);
    }

    // ---- Another machine's turns (DESIGN.md §9, "One machine at a time") ---------------------------------------------

    [Fact]
    public async Task A_restored_tab_picks_up_where_another_machine_carried_its_session_on()
    {
        await using var h = new TabTestHarness();
        var first = UserLine("pin-3", "Add the export button", h.WorkFolder);
        h.WriteTranscript("pin-3", first);
        // The laptop carried the session on, then quit Deventry, which released its lease.
        var library = await CarriedOnElsewhereAsync(h, "pin-3", first, UserLine("pin-3", "Now the import", h.WorkFolder, "2026-09-28T12:00:00Z"));
        var tab = RestorePinned(h, "pin-3");

        await tab.EnsureStartedAsync();

        // It resumes a working copy of the library's, with the laptop's turn in the conversation.
        var resume = Assert.Single(h.Factory.Launches).Resume!;
        Assert.StartsWith(h.Services.Paths.LocalSessionsDirectory, resume, StringComparison.Ordinal);
        Assert.Equal(await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(resume, TestContext.Current.CancellationToken));
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem { Text: "Now the import" });
        Assert.Contains(Notes(tab), n => n.Text.StartsWith("Picked up where LAPTOP-02 left off", StringComparison.Ordinal));
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("pin-3"));

        // Its next turn goes into the library after the laptop's.
        await File.AppendAllLinesAsync(resume, [ReplyLine("pin-3", "Done")], TestContext.Current.CancellationToken);
        h.Transport.EmitTurn(sessionId: "pin-3");
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        await SettleAsync(h);
        var lines = await File.ReadAllLinesAsync(library, TestContext.Current.CancellationToken);
        Assert.Equal(3, lines.Length);
        Assert.Contains("Now the import", lines[1], StringComparison.Ordinal);
        Assert.Null(tab.TakenOverBy);
    }

    [Fact]
    public async Task A_restored_tab_whose_session_went_on_in_both_places_keeps_this_machines_turns_apart()
    {
        await using var h = new TabTestHarness();
        var first = UserLine("pin-4", "Add the export button", h.WorkFolder);
        var local = h.WriteTranscript("pin-4", first, UserLine("pin-4", "Here, on the desktop", h.WorkFolder, "2026-09-28T11:00:00Z"));
        var library = await CarriedOnElsewhereAsync(h, "pin-4", first, UserLine("pin-4", "There, on the laptop", h.WorkFolder, "2026-09-28T12:00:00Z"));
        var laptops = await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken);
        var tab = RestorePinned(h, "pin-4");

        await tab.EnsureStartedAsync();

        // Nothing starts, and nothing is lost: the library keeps the laptop's, beside a copy of this machine's.
        Assert.Empty(h.Factory.Launches);
        Assert.Equal("LAPTOP-02", tab.TakenOverBy);
        Assert.Equal(laptops, await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken));
        var copy = Assert.Single(h.Services.Library.Library.List(), e => e.IsConflictCopy);
        Assert.Equal(await File.ReadAllTextAsync(local, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(copy.TranscriptPath, TestContext.Current.CancellationToken));
        Assert.Contains(Notes(tab), n => n.Text.Contains("LAPTOP-02 carried this session on too", StringComparison.Ordinal) && n.Text.Contains($"\"{copy.ConflictLabel}\" in History", StringComparison.Ordinal));
        // The conversation shown is this machine's, and the lease is left for the laptop.
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem { Text: "Here, on the desktop" });
        Assert.IsType<LeaseStatus.Free>(h.Services.Library.CheckLease("pin-4"));
    }

    [Fact]
    public async Task A_tab_whose_turns_were_kept_apart_never_starts_again()
    {
        await using var h = new TabTestHarness();
        var first = UserLine("pin-7", "Add the export button", h.WorkFolder);
        h.WriteTranscript("pin-7", first, UserLine("pin-7", "Here, on the desktop", h.WorkFolder, "2026-09-28T11:00:00Z"));
        await CarriedOnElsewhereAsync(h, "pin-7", first, UserLine("pin-7", "There, on the laptop", h.WorkFolder, "2026-09-28T12:00:00Z"));
        var tab = RestorePinned(h, "pin-7");
        await tab.EnsureStartedAsync();
        Assert.True(tab.IsReadOnly);

        // Shown again, which starts a tab whose Claude Code isn't running: it stays read-only, offers no Restart, and
        // leaves the lease for the laptop.
        await tab.EnsureStartedAsync();

        Assert.False(tab.CanRestart);
        Assert.Empty(h.Factory.Launches);
        Assert.IsType<LeaseStatus.Free>(h.Services.Library.CheckLease("pin-7"));
    }

    [Fact]
    public async Task Turns_that_reach_the_library_after_the_tab_started_are_picked_up_while_it_waits()
    {
        await using var h = new TabTestHarness();
        var first = UserLine("pin-5", "Add the export button", h.WorkFolder);
        h.WriteTranscript("pin-5", first);
        await CarriedOnElsewhereAsync(h, "pin-5", first);
        var tab = RestorePinned(h, "pin-5");
        await tab.EnsureStartedAsync();
        Assert.Equal("pin-5", Assert.Single(h.Factory.Launches).Resume);

        // The sync client brings the laptop's turn only now.
        await CarriedOnElsewhereAsync(h, "pin-5", first, UserLine("pin-5", "Now the import", h.WorkFolder, "2026-09-28T12:00:00Z"));
        h.Time.Advance(TabViewModel.LibraryCheckInterval);

        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && tab.Status == TabStatus.Idle, "the tab to start again from the library's copy");
        Assert.StartsWith(h.Services.Paths.LocalSessionsDirectory, h.Factory.Launches[1].Resume!, StringComparison.Ordinal);
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem { Text: "Now the import" });
        Assert.Single(tab.Items, i => i is Conversation.UserMessageItem { Text: "Add the export button" });
        Assert.Contains(Notes(tab), n => n.Text.StartsWith("Picked up where LAPTOP-02 left off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_first_message_after_another_machine_carried_the_session_on_stays_in_the_composer()
    {
        await using var h = new TabTestHarness();
        var first = UserLine("pin-6", "Add the export button", h.WorkFolder);
        h.WriteTranscript("pin-6", first);
        await CarriedOnElsewhereAsync(h, "pin-6", first);
        var tab = RestorePinned(h, "pin-6");
        await tab.EnsureStartedAsync();
        await CarriedOnElsewhereAsync(h, "pin-6", first, UserLine("pin-6", "Now the import", h.WorkFolder, "2026-09-28T12:00:00Z"));

        tab.ComposerText = "Ship it";
        await tab.SendCommand.ExecuteAsync(null);

        // Not sent: the laptop's turn is read first, and the message waits to be sent again.
        Assert.Equal(2, h.Factory.Launches.Count);
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem { Text: "Now the import" });
        Assert.Equal("Ship it", tab.ComposerText);
        Assert.DoesNotContain("Ship it", h.Transport.SentUserTexts);
        Assert.Contains(Notes(tab), n => n.Text.StartsWith("Your message wasn't sent", StringComparison.Ordinal));

        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains("Ship it"), "the message");
    }

    [Fact]
    public async Task A_restored_tab_that_has_not_started_looks_at_the_library_before_its_first_message()
    {
        await using var h = new TabTestHarness();
        var first = UserLine("pin-8", "Add the export button", h.WorkFolder);
        h.WriteTranscript("pin-8", first);
        await CarriedOnElsewhereAsync(h, "pin-8", first);
        // Selected, it reads its conversation without starting (DESIGN.md §9, "Starting fast").
        var tab = RestorePinned(h, "pin-8");
        await TabTestHarness.Eventually(() => Notes(tab).Any(n => n.Text == "Resumed."), "the earlier conversation");
        // The laptop carries the session on meanwhile, and closes Deventry, which lets its lease go.
        await CarriedOnElsewhereAsync(h, "pin-8", first, UserLine("pin-8", "Now the import", h.WorkFolder, "2026-09-28T12:00:00Z"));

        tab.ComposerText = "Ship it";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));

        // Not sent from this machine's older copy: the laptop's turn is read first, and the message waits to be sent again.
        var resume = Assert.Single(h.Factory.Launches).Resume!;
        Assert.StartsWith(h.Services.Paths.LocalSessionsDirectory, resume, StringComparison.Ordinal);
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem { Text: "Now the import" });
        Assert.Equal("Ship it", tab.ComposerText);
        Assert.DoesNotContain("Ship it", h.Transport.SentUserTexts);
        Assert.Contains(Notes(tab), n => n.Text.StartsWith("Your message wasn't sent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_copy_after_a_turn_never_writes_over_turns_another_machine_added()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        var first = UserLine("s1", "Add the export button", h.WorkFolder);
        var local = h.WriteTranscript("s1", first);
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        await SettleAsync(h);
        // The laptop carried it on from there, but its lease never reached this machine before the sync client brought
        // its turn: a turn here meanwhile goes another way.
        var library = await CarriedOnElsewhereAsync(h, "s1", first, UserLine("s1", "There, on the laptop", h.WorkFolder, "2026-09-28T12:00:00Z"));
        var laptops = await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken);
        await File.AppendAllLinesAsync(local, [UserLine("s1", "Here, on the desktop", h.WorkFolder, "2026-09-28T12:01:00Z")], TestContext.Current.CancellationToken);

        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 240, "the turn to finish");
        await SettleAsync(h);

        Assert.Equal(laptops, await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken));
        var copy = Assert.Single(h.Services.Library.Library.List(), e => e.IsConflictCopy);
        Assert.Equal(await File.ReadAllTextAsync(local, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(copy.TranscriptPath, TestContext.Current.CancellationToken));
        Assert.Equal("LAPTOP-02", tab.TakenOverBy);
        Assert.IsNotType<LeaseStatus.Mine>(h.Services.Library.CheckLease("s1"));
    }

    [Fact]
    public async Task Turning_sync_on_for_a_session_that_went_on_elsewhere_too_leaves_it_off()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var first = UserLine("s1", "Add the export button", h.WorkFolder);
        h.WriteTranscript("s1", first, UserLine("s1", "Here, on the desktop", h.WorkFolder, "2026-09-28T11:00:00Z"));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        var library = await CarriedOnElsewhereAsync(h, "s1", first, UserLine("s1", "There, on the laptop", h.WorkFolder, "2026-09-28T12:00:00Z"));
        var laptops = await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken);

        tab.SetSyncToLibrary(true);
        await SettleAsync(h);

        Assert.False(tab.SyncToLibrary);
        Assert.Null(tab.TakenOverBy);
        Assert.Equal(laptops, await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(h.Services.Library.Library.List(), e => e.IsConflictCopy);
        Assert.Contains(Notes(tab), n => n.Text.Contains("Sync stays off", StringComparison.Ordinal));
    }

    /// <summary>A pinned tab that syncs, restored as Deventry starts.</summary>
    private static TabViewModel RestorePinned(TabTestHarness h, string sessionId)
    {
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = sessionId, SyncToLibrary = true }];
        h.Shell.Restore(null);
        return h.Shell.AllTabs.Single();
    }

    /// <summary>The session's library copy as LAPTOP-02 left it, with no lease: that machine closed Deventry since. Returns the library's transcript.</summary>
    private static async Task<string> CarriedOnElsewhereAsync(TabTestHarness h, string sessionId, params string[] lines)
    {
        var source = Path.Combine(h.Root, $"{sessionId}-laptop.jsonl");
        await File.WriteAllLinesAsync(source, lines);
        var record = new SessionRecord
        {
            SessionId = sessionId,
            Name = "Export button",
            Machine = "LAPTOP-02",
            LastUsed = DateTimeOffset.Parse("2026-09-28T12:00:00Z", CultureInfo.InvariantCulture),
            Folder = h.WorkFolder,
            Tokens = new TokenTotals(),
        };
        // As the laptop wrote it: the whole file, whatever was there.
        await h.Services.Library.Library.SaveAsync(record, source, subagentsDirectory: null, force: true);
        File.Delete(Path.Combine(h.Services.Library.Library.GetSessionFolder(sessionId), LeaseManager.FileName));
        return h.Services.Library.Library.GetTranscriptPath(sessionId)!;
    }

    [Fact]
    public async Task A_library_session_opens_with_its_tab_overrides()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-4", "DESKTOP-01", overrides: new TabOverrides { Model = "claude-sonnet-5-5", PermissionMode = "plan", ShowProcessMonitor = true });
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.Equal("claude-sonnet-5-5", h.Factory.Launches[0].Model);
        Assert.Equal("plan", h.Factory.Launches[0].PermissionMode);
        Assert.True(h.Shell.SelectedTab!.State.Overrides.ShowProcessMonitor);
    }

    [Fact]
    public async Task A_turn_saves_the_tab_overrides_to_the_library()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        tab.SetSyncToLibrary(true);
        tab.State.Overrides.Effort = "high";
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();

        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.Library.List().Any(e => e.Record.SessionId == "s1");
        }, "the library copy");
        var record = h.Services.Library.Library.List().Single(e => e.Record.SessionId == "s1").Record;
        Assert.Equal("high", record.Overrides?.Effort);
    }

    [Fact]
    public async Task A_library_session_opens_with_its_reviewed_files_in_this_machines_folder()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-5", "DESKTOP-01", reviewedFiles: [new ReviewedFile { Path = "src/auth.cs", Change = "e1" }]);
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());

        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.Equal([(Path.Combine(h.WorkFolder, "src", "auth.cs"), "e1")], h.Shell.SelectedTab!.State.ReviewedFiles.Select(m => (m.Path, m.Change)));
    }

    [Fact]
    public async Task Ticking_a_file_in_a_tab_that_syncs_updates_its_record_without_waiting_for_a_turn()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        // The tokens come with the result, and the tab is Idle again only with the event after it: sync turned on while
        // the tab still works waits for a turn that won't come.
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120 && !tab.IsWorking, "the turn to finish");
        tab.SetSyncToLibrary(true);
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the first copy");
        Assert.Empty(Record().ReviewedFiles ?? []);
        var path = Path.Combine(h.WorkFolder, "src", "auth.cs");

        tab.ChangedFiles.ToggleFileReviewedCommand.Execute(new ChangedFileRow { Path = path, DisplayPath = "src/auth.cs", Status = "M", StatusText = "Modified", FromGit = true });

        // Relative to the folder, which has another path on another machine.
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return Record().ReviewedFiles is [{ Path: "src/auth.cs", Change: null }];
        }, "the record");

        SessionRecord Record() => h.Services.Library.Library.List().Single(e => e.Record.SessionId == "s1").Record;
    }

    // ---- Syncing is per tab, and opt-in (DESIGN.md §9, "Session library") -------------------------------------------

    [Fact]
    public async Task A_new_tab_writes_nothing_to_the_library_and_takes_no_lease()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Assert.False(tab.SyncToLibrary);
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));

        h.Transport.EmitTurn();

        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        await SettleAsync(h);
        Assert.Empty(h.Services.Library.Library.List());
        Assert.Null(h.Services.Library.Library.GetTranscriptPath("s1"));
        Assert.IsType<LeaseStatus.Free>(h.Services.Library.CheckLease("s1"));
        Assert.Empty(h.Services.Library.Leases.HeldSessions);
    }

    [Fact]
    public async Task Turning_sync_on_copies_the_session_straight_away_and_takes_its_lease()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        // Idle again too, which comes with the event after the result's: sync turned on while the tab works waits for
        // the turn's end.
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120 && !tab.IsWorking, "the turn to finish");

        tab.ToggleSyncToLibraryCommand.Execute(null);

        Assert.True(tab.SyncToLibrary);
        Assert.True(Assert.Single(h.Services.State.Tabs).SyncToLibrary);
        // No turn needed: the copy follows as soon as the transcript has settled.
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the library copy");
        Assert.NotNull(h.Services.Library.Library.GetTranscriptPath("s1"));
        Assert.Equal(120, Assert.Single(h.Services.Library.Library.List()).Record.Tokens.Total);
    }

    [Fact]
    public async Task Turning_sync_on_during_a_turn_copies_the_session_when_the_turn_ends()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "long job", h.WorkFolder));
        tab.ComposerText = "long job";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit("""{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-5-5"}""");
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "working");

        tab.SetSyncToLibrary(true);

        // Never while Claude Code is writing the transcript.
        await SettleAsync(h);
        Assert.Null(h.Services.Library.Library.GetTranscriptPath("s1"));
        Assert.Empty(h.Services.Library.Leases.HeldSessions);

        h.Transport.EmitTurn();

        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the library copy");
        Assert.NotNull(h.Services.Library.Library.GetTranscriptPath("s1"));
    }

    [Fact]
    public async Task Turning_sync_off_releases_the_lease_and_leaves_the_copy()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the library copy");

        tab.ToggleSyncToLibraryCommand.Execute(null);

        Assert.False(tab.SyncToLibrary);
        // Released in the background, off the UI thread.
        await TabTestHarness.Eventually(() => h.Services.Library.CheckLease("s1") is LeaseStatus.Free, "the release");
        Assert.Empty(h.Services.Library.Leases.HeldSessions);
        // Later turns stay on this machine; other machines still see the copy as it was.
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 240, "the second turn");
        await SettleAsync(h);
        Assert.NotNull(h.Services.Library.Library.GetTranscriptPath("s1"));
        Assert.Equal(120, Assert.Single(h.Services.Library.Library.List()).Record.Tokens.Total);
        Assert.IsType<LeaseStatus.Free>(h.Services.Library.CheckLease("s1"));
    }

    [Fact]
    public async Task New_tabs_sync_when_Settings_says_so()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);

        var tab = await h.OpenTabAsync();

        Assert.True(tab.SyncToLibrary);
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the library copy");
    }

    [Fact]
    public async Task A_session_on_this_machine_with_a_library_copy_opens_syncing()
    {
        await using var h = new TabTestHarness();
        h.WriteTranscript("both-3", UserLine("both-3", "Add the export button", h.WorkFolder));
        await SaveToLibraryAsync(h, "both-3", h.Services.Library.MachineName, lastUsed: DateTimeOffset.Parse("2026-09-28T09:00:00Z", CultureInfo.InvariantCulture));
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
        var entry = history.Groups.SelectMany(g => g.Entries).Single();
        Assert.True(entry.IsLocal);
        Assert.False(entry.ContinuedElsewhere);

        await history.OpenCommand.ExecuteAsync(entry);

        // Resumed from this machine's own transcript, and syncing, so it takes the lease as it starts.
        h.Start();
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1, "the resume");
        Assert.Equal("both-3", h.Factory.Launches[0].Resume);
        Assert.True(h.Shell.SelectedTab!.SyncToLibrary);
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("both-3"));
    }

    [Fact]
    public async Task A_tab_cannot_start_syncing_a_session_another_machine_has_open()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        await SaveToLibraryAsync(h, "s1", "LAPTOP-02");
        await HoldLeaseAsync(h, "s1", "LAPTOP-02");

        tab.ToggleSyncToLibraryCommand.Execute(null);

        Assert.False(tab.SyncToLibrary);
        Assert.Contains(InlineDispatcher.Read(() => tab.Items.OfType<Conversation.NoteItem>().ToArray()), n => n.Text.Contains("LAPTOP-02", StringComparison.Ordinal));
        await SettleAsync(h);
        Assert.Equal("LAPTOP-02", Assert.Single(h.Services.Library.Library.List()).Record.Machine);
        Assert.IsType<LeaseStatus.HeldByOther>(h.Services.Library.CheckLease("s1"));
    }

    [Fact]
    public async Task Tab_settings_turn_sync_on_and_Use_defaults_leaves_it()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var settings = new TabSettingsViewModel(h.Services, tab, () => { });
        Assert.False(settings.SyncToLibrary);

        settings.SyncToLibrary = true;
        settings.UseAllDefaults();
        await settings.DoneCommand.ExecuteAsync(null);

        // It's the tab's own state, not an override.
        Assert.True(tab.SyncToLibrary);
        Assert.False(tab.HasOverrides);
        Assert.True(new TabSettingsViewModel(h.Services, tab, () => { }).SyncToLibrary);
    }

    [Fact]
    public async Task The_sync_switch_survives_saving_restoring_and_a_restart_into_a_new_build()
    {
        var saved = new List<TabState>();
        RestartSnapshot? snapshot;
        await using (var h = new TabTestHarness())
        {
            h.Services.Settings.Sessions.RestoreUnpinnedTabs = true;
            h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, UserName = "synced" }, new TabState { Folder = h.WorkFolder, UserName = "local" }];
            h.Shell.Restore(null);
            h.Shell.AllTabs.First().SetSyncToLibrary(true);
            await h.Services.FlushAsync();
            saved.AddRange(new JsonFileStore<AppState>(h.Services.Paths.StateFile).Load().Tabs);

            var captured = h.Shell.CaptureForRestart();
            captured.Nonce = "n";
            captured.CreatedAt = h.Time.GetUtcNow();
            captured.Save(h.Services.Paths.RestartFile);
            snapshot = RestartSnapshot.Load(h.Services.Paths.RestartFile, "n", h.Time.GetUtcNow());
        }
        Assert.Equal([true, false], saved.Select(t => t.SyncToLibrary));
        Assert.NotNull(snapshot);
        Assert.Equal([true, false], snapshot.Tabs.Select(t => t.SyncToLibrary));

        await using var launched = new TabTestHarness(s => s.Sessions.RestoreUnpinnedTabs = true);
        launched.Services.State.Tabs = saved;
        launched.Shell.Restore(null);
        Assert.Equal([true, false], launched.Shell.AllTabs.Select(t => t.SyncToLibrary));

        await using var restarted = new TabTestHarness();
        restarted.Shell.Restore(null, snapshot);
        Assert.Equal([true, false], restarted.Shell.AllTabs.Select(t => t.SyncToLibrary));
    }

    // ---- Sync now (DESIGN.md §9, "Which tabs sync") -----------------------------------------------------------------

    [Fact]
    public async Task Sync_now_copies_the_session_straight_away_every_file_again_and_says_so()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        var transcript = h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        var agent = Path.Combine(Path.GetDirectoryName(transcript)!, "s1", SessionLibrary.SubagentsFolderName, "agent-a1.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(agent)!);
        await File.WriteAllLinesAsync(agent, [ReplyLine("s1", "Found it")], TestContext.Current.CancellationToken);
        h.Transport.EmitTurn();
        // The copy starts with the result, and the tab is Idle again, which Sync now needs, only with the event after it.
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine && !tab.IsWorking;
        }, "the library copy");
        // Renamed since the turn, and the library's copy of the subagent's transcript changed without its size or time
        // changing, so an ordinary copy would skip it.
        tab.State.UserName = "Login fix";
        var copy = Path.Combine(h.Services.Library.Library.GetSessionFolder("s1"), SessionLibrary.SubagentsFolderName, "agent-a1.jsonl");
        var source = await File.ReadAllTextAsync(copy, TestContext.Current.CancellationToken);
        var copyTime = File.GetLastWriteTimeUtc(copy);
        await File.WriteAllTextAsync(copy, new string('x', source.Length), TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(copy, copyTime);
        Assert.True(tab.SyncNowCommand.CanExecute(null));

        await SyncNowAsync(h, tab);

        Assert.Equal(source, await File.ReadAllTextAsync(copy, TestContext.Current.CancellationToken));
        Assert.Equal("Login fix", Assert.Single(h.Services.Library.Library.List()).Record.Name);
        Assert.IsType<LeaseStatus.Mine>(h.Services.Library.CheckLease("s1"));
        Assert.Contains(Notes(tab), n => n is { Text: "Copied this session to the session library.", Kind: Conversation.NoteKind.Info });
    }

    [Fact]
    public async Task Sync_now_is_only_there_for_a_tab_that_syncs_with_a_session_Claude_is_not_writing()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Assert.False(tab.SyncNowCommand.CanExecute(null));

        tab.SetSyncToLibrary(true);

        // Nothing to copy until the first message.
        Assert.False(tab.SyncNowCommand.CanExecute(null));
        Assert.Contains("nothing to copy", tab.SyncNowTip, StringComparison.Ordinal);

        h.WriteTranscript("s1", UserLine("s1", "long job", h.WorkFolder));
        tab.ComposerText = "long job";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit("""{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-5-5"}""");
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "working");

        // Never while Claude Code is writing the transcript: the turn's end copies it anyway.
        Assert.False(tab.SyncNowCommand.CanExecute(null));
        Assert.Contains("when the turn ends", tab.SyncNowTip, StringComparison.Ordinal);

        h.Transport.EmitTurn();

        await TabTestHarness.Eventually(() => tab.SyncNowCommand.CanExecute(null), "Sync now after the turn");
        Assert.Contains("without waiting", tab.SyncNowTip, StringComparison.Ordinal);

        await tab.OnTakenOverAsync("LAPTOP-02");

        Assert.False(tab.SyncNowCommand.CanExecute(null));
        Assert.Contains("LAPTOP-02", tab.SyncNowTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_now_writes_nothing_to_a_session_another_machine_took_over_and_the_tab_becomes_read_only()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        var transcript = h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return h.Services.Library.CheckLease("s1") is LeaseStatus.Mine;
        }, "the library copy");
        // Taken over since the last lease refresh, which hasn't noticed yet.
        await HoldLeaseAsync(h, "s1", "LAPTOP-02");
        await File.AppendAllTextAsync(transcript, UserLine("s1", "written here", h.WorkFolder) + "\n", TestContext.Current.CancellationToken);

        await SyncNowAsync(h, tab);

        Assert.Equal("LAPTOP-02", tab.TakenOverBy);
        await SettleAsync(h);
        Assert.DoesNotContain("written here", await File.ReadAllTextAsync(h.Services.Library.Library.GetTranscriptPath("s1")!, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.IsType<LeaseStatus.HeldByOther>(h.Services.Library.CheckLease("s1"));
        Assert.DoesNotContain(Notes(tab), n => n.Text.StartsWith("Copied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_now_says_why_when_the_library_folder_is_not_available()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        await SettleAsync(h);
        // A file where the library's folder should be, as when a sync client's drive isn't mounted.
        var blocked = Path.Combine(h.Root, "not-a-folder");
        await File.WriteAllTextAsync(blocked, "", TestContext.Current.CancellationToken);
        h.Services.Settings.Sessions.LibraryFolder = blocked;
        h.Services.Library.OnSettingsChanged();

        await SyncNowAsync(h, tab);

        var note = Assert.Single(Notes(tab), n => n.Text.StartsWith("Couldn't copy this session to the session library: ", StringComparison.Ordinal));
        Assert.Equal(Conversation.NoteKind.Warning, note.Kind);
        Assert.True(tab.SyncToLibrary);
    }

    [Fact]
    public async Task A_copy_after_a_turn_that_fails_says_so_once_and_again_when_it_works()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        var library = h.Services.Settings.Sessions.LibraryFolder;
        var blocked = Path.Combine(h.Root, "not-a-folder");
        await File.WriteAllTextAsync(blocked, "", TestContext.Current.CancellationToken);
        h.Services.Settings.Sessions.LibraryFolder = blocked;
        h.Services.Library.OnSettingsChanged();
        static bool Failed(Conversation.NoteItem n) => n.Text.StartsWith("Couldn't copy this session to the session library: ", StringComparison.Ordinal);

        for (var turn = 1; turn <= 2; turn++)
        {
            h.Transport.EmitTurn();
            await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120 * turn && tab.IsSettled, "the turn to finish");
            await CopyAfterTurnAsync(h, tab);
        }
        var note = Assert.Single(Notes(tab), Failed);
        Assert.Equal(Conversation.NoteKind.Warning, note.Kind);
        Assert.EndsWith("It's tried again after the next turn, or use Sync now.", note.Text, StringComparison.Ordinal);

        h.Services.Settings.Sessions.LibraryFolder = library;
        h.Services.Library.OnSettingsChanged();
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 360 && tab.IsSettled, "the turn to finish");
        await CopyAfterTurnAsync(h, tab);
        Assert.Equal("Copied this session to the session library again.", Notes(tab)[^1].Text);
    }

    // ---- What it takes in the library (DESIGN.md §9, "Writing") -----------------------------------------------------

    [Fact]
    public async Task A_tab_that_syncs_shows_what_its_session_takes_in_the_library_after_each_copy()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        var transcript = h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        // Nothing until the first copy.
        Assert.Null(tab.SyncSize);
        Assert.Equal("Synced to the session library", tab.SyncTip);
        Assert.DoesNotContain(tab.InfoRows, r => r.Label == "Sync size");

        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120 && tab.IsSettled, "the turn to finish");
        await CopyAfterTurnAsync(h, tab);

        var size = Formats.Size(h.Services.Library.Library.GetSessionSize("s1")!.Value);
        await TabTestHarness.Eventually(() => tab.SyncSize == size, "the size");
        Assert.Equal($"Synced to the session library · {size}", tab.SyncTip);
        Assert.Contains(tab.InfoRows, r => r is { Label: "Sync size" } && r.Value == $"{size} in the session library");
        // Saved with the tab, so it shows from launch.
        await h.Services.FlushAsync();
        var saved = Assert.Single(new JsonFileStore<AppState>(h.Services.Paths.StateFile).Load().Tabs).SyncedSize!;
        Assert.Equal(("s1", h.Services.Library.Library.GetSessionSize("s1")), (saved.SessionId, (long?)saved.Bytes));

        // A turn adds to the transcript, and so to what the library keeps.
        await File.AppendAllTextAsync(transcript, UserLine("s1", new string('x', 200 * 1024), h.WorkFolder) + "\n", TestContext.Current.CancellationToken);
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 240 && tab.IsSettled, "the second turn");
        await CopyAfterTurnAsync(h, tab);

        var grown = Formats.Size(h.Services.Library.Library.GetSessionSize("s1")!.Value);
        Assert.NotEqual(size, grown);
        await TabTestHarness.Eventually(() => tab.SyncSize == grown, "the new size");
    }

    [Fact]
    public async Task The_size_shows_only_while_the_tab_syncs_and_only_for_its_own_session()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120 && tab.IsSettled, "the turn to finish");
        await CopyAfterTurnAsync(h, tab);
        await TabTestHarness.Eventually(() => tab.SyncSize is not null, "the size");
        var size = tab.SyncSize;

        tab.ToggleSyncToLibraryCommand.Execute(null);

        // Turned off, the library's copy stays as it was, but nothing here keeps it up.
        Assert.Null(tab.SyncSize);
        Assert.DoesNotContain(tab.InfoRows, r => r.Label == "Sync size");
        await TabTestHarness.Eventually(() => h.Services.Library.CheckLease("s1") is LeaseStatus.Free, "the release");

        // On again, the copy already there shows straight away.
        tab.ToggleSyncToLibraryCommand.Execute(null);
        Assert.Equal(size, tab.SyncSize);
        await SettleAsync(h);

        // After /clear the session is a new one, which the library doesn't have yet.
        h.Transport.EmitTurn(sessionId: "s2");
        await TabTestHarness.Eventually(() => tab.State.SessionId == "s2", "the new session");
        Assert.Null(tab.SyncSize);
        Assert.Equal("Synced to the session library", tab.SyncTip);
        await SettleAsync(h);
    }

    [Fact]
    public async Task A_restored_tab_shows_the_size_it_saved_for_its_own_session()
    {
        await using var h = new TabTestHarness(s => s.Sessions.RestoreUnpinnedTabs = true);
        h.Services.State.Tabs =
        [
            new TabState { Folder = h.WorkFolder, UserName = "synced", SessionId = "s1", SyncToLibrary = true, SyncedSize = new SyncedSize { SessionId = "s1", Bytes = 312L * 1024 * 1024 } },
            // Measured for the session it had before /clear.
            new TabState { Folder = h.WorkFolder, UserName = "cleared", SessionId = "s2", SyncToLibrary = true, SyncedSize = new SyncedSize { SessionId = "s0", Bytes = 2048 } },
        ];

        h.Shell.Restore(null);

        var tabs = h.Shell.AllTabs.ToArray();
        Assert.Equal("312 MB", tabs[0].SyncSize);
        Assert.Equal("Synced to the session library · 312 MB", tabs[0].SyncTip);
        Assert.Contains(tabs[0].InfoRows, r => r is { Label: "Sync size", Value: "312 MB in the session library" });
        Assert.Null(tabs[1].SyncSize);
    }

    [Fact]
    public async Task A_library_session_shows_what_it_takes_there_as_it_starts()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-4", "DESKTOP-01");
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        await history.OpenCommand.ExecuteAsync(history.Groups.SelectMany(g => g.Entries).Single());
        h.Start();

        // Before any turn: read with its lease as the tab starts.
        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => tab.SyncSize is not null, "the size");
        Assert.Equal(Formats.Size(h.Services.Library.Library.GetSessionSize("lib-4")!.Value), tab.SyncSize);
    }

    [Fact]
    public async Task Tab_settings_say_what_the_session_takes_in_the_library_while_sync_is_ticked()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        Assert.Null(new TabSettingsViewModel(h.Services, tab, () => { }).SyncSizeText);
        h.WriteTranscript("s1", UserLine("s1", "hello", h.WorkFolder));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120 && tab.IsSettled, "the turn to finish");
        await CopyAfterTurnAsync(h, tab);
        await TabTestHarness.Eventually(() => tab.SyncSize is not null, "the size");

        var settings = new TabSettingsViewModel(h.Services, tab, () => { });

        Assert.Equal($"This session takes {tab.SyncSize} in the session library.", settings.SyncSizeText);
        settings.SyncToLibrary = false;
        Assert.Null(settings.SyncSizeText);
    }

    [Fact]
    public async Task History_says_what_a_session_takes_in_the_library()
    {
        await using var h = new TabTestHarness();
        await SaveToLibraryAsync(h, "lib-5", "DESKTOP-01");
        h.WriteTranscript("local-1", UserLine("local-1", "Only here", h.WorkFolder));
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");

        var entries = history.Groups.SelectMany(g => g.Entries).ToDictionary(e => e.SessionId);

        Assert.EndsWith($" · {Formats.Size(h.Services.Library.Library.GetSessionSize("lib-5")!.Value)} in the library", entries["lib-5"].Details, StringComparison.Ordinal);
        // A session only on this machine takes nothing there.
        Assert.DoesNotContain("in the library", entries["local-1"].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_say_what_the_whole_library_takes_each_time_the_page_shows()
    {
        await using var h = new TabTestHarness();
        var settings = new SettingsViewModel(h.Services, null, opening: new SettingsOpening(SettingsCategory.Sessions));
        await settings.Sessions.Measuring;
        Assert.Equal("The library is empty.", settings.Sessions.LibrarySizeText);

        await SaveToLibraryAsync(h, "lib-6", "DESKTOP-01");
        await SaveToLibraryAsync(h, "lib-7", "DESKTOP-01");
        // Measured again as the page shows, not as the library changes.
        Assert.Equal("The library is empty.", settings.Sessions.LibrarySizeText);
        settings.SelectedCategory = SettingsCategory.General;
        settings.SelectedCategory = SettingsCategory.Sessions;
        await settings.Sessions.Measuring;

        var bytes = h.Services.Library.Library.GetSessionSize("lib-6")!.Value + h.Services.Library.Library.GetSessionSize("lib-7")!.Value;
        Assert.Equal($"The library holds {Formats.Size(bytes)}: 2 sessions.", settings.Sessions.LibrarySizeText);
    }

    /// <summary>Types a search into History and waits for typing's pause and the filtering after it.</summary>
    private static async Task SearchAsync(TabTestHarness h, HistoryViewModel history, string text)
    {
        history.Search = text;
        Assert.True(history.IsFilterPending);
        h.Time.Advance(HistoryViewModel.SearchDelay);
        await TabTestHarness.Eventually(() => !history.IsFilterPending, "the search");
    }

    /// <summary>Waits out the copy that follows a turn, past its settle delay.</summary>
    private static async Task CopyAfterTurnAsync(TabTestHarness h, TabViewModel tab)
    {
        var copy = tab.LibraryCopy;
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return copy.IsCompleted;
        }, "the copy after the turn");
        await copy;
    }

    [Fact]
    public async Task Sync_now_says_so_when_this_machine_no_longer_has_the_transcript()
    {
        await using var h = new TabTestHarness(s => s.Sessions.SyncNewTabs = true);
        var tab = await h.OpenTabAsync();
        // The turn's own copy finds no transcript and gives up quietly.
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.State.Tokens.Total == 120, "the turn to finish");
        await SettleAsync(h);

        await SyncNowAsync(h, tab);

        var note = Assert.Single(Notes(tab), n => n.Text.StartsWith("Couldn't copy", StringComparison.Ordinal));
        Assert.Equal("Couldn't copy this session to the session library: its transcript isn't on this machine.", note.Text);
        Assert.Equal(Conversation.NoteKind.Warning, note.Kind);
        Assert.Empty(h.Services.Library.Library.List());
    }

    /// <summary>Runs <b>Sync now</b> to the end, moving the clock past the settle delay it waits.</summary>
    private static async Task SyncNowAsync(TabTestHarness h, TabViewModel tab)
    {
        var sync = tab.SyncNowCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() =>
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            return sync.IsCompleted;
        }, "Sync now");
        await sync;
    }

    private static Conversation.NoteItem[] Notes(TabViewModel tab) => InlineDispatcher.Read(() => tab.Items.OfType<Conversation.NoteItem>().ToArray());

    /// <summary>
    /// Lets a copy that would follow a turn run its course: past the settle delay, until the library has no copy under
    /// way and every tab has heard how its last one went.
    /// </summary>
    private static Task SettleAsync(TabTestHarness h)
    {
        h.Time.Advance(Deventry.App.Services.LibraryService.SettleDelay);
        return Waiting.UntilAsync(
            () => h.Services.Library.IsIdle && InlineDispatcher.Read(() => h.Shell.AllTabs.All(t => t.LibraryCopy.IsCompleted)),
            "the library copies to finish");
    }

    /// <summary>Another machine has the session open, with a fresh lease.</summary>
    private static Task HoldLeaseAsync(TabTestHarness h, string sessionId, string machine) =>
        File.WriteAllTextAsync(
            Path.Combine(h.Services.Library.Library.GetSessionFolder(sessionId), LeaseManager.FileName),
            new JsonObject { ["machine"] = machine, ["owner"] = "other", ["updatedAt"] = h.Time.GetUtcNow().ToString("O") }.ToJsonString(),
            TestContext.Current.CancellationToken);

    /// <summary>An earlier run of Deventry on this machine had the session open and ended without letting it go, as in a crash.</summary>
    private static Task LeaveLeaseAsync(TabTestHarness h, string sessionId) =>
        File.WriteAllTextAsync(
            Path.Combine(h.Services.Library.Library.GetSessionFolder(sessionId), LeaseManager.FileName),
            new JsonObject
            {
                ["machine"] = h.Services.Library.MachineName,
                ["machineId"] = h.Services.MachineId,
                ["owner"] = "crashed",
                ["updatedAt"] = h.Time.GetUtcNow().ToString("O"),
            }.ToJsonString(),
            TestContext.Current.CancellationToken);

    private static async Task SaveToLibraryAsync(TabTestHarness h, string sessionId, string machine, DateTimeOffset? lastUsed = null, TabOverrides? overrides = null,
        List<ReviewedFile>? reviewedFiles = null)
    {
        var source = Path.Combine(h.Root, $"{sessionId}.jsonl");
        await File.WriteAllLinesAsync(source, [UserLine(sessionId, "Add the export button", h.WorkFolder)]);
        var record = new SessionRecord
        {
            SessionId = sessionId,
            Name = "Export button",
            Machine = machine,
            LastUsed = lastUsed ?? h.Time.GetUtcNow().AddHours(-1),
            Folder = h.WorkFolder,
            Tokens = new TokenTotals(),
            Overrides = overrides,
            ReviewedFiles = reviewedFiles,
        };
        await h.Services.Library.Library.SaveAsync(record, source, subagentsDirectory: null);
    }

    private static string ReplyLine(string sessionId, string text) => new JsonObject
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) },
        ["sessionId"] = sessionId,
        ["timestamp"] = "2026-09-28T10:01:00Z",
    }.ToJsonString();

    private static string UserLine(string sessionId, string text, string cwd, string time = "2026-09-28T10:00:00Z") => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = text },
        ["cwd"] = cwd,
        ["sessionId"] = sessionId,
        ["timestamp"] = time,
    }.ToJsonString();
}
