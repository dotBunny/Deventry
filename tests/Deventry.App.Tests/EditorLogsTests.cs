using System.Globalization;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.ProjectTools;

namespace Deventry.App.Tests;

/// <summary>
/// Editor logs (DESIGN.md §18, "Editor logs"): while a project's editor runs, its log is a row under one of the project's
/// tabs, read as it's written, and it stays after the editor ends. The engine and project are a few files in a temporary
/// folder, the editor a made-up process, and its log a file the test writes.
/// </summary>
public class EditorLogsTests
{
    private sealed record Fixture(TabTestHarness H, FakeLauncher Launcher, FakeSystemProcesses Processes, string UProject, string Logs) : IAsyncDisposable
    {
        public string MainLog => Path.Combine(Logs, "NightOwl.log");

        public ValueTask DisposeAsync() => H.DisposeAsync();

        /// <summary>Lets the clock run long enough for a look at the processes, and the reads after it.</summary>
        public void Look() => H.Time.Advance(TimeSpan.FromSeconds(EditorLogService.ReadsPerCheck));

        /// <summary>Starts an Unreal process with the project on its command line.</summary>
        public SystemProcess StartEditor(int pid, string name = "UnrealEditor", string arguments = "")
        {
            var process = new SystemProcess(pid, name, $"C:\\UE\\Engine\\Binaries\\Win64\\{name}.exe \"{UProject}\" {arguments}", H.Time.GetUtcNow());
            Processes.Start(process);
            return process;
        }

        /// <summary>A new log, as Unreal starts one: the line saying when it opened, then <paramref name="lines"/>.</summary>
        public void WriteLog(string path, params string[] lines)
        {
            Directory.CreateDirectory(Logs);
            var opened = H.Time.GetUtcNow().AddSeconds(1).UtcDateTime.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllText(path, string.Join("\n", [$"Log file open, {opened}", .. lines]) + "\n");
        }

        public static void AppendLog(string path, params string[] lines) => File.AppendAllText(path, string.Join("\n", lines) + "\n");
    }

    private static async Task<(Fixture F, TabViewModel Tab)> OpenAsync(Action<Core.Settings.AppSettings>? configure = null)
    {
        var (h, launcher, uproject) = ProjectToolsTests.UnrealHarness(configure);
        var processes = new FakeSystemProcesses();
        h.Services.ProjectTools.Processes = processes;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        return (new Fixture(h, launcher, processes, uproject, Path.Combine(h.WorkFolder, "Saved", "Logs")), tab);
    }

    private static IReadOnlyList<EditorLogViewModel> Logs(TabViewModel tab) => InlineDispatcher.Read(() => tab.ProjectTools.Logs.ToArray());

    [Fact]
    public async Task A_running_editor_gets_a_row_under_the_tab_with_its_log_which_stays_after_it_closes()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        var h = f.H;
        f.WriteLog(f.MainLog, "LogInit: Display: Starting", "[2026.09.28-12.00.01:000][  0]LogBlueprint: Error: Missing node", "LogTemp: Warning: Slow");
        f.StartEditor(4412);
        Assert.Empty(Logs(tab));

        f.Look();

        var log = Assert.Single(Logs(tab));
        Assert.Equal("NightOwl.log", log.FileName);
        Assert.Equal(f.MainLog, log.LogPath);
        Assert.True(log.IsRunning);
        Assert.Equal("Unreal editor", log.Role);
        Assert.Contains("LogTemp: Warning: Slow", log.Lines());
        Assert.Equal(1, log.Errors);
        Assert.Equal(1, log.Warnings);
        Assert.Equal("Unreal editor · 1 error · 1 warning", log.Detail);
        Assert.Equal($"Unreal editor running since {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)} (PID 4412).", log.Status);
        Assert.Null(log.Timing);
        // Its log can be shown, but appearing doesn't take the main area; the tab has a row under it now.
        Assert.Contains(log, h.Shell.OpenTabs);
        Assert.Same(tab, h.Shell.Selected);
        Assert.True(tab.HasRowsUnder);
        Assert.True(tab.ShowsLogs);
        Assert.Equal("1 log", tab.RowsUnderText);

        // New lines come as they're written, each read once.
        Fixture.AppendLog(f.MainLog, "LogTemp: Display: Loaded");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("LogTemp: Display: Loaded", log.Lines()[^1]);
        Assert.Single(log.Lines(), l => l == "LogTemp: Display: Loaded");

        // Shown, in the main area: its errors are seen.
        Assert.Equal(1, log.UnseenErrors);
        log.OpenCommand.Execute(null);
        Assert.Same(log, h.Shell.Selected);
        Assert.True(log.IsShown);
        Assert.Equal(0, log.UnseenErrors);
        Assert.Same(tab, h.Shell.ProjectTab);

        // The editor closes: the row stays, saying so, with its lines.
        Fixture.AppendLog(f.MainLog, "Log file closed, 09/28/26 12:00:09");
        f.Processes.End(4412);
        f.Look();
        Assert.False(log.IsRunning);
        Assert.Same(log, Assert.Single(Logs(tab)));
        Assert.Equal("Log file closed, 09/28/26 12:00:09", log.Lines()[^1]);
        Assert.Equal($"Closed · {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)} · 1 error · 1 warning", log.Detail);
        Assert.Equal($"Unreal editor closed at {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)}.", log.Status);
        Assert.NotNull(log.Timing);
    }

    [Fact]
    public async Task An_editor_starting_again_brings_the_same_row_back_with_its_new_log()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, "LogTemp: Error: First session");
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        f.Processes.End(4412);
        f.Look();
        Assert.False(log.IsRunning);

        // As Unreal starts again: the old log becomes a backup, and a new one begins.
        File.Move(f.MainLog, Path.Combine(f.Logs, "NightOwl-backup-2026.09.28-12.00.05.log"));
        f.H.Time.Advance(TimeSpan.FromMinutes(5));
        f.StartEditor(5120);
        f.WriteLog(f.MainLog, "LogInit: Display: Second session");
        f.Look();

        Assert.Same(log, Assert.Single(Logs(tab)));
        Assert.True(log.IsRunning);
        Assert.Contains("LogInit: Display: Second session", log.Lines());
        Assert.DoesNotContain("LogTemp: Error: First session", log.Lines());
        Assert.Equal(0, log.Errors);
        Assert.Contains("(PID 5120)", log.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_the_row_while_the_editor_runs_keeps_it_away_until_the_editor_starts_again()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, "LogInit: Display: Starting");
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.OpenCommand.Execute(null);

        log.CloseCommand.Execute(null);

        Assert.Empty(Logs(tab));
        Assert.DoesNotContain(log, f.H.Shell.OpenTabs);
        // Its tab takes its place.
        Assert.Same(tab, f.H.Shell.Selected);
        Fixture.AppendLog(f.MainLog, "LogTemp: Display: More");
        f.Look();
        Assert.Empty(Logs(tab));

        // The editor ends and starts again: the row is back, with the new log.
        f.Processes.End(4412);
        f.Look();
        f.StartEditor(5120);
        File.Delete(f.MainLog);
        f.WriteLog(f.MainLog, "LogInit: Display: Again");
        f.Look();
        var again = Assert.Single(Logs(tab));
        Assert.Contains("LogInit: Display: Again", again.Lines());

        // Closed once the editor has ended, it's gone; the next start is a log of its own.
        f.Processes.End(5120);
        f.Look();
        again.CloseCommand.Execute(null);
        Assert.Empty(Logs(tab));
        f.StartEditor(6000);
        File.Delete(f.MainLog);
        f.WriteLog(f.MainLog, "LogInit: Display: Third");
        f.Look();
        Assert.NotSame(again, Assert.Single(Logs(tab)));
    }

    [Fact]
    public async Task Ctrl_W_on_an_editors_log_closes_it()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.OpenCommand.Execute(null);

        await f.H.Shell.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Empty(Logs(tab));
        Assert.Same(tab, f.H.Shell.Selected);
        Assert.Contains(tab, f.H.Shell.AllTabs);
    }

    [Fact]
    public async Task A_commandlet_beside_the_editor_gets_a_row_for_the_log_it_writes()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog);
        f.StartEditor(4412);
        f.Look();
        f.H.Time.Advance(TimeSpan.FromMinutes(1));

        f.StartEditor(4500, "UnrealEditor-Cmd", "-run=ResavePackages");
        f.WriteLog(Path.Combine(f.Logs, "NightOwl_2.log"), "LogResave: Display: Resaving");
        f.Look();

        Assert.Equal(["NightOwl.log", "NightOwl_2.log"], Logs(tab).Select(l => l.FileName));
        Assert.Equal("Unreal commandlet ResavePackages", Logs(tab)[1].Role);
        Assert.Equal("2 logs", tab.RowsUnderText);
    }

    /// <summary>
    /// Another tab of the project in its group, at <paramref name="index"/>: made as a restored one is, so it starts no
    /// session until it's selected, and its project found.
    /// </summary>
    private static async Task<TabViewModel> AddTabAsync(Fixture f, int index)
    {
        var h = f.H;
        var tab = InlineDispatcher.Read(() =>
        {
            var other = new TabViewModel(h.Services, h.Shell, new Core.Settings.TabState { Folder = h.WorkFolder }, isRestored: true);
            h.Shell.Groups.Single().Tabs.Insert(index, other);
            h.Shell.OpenTabs.Add(other);
            return other;
        });
        await tab.ProjectTools.RefreshAsync();
        return tab;
    }

    [Fact]
    public async Task The_log_goes_under_the_tab_that_launched_the_editor_and_closes_with_it()
    {
        var (f, first) = await OpenAsync();
        await using var _f = f;
        var h = f.H;
        var second = await AddTabAsync(f, 1);

        await second.ProjectTools.RunActionCommand.ExecuteAsync(InlineDispatcher.Read(() => second.ProjectTools.Actions.Single(a => a.Id == "launch-editor")));
        Assert.NotEmpty(f.Launcher.Started);
        f.WriteLog(f.MainLog, "LogInit: Display: Starting");
        f.StartEditor(4412);
        f.Look();

        Assert.Empty(Logs(first));
        var log = Assert.Single(Logs(second));
        log.OpenCommand.Execute(null);

        await h.Shell.CloseTabCommand.ExecuteAsync(second);

        // It doesn't go to the project's other tab, which takes its place on screen.
        Assert.Empty(Logs(first));
        Assert.DoesNotContain(log, h.Shell.OpenTabs);
        Assert.Same(first, h.Shell.Selected);
        // It stays away while the editor runs, as one ×'d away does.
        Fixture.AppendLog(f.MainLog, "LogTemp: Display: More");
        f.Look();
        Assert.Empty(Logs(first));

        // The editor starts again: its new log is under the project's tab.
        f.Processes.End(4412);
        f.Look();
        f.StartEditor(5120);
        File.Delete(f.MainLog);
        f.WriteLog(f.MainLog, "LogInit: Display: Again");
        f.Look();
        var again = Assert.Single(Logs(first));
        Assert.Contains("LogInit: Display: Again", again.Lines());

        // Ended, it closes with its tab all the same.
        f.Processes.End(5120);
        f.Look();
        var third = await AddTabAsync(f, 1);
        Assert.Same(again, Assert.Single(Logs(first)));
        await h.Shell.CloseTabCommand.ExecuteAsync(first);
        Assert.Empty(Logs(third));
        Assert.DoesNotContain(again, h.Shell.OpenTabs);
    }

    [Fact]
    public async Task A_commandlet_a_tab_started_goes_under_that_tab_and_one_nobody_started_under_the_first()
    {
        var (f, started) = await OpenAsync();
        await using var _f = f;
        var h = f.H;
        // A tab of the project above it in the sidebar, which would have the log otherwise.
        var above = await AddTabAsync(f, 0);
        // Claude ran it in the tab that opened first, so it's among that tab's processes.
        h.Trees.Trees[h.Transport.ProcessId!.Value].Children.Add((4500, "UnrealEditor-Cmd"));

        f.StartEditor(4500, "UnrealEditor-Cmd", "-run=Cook");
        f.WriteLog(f.MainLog);
        f.Look();

        Assert.Empty(Logs(above));
        Assert.Single(Logs(started));

        f.StartEditor(4600, "UnrealEditor", "-game");
        f.WriteLog(Path.Combine(f.Logs, "NightOwl_2.log"));
        f.Look();
        Assert.Equal("NightOwl_2.log", Assert.Single(Logs(above)).FileName);
    }

    [Fact]
    public async Task Collapsed_the_tab_folds_its_logs_away_and_its_badge_shows_errors_not_seen_and_a_crash()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, "LogInit: Display: Starting");
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        Assert.False(log.IsError);

        tab.ToggleRowsCollapsedCommand.Execute(null);
        Assert.True(tab.AreRowsCollapsed);
        Assert.False(tab.ShowsLogs);
        Assert.Equal(1, tab.CollapsedRowCount);
        Assert.False(tab.CollapsedIsError);

        Fixture.AppendLog(f.MainLog, "LogBlueprint: Error: Missing node");
        f.H.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(tab.CollapsedIsError);
        Assert.Contains("NightOwl.log · Unreal editor · 1 error", tab.CollapsedTip, StringComparison.Ordinal);

        // Looked at, the errors are seen; the row comes out from under the fold.
        log.OpenCommand.Execute(null);
        Assert.False(tab.AreRowsCollapsed);
        Assert.False(log.IsError);

        Fixture.AppendLog(f.MainLog, "LogWindows: Error: === Critical error: ===");
        f.H.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(log.IsCrashed);
        Assert.True(log.IsError);
        Assert.StartsWith("Crashed", log.Detail, StringComparison.Ordinal);
        Assert.StartsWith("Unreal editor crashed", log.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// Settings → Project tools → Show editor logs under the tab, changed on the UI thread as the Settings window does:
    /// saving follows the logs again there, and so does the project's refresh it starts, which would otherwise race it for
    /// the same rows.
    /// </summary>
    private static void ShowEditorLogs(Fixture f, bool show) => InlineDispatcher.Run(() =>
    {
        f.H.Services.Settings.ProjectTools.ShowEditorLogs = show;
        f.H.Services.SaveSettings();
    });

    [Fact]
    public async Task With_the_setting_off_there_are_no_logs_and_turning_it_off_takes_them_away()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.OpenCommand.Execute(null);

        ShowEditorLogs(f, false);

        Assert.Empty(Logs(tab));
        Assert.DoesNotContain(log, f.H.Shell.OpenTabs);
        Assert.Same(tab, f.H.Shell.Selected);
        f.Look();
        Assert.Empty(Logs(tab));

        ShowEditorLogs(f, true);
        f.Look();
        Assert.Single(Logs(tab));
    }

    // ---- Filtering it (DESIGN.md §18, "Editor logs", "Filtering it") ----------------------------------------------------

    private static readonly string[] UnrealLines =
    [
        "[2026.09.28-12.00.02:000][  0]LogInit: Display: Starting",
        "[2026.09.28-12.00.02:001][  0]LogBlueprint: Error: Missing node",
        "[2026.09.28-12.00.02:002][  0]LogNet: Verbose: Tick",
        "[2026.09.28-12.00.02:003][  0]LogNet: Warning: Lag",
        "[2026.09.28-12.00.02:004][  1]LogTemp: Loaded the map",
    ];

    [Fact]
    public async Task The_log_has_a_chip_for_each_level_and_its_categories_with_their_counts()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, UnrealLines);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));

        Assert.Equal(["Errors", "Warnings", "Display", "Log", "Verbose"], log.Levels.Select(l => l.Name));
        // "Log file open" is a Log line too.
        Assert.Equal([1, 1, 1, 2, 1], log.Levels.Select(l => l.Count));
        Assert.All(log.Levels, l => Assert.True(l.IsShown));
        Assert.True(log.HasCategories);
        Assert.Equal(["LogBlueprint", "LogInit", "LogNet", "LogTemp"], log.Categories.Select(c => c.Name));
        Assert.Equal(2, log.Categories.Single(c => c.Name == "LogNet").Count);
        Assert.Equal("Categories", log.CategoriesLabel);
        Assert.Equal("LogBlueprint".Length, log.CategoryColumn);
        Assert.True(log.HasTimeColumn);
        Assert.Equal("12:00:02.001", log.Output[2].Columns!.Time);
        Assert.False(log.IsFiltered);
        Assert.Null(log.ShownNote);
        Assert.True(log.StayCurrent);

        // A category that comes later takes its place by name, and counts go on.
        Fixture.AppendLog(f.MainLog, "[2026.09.28-12.00.03:000][  2]LogAudio: Warning: No device");
        f.H.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["LogAudio", "LogBlueprint", "LogInit", "LogNet", "LogTemp"], log.Categories.Select(c => c.Name));
        Assert.Equal(2, log.Levels[1].Count);
    }

    [Fact]
    public async Task Hiding_a_level_a_category_or_lines_without_some_text_hides_their_lines()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, UnrealLines);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        var changes = 0;
        log.FilterChanged += () => changes++;
        IEnumerable<string> Shown() => log.Output.Where(log.Shows).Select(l => l.Text[(l.Columns?.MessageStart ?? 0)..]);

        log.Levels.Single(l => l.Name == "Verbose").IsShown = false;
        Assert.Equal(1, changes);
        Assert.DoesNotContain("Tick", Shown());
        Assert.True(log.IsFiltered);
        Assert.Equal("Showing 5 of 6 lines", log.ShownNote);
        Assert.Equal("Hiding Verbose lines: click to show them", log.Levels[^1].Tip);

        log.Categories.Single(c => c.Name == "LogNet").IsShown = false;
        Assert.Equal(2, changes);
        Assert.DoesNotContain("Lag", Shown());
        Assert.Equal("Categories (3 of 4)", log.CategoriesLabel);

        log.TextFilter = "MAP";
        Assert.Equal(3, changes);
        Assert.Equal(["Loaded the map"], Shown());
        Assert.Equal("Showing 1 of 6 lines", log.ShownNote);

        // Copy takes the lines shown, as the file has them.
        await log.CopyOutputCommand.ExecuteAsync(null);
        Assert.Equal("[2026.09.28-12.00.02:004][  1]LogTemp: Loaded the map", f.H.Platform.Clipboard);

        log.ClearTextFilterCommand.Execute(null);
        Assert.Equal("", log.TextFilter);
        Assert.Equal(4, Shown().Count());

        // The menu's search, and Show all and Hide all for what it finds.
        log.CategorySearch = "log";
        Assert.Equal(4, log.FoundCategories.Count);
        log.CategorySearch = "BLUE";
        Assert.Equal(["LogBlueprint"], log.FoundCategories.Select(c => c.Name));
        log.HideAllCategoriesCommand.Execute(null);
        Assert.Equal(5, changes);
        Assert.Equal("Categories (2 of 4)", log.CategoriesLabel);
        log.CategorySearch = "nothing like it";
        Assert.True(log.NoCategoryFound);
        log.CategorySearch = "";
        log.ShowAllCategoriesCommand.Execute(null);
        Assert.All(log.Categories, c => Assert.True(c.IsShown));
        log.Levels[^1].IsShown = true;
        Assert.False(log.IsFiltered);
    }

    [Fact]
    public async Task The_levels_and_categories_hidden_are_the_projects_and_its_next_log_starts_with_them()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, UnrealLines);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        var state = f.H.Services.State.ProjectTools;

        log.Levels.Single(l => l.Name == "Display").IsShown = false;
        log.Levels.Single(l => l.Name == "Verbose").IsShown = false;
        log.Categories.Single(c => c.Name == "LogNet").IsShown = false;
        log.TextFilter = "only this log's";
        Assert.Equal("Display,Verbose", state.Get(log.ProjectPath, EditorLogViewModel.HiddenLevelsKey));
        Assert.Equal("LogNet", state.Get(log.ProjectPath, EditorLogViewModel.HiddenCategoriesKey));

        // A commandlet's log, beside the editor's: the project's levels and categories, but not the text.
        f.H.Time.Advance(TimeSpan.FromMinutes(1));
        f.StartEditor(4500, "UnrealEditor-Cmd", "-run=ResavePackages");
        f.WriteLog(Path.Combine(f.Logs, "NightOwl_2.log"), "[2026.09.28-12.01.02:000][  0]LogNet: Display: Resaving", "[2026.09.28-12.01.02:001][  0]LogTemp: Warning: Slow");
        f.Look();
        var commandlet = Logs(tab)[1];
        Assert.Equal([true, true, false, true, false], commandlet.Levels.Select(l => l.IsShown));
        Assert.False(commandlet.Categories.Single(c => c.Name == "LogNet").IsShown);
        Assert.Equal("", commandlet.TextFilter);
        Assert.Equal(["[2026.09.28-12.01.02:001][  0]LogTemp: Warning: Slow"], commandlet.Output.Where(commandlet.Shows).Select(l => l.Text).Skip(1));

        // Showing them all again forgets them.
        foreach (var level in log.Levels)
        {
            level.IsShown = true;
        }
        log.ShowAllCategoriesCommand.Execute(null);
        Assert.Null(state.Get(log.ProjectPath, EditorLogViewModel.HiddenLevelsKey));
        Assert.Null(state.Get(log.ProjectPath, EditorLogViewModel.HiddenCategoriesKey));
    }

    [Fact]
    public async Task A_new_log_in_its_place_counts_afresh_and_keeps_its_filter()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, UnrealLines);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        log.Categories.Single(c => c.Name == "LogNet").IsShown = false;
        f.Processes.End(4412);
        f.Look();

        File.Move(f.MainLog, Path.Combine(f.Logs, "NightOwl-backup-2026.09.28-12.00.05.log"));
        f.H.Time.Advance(TimeSpan.FromMinutes(5));
        f.StartEditor(5120);
        f.WriteLog(f.MainLog, "[2026.09.28-12.05.02:000][  0]LogNet: Warning: Again", "[2026.09.28-12.05.02:001][  0]LogInit: Display: Second session");
        f.Look();

        Assert.Equal([0, 1, 1, 1, 0], log.Levels.Select(l => l.Count));
        Assert.Equal(["LogInit", "LogNet"], log.Categories.Select(c => c.Name));
        Assert.False(log.Categories.Single(c => c.Name == "LogNet").IsShown);
        Assert.DoesNotContain(log.Output, l => l.Text.EndsWith("Again", StringComparison.Ordinal) && log.Shows(l));
    }

    [Fact]
    public async Task Each_log_wraps_its_lines_as_the_last_one_was_left()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.WriteLog(f.MainLog, UnrealLines);
        f.StartEditor(4412);
        f.Look();
        var log = Assert.Single(Logs(tab));
        Assert.True(log.WordWrap);

        log.WordWrap = false;
        Assert.False(f.H.Services.State.LogWordWrap);

        f.H.Time.Advance(TimeSpan.FromMinutes(1));
        f.StartEditor(4500, "UnrealEditor-Cmd", "-run=ResavePackages");
        f.WriteLog(Path.Combine(f.Logs, "NightOwl_2.log"), "LogResave: Display: Resaving");
        f.Look();
        Assert.False(Logs(tab)[1].WordWrap);
    }

    // ---- Unity's log moving to the project's (DESIGN.md §18, "Editor logs", "A log that moves") ------------------------

    private const string MovedSentence = "Logs moved to project-relative Editor.log file, and subsequent logs will only be written to this file.";

    /// <summary>A tab on a Unity project, with Unity's machine-wide log in a temporary folder.</summary>
    private sealed record UnityFixture(TabTestHarness H, FakeSystemProcesses Processes, TabViewModel Tab, string GlobalLog) : IAsyncDisposable
    {
        public string Root => H.WorkFolder;

        /// <summary>The project's own log, which Unity 6.5 moves to once it has the project open.</summary>
        public string ProjectLog => Path.Combine(Root, "Logs", "Editor.log");

        /// <summary>The project's log as Unity writes it in the machine's: with forward slashes, even on Windows.</summary>
        public string ProjectLogAsUnityWritesIt => ProjectLog.Replace('\\', '/');

        public ValueTask DisposeAsync() => H.DisposeAsync();

        public void Look() => H.Time.Advance(TimeSpan.FromSeconds(EditorLogService.ReadsPerCheck));

        public void Read() => H.Time.Advance(TimeSpan.FromSeconds(1));

        public SystemProcess StartEditor(int pid, string arguments = "")
        {
            var process = new SystemProcess(pid, "Unity", $"\"C:\\Unity\\6000.5.0f1\\Editor\\Unity.exe\" -projectPath \"{Root}\" {arguments}", H.Time.GetUtcNow());
            Processes.Start(process);
            return process;
        }

        /// <summary>Writes the project's own log, as written <paramref name="since"/> the editor started (or before it, when negative).</summary>
        public void WriteProjectLog(TimeSpan since, params string[] lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProjectLog)!);
            File.WriteAllText(ProjectLog, string.Join("\n", lines) + "\n");
            File.SetLastWriteTimeUtc(ProjectLog, H.Time.GetUtcNow().UtcDateTime + since);
        }

        public void WriteGlobalLog(params string[] lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GlobalLog)!);
            File.WriteAllText(GlobalLog, string.Join("\n", lines) + "\n");
        }
    }

    private static async Task<UnityFixture> OpenUnityAsync()
    {
        var h = new TabTestHarness();
        var os = ToolOSExtensions.Current == ToolOS.Windows ? ToolOS.Windows : ToolOS.Linux;
        h.Services.ProjectTools.OS = os;
        h.Services.ProjectTools.Paths = new ProjectToolPaths(Path.Combine(h.Root, "home"), LocalAppData: Path.Combine(h.Root, "local"));
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "ProjectSettings"));
        File.WriteAllText(Path.Combine(h.WorkFolder, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.5.0f1\n");
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "Assets"));
        var processes = new FakeSystemProcesses();
        h.Services.ProjectTools.Processes = processes;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is { Logs: not null }, "the project");
        return new UnityFixture(h, processes, tab, Core.ProjectTools.Unity.UnityEditors.EditorLog(h.Services.ProjectTools.Paths, os)!);
    }

    [Fact]
    public async Task Unitys_log_moving_to_the_projects_gives_it_a_row_read_from_its_start_and_the_old_one_stops_saying_where()
    {
        await using var f = await OpenUnityAsync();
        var h = f.H;
        f.WriteGlobalLog("Unity Editor version: 6000.5.0f1", "Loading project");
        f.StartEditor(4412);
        f.Look();
        var global = Assert.Single(Logs(f.Tab));
        Assert.Equal(f.GlobalLog, global.LogPath);
        Assert.Equal("Unity editor", global.Detail);

        // It has the project open: the machine's log says where its log went, before the file is there, and another
        // editor's startup comes after it.
        Fixture.AppendLog(f.GlobalLog, MovedSentence, f.ProjectLogAsUnityWritesIt, "Another editor: Loading project");
        f.Read();

        Assert.Equal(2, Logs(f.Tab).Count);
        var own = Logs(f.Tab)[1];
        Assert.Equal(f.ProjectLog, own.LogPath);
        Assert.Equal("Editor.log", own.FileName);
        Assert.True(own.IsRunning);
        Assert.Equal("Unity editor", own.Role);
        Assert.Contains("(PID 4412)", own.Status, StringComparison.Ordinal);
        Assert.Contains(own, h.Shell.OpenTabs);
        var moved = Path.Combine("Logs", "Editor.log");
        Assert.False(global.IsRunning);
        Assert.True(global.IsMoved);
        Assert.Equal($"Moved to {moved}", global.Detail);
        Assert.Equal($"Unity editor moved its log to {moved} at {MessageTimes.Short(h.Time.GetUtcNow(), h.Time)}.", global.Status);
        Assert.Equal(f.ProjectLogAsUnityWritesIt, global.Lines()[^1]);
        Assert.Equal("2 logs", f.Tab.RowsUnderText);

        // Unity copies what it wrote as it started into the project's log, and carries on there: read from its start.
        f.WriteProjectLog(TimeSpan.FromMinutes(1), "Unity Editor version: 6000.5.0f1", "Loading project", "Assets/Player.cs(3,7): warning CS0168: The variable 'e' is declared but never used");
        f.Read();
        Assert.Equal(["Unity Editor version: 6000.5.0f1", "Loading project", "Assets/Player.cs(3,7): warning CS0168: The variable 'e' is declared but never used"], own.Lines());
        Assert.Equal(1, own.Warnings);

        // The machine's log goes on with other editors' lines, which aren't this project's.
        Fixture.AppendLog(f.GlobalLog, "Another editor: Refreshing");
        f.Look();
        Assert.Equal([global, own], Logs(f.Tab));
        Assert.DoesNotContain("Another editor: Refreshing", global.Lines());
        Assert.DoesNotContain("Another editor: Loading project", global.Lines());
        Assert.True(global.IsMoved);
        Assert.True(own.IsRunning);

        // The editor closes: the old row goes, its tab taking its place on screen, and the project's log stays, closed.
        global.OpenCommand.Execute(null);
        Assert.Same(global, h.Shell.Selected);
        f.Processes.End(4412);
        f.Look();
        Assert.Same(own, Assert.Single(Logs(f.Tab)));
        Assert.False(own.IsRunning);
        Assert.StartsWith("Closed", own.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(global, h.Shell.OpenTabs);
        Assert.Same(f.Tab, h.Shell.Selected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unitys_line_saying_the_log_moved_can_have_its_file_after_it_or_none(bool sameLine)
    {
        await using var f = await OpenUnityAsync();
        f.WriteGlobalLog("Loading project");
        f.StartEditor(4412);
        f.Look();
        var global = Assert.Single(Logs(f.Tab));

        if (sameLine)
        {
            Fixture.AppendLog(f.GlobalLog, $"{MovedSentence} {f.ProjectLogAsUnityWritesIt}", "Another editor: Loading project");
        }
        else
        {
            // Without a file, it's the project's own.
            Fixture.AppendLog(f.GlobalLog, MovedSentence, "Another editor: Loading project");
        }
        f.Read();

        Assert.Equal([global.LogPath, f.ProjectLog], Logs(f.Tab).Select(l => l.LogPath));
        Assert.True(global.IsMoved);
        Assert.DoesNotContain("Another editor: Loading project", global.Lines());
        Assert.EndsWith(sameLine ? f.ProjectLogAsUnityWritesIt : MovedSentence, global.Lines()[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_projects_line_saying_its_log_moved_leaves_this_projects_log_as_it_is()
    {
        await using var f = await OpenUnityAsync();
        f.WriteGlobalLog("Loading project");
        f.StartEditor(4412);
        f.Look();
        var global = Assert.Single(Logs(f.Tab));

        var other = Path.Combine(f.H.Root, "Lark", "Logs", "Editor.log").Replace('\\', '/');
        Fixture.AppendLog(f.GlobalLog, MovedSentence, other, "Still this editor");
        f.Read();
        f.Look();

        Assert.Same(global, Assert.Single(Logs(f.Tab)));
        Assert.True(global.IsRunning);
        Assert.False(global.IsMoved);
        Assert.Equal([MovedSentence, other, "Still this editor"], global.Lines().TakeLast(3));
    }

    [Fact]
    public async Task A_unity_editor_with_useGlobalLog_keeps_the_machines_log()
    {
        await using var f = await OpenUnityAsync();
        f.WriteGlobalLog("Loading project");
        f.StartEditor(4412, "-useGlobalLog");
        f.Look();
        var global = Assert.Single(Logs(f.Tab));

        // Even with the project's log being written, by another session say, and a line naming it.
        f.WriteProjectLog(TimeSpan.FromMinutes(1), "Another session");
        Fixture.AppendLog(f.GlobalLog, MovedSentence, f.ProjectLogAsUnityWritesIt, "Still this editor");
        f.Read();
        f.Look();

        Assert.Same(global, Assert.Single(Logs(f.Tab)));
        Assert.Equal(f.GlobalLog, global.LogPath);
        Assert.True(global.IsRunning);
        Assert.False(global.IsMoved);
        Assert.Equal("Still this editor", global.Lines()[^1]);
    }

    [Fact]
    public async Task A_unity_editor_already_writing_the_projects_log_has_that_row_alone()
    {
        await using var f = await OpenUnityAsync();
        // Deventry starts after the move, as when it's opened with the editor open.
        f.WriteGlobalLog("Unity Editor version: 6000.5.0f1", MovedSentence, f.ProjectLogAsUnityWritesIt);
        f.StartEditor(4412);
        f.WriteProjectLog(TimeSpan.FromMinutes(1), "Unity Editor version: 6000.5.0f1", "Loading project", "Refreshing");

        f.Look();

        var own = Assert.Single(Logs(f.Tab));
        Assert.Equal(f.ProjectLog, own.LogPath);
        Assert.True(own.IsRunning);
        Assert.Equal(["Unity Editor version: 6000.5.0f1", "Loading project", "Refreshing"], own.Lines());
    }

    [Fact]
    public async Task An_earlier_sessions_project_log_isnt_followed_and_one_written_at_a_later_look_moves_the_log_there()
    {
        await using var f = await OpenUnityAsync();
        f.WriteGlobalLog("Loading project");
        f.WriteProjectLog(TimeSpan.FromHours(-1), "An earlier session");
        f.StartEditor(4412);
        f.Look();
        var global = Assert.Single(Logs(f.Tab));
        Assert.Equal(f.GlobalLog, global.LogPath);

        // The project's log is being written before its line is read: the same move.
        f.WriteProjectLog(TimeSpan.FromMinutes(1), "Loading project", "Refreshing");
        f.Look();

        Assert.Equal([f.GlobalLog, f.ProjectLog], Logs(f.Tab).Select(l => l.LogPath));
        Assert.True(global.IsMoved);
        var own = Logs(f.Tab)[1];
        Assert.True(own.IsRunning);
        Assert.Equal(["Loading project", "Refreshing"], own.Lines());

        f.Processes.End(4412);
        f.Look();
        Assert.Same(own, Assert.Single(Logs(f.Tab)));
    }

    [Fact]
    public async Task Another_projects_editor_gets_no_row()
    {
        var (f, tab) = await OpenAsync();
        await using var _f = f;
        f.Processes.Start(new SystemProcess(4412, "UnrealEditor", "UnrealEditor.exe D:/Other/Other.uproject", f.H.Time.GetUtcNow()));
        f.WriteLog(f.MainLog);

        f.Look();

        Assert.Empty(Logs(tab));
        Assert.False(tab.HasRowsUnder);
    }
}
