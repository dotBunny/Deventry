using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Processes;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// Settings → Source control (DESIGN.md §14): Git, then Perforce; the Git page's settings, which keep the keys they had
/// on Conversation and General; the git Deventry runs; and the Project Files tab's two choices (§18, "Project files").
/// </summary>
public class GitSettingsTests
{
    [Fact]
    public async Task Source_control_has_Git_then_Perforce_and_its_words_find_both()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);

        Assert.Equal([SettingsCategory.Git, SettingsCategory.Perforce], settings.Categories.SkipWhile(c => c != SettingsCategory.Git).Take(2));
        Assert.Equal("IconBranch", settings.Git.IconKey);
        Assert.False(string.IsNullOrEmpty(settings.Git.Description));
        Assert.Equal(["tabRows", "worktrees", "git", "projectFiles"], settings.Git.Groups.Select(g => g.Key));

        foreach (var words in new[] { "source control", "version control", "vcs" })
        {
            settings.SearchText = words;
            Assert.Equal([SettingsCategory.Git, SettingsCategory.Perforce], settings.SearchResults.Select(r => r.Category).Distinct());
        }
        foreach (var words in new[] { "git", "branch", "worktree" })
        {
            settings.SearchText = words;
            Assert.Equal(SettingsCategory.Git, settings.SearchResults[0].Category);
        }
        settings.SearchText = "branch";
        Assert.Equal("Show the git branch", settings.SearchResults[0].Label);
        settings.SearchText = "git";
        Assert.Same(settings.Git, settings.CurrentPage);
        // Perforce is found as before (and About's trademark row with it).
        foreach (var words in new[] { "perforce", "p4" })
        {
            settings.SearchText = words;
            Assert.Contains(settings.SearchResults, r => r.Category == SettingsCategory.Perforce);
            Assert.DoesNotContain(settings.SearchResults, r => r.Category == SettingsCategory.Git);
        }
    }

    [Fact]
    public async Task The_branch_switch_and_worktrees_moved_to_Git_keeping_their_settings()
    {
        await using var h = new TabTestHarness(s =>
        {
            s.Appearance.ShowBranchOnTabs = false;
            s.General.RemoveMergedWorktrees = true;
            s.General.RemoveInactiveWorktreesAfterDays = 30;
        });
        using var settings = new SettingsViewModel(h.Services, null);

        Assert.Null(settings.Conversation.FindSetting("appearance.showBranchOnTabs"));
        Assert.NotNull(settings.Conversation.FindGroup("tabRows"));
        Assert.Null(settings.General.FindGroup("worktrees"));
        Assert.Null(settings.General.FindSetting("general.removeMergedWorktrees"));

        var git = settings.Git;
        Assert.False(git.ShowBranchOnTabs);
        Assert.True(git.FindSetting("appearance.showBranchOnTabs")!.IsChanged);
        Assert.True(git.RemoveMergedWorktrees);
        Assert.Equal(30, git.RemoveInactiveWorktreesAfter.Days);
        Assert.True(git.CanCleanUpWorktrees);

        git.ShowBranchOnTabs = true;
        Assert.True(h.Services.Settings.Appearance.ShowBranchOnTabs);
        git.FindGroup("worktrees")!.ResetCommand.Execute(null);
        Assert.False(h.Services.Settings.General.RemoveMergedWorktrees);
        Assert.Equal(0, h.Services.Settings.General.RemoveInactiveWorktreesAfterDays);
        Assert.False(git.CanCleanUpWorktrees);
    }

    /// <summary>
    /// What syncs, and what's this computer's (DESIGN.md §14, "This computer"): the branch switch syncs as it did on
    /// Conversation; every other section of the page is this computer's, which its heading says, not the page's title.
    /// </summary>
    [Fact]
    public async Task Git_marks_this_computer_by_its_sections_and_the_branch_switch_still_syncs()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);
        var git = settings.Git;

        Assert.False(git.IsMachineOnly);
        Assert.False(git.FindGroup("tabRows")!.ShowsMachineMark);
        Assert.False(git.FindSetting("appearance.showBranchOnTabs")!.IsMachineOnly);
        Assert.All(["worktrees", "git", "projectFiles"], key => Assert.True(git.FindGroup(key)!.ShowsMachineMark, key));
        Assert.All(["git.path", "git.newFilesTabsShow"], key => Assert.True(git.FindSetting(key)!.IsMachineOnly, key));
        Assert.All(git.Items.Where(i => i.Key.StartsWith("git.", StringComparison.Ordinal)), i => Assert.False(i.ShowsMachineMark));
        // Ignored files moved to Files and diffs, beside Hidden files, keeping its key; both this computer's.
        Assert.Null(git.FindSetting("git.ignoredFiles"));
        Assert.All(["git.ignoredFiles", "editor.hiddenFiles"], key => Assert.True(settings.Files.FindSetting(key)!.IsMachineOnly, key));
    }

    [Fact]
    public async Task Path_to_git_says_which_git_runs_and_what_is_wrong_with_one_that_cant()
    {
        var launcher = new FakeLauncher();
        launcher.OnStart = (spec, process) =>
        {
            process.WriteOutput(spec.FileName.EndsWith("notgit", StringComparison.Ordinal) ? "Usage: notgit [options]" : "git version 2.50.1.windows.1");
            process.Exit(0);
        };
        await using var h = new TabTestHarness(launcher: launcher);
        var git = Directory.CreateDirectory(Path.Combine(h.Root, "tools")).FullName;
        var gitPath = Path.Combine(git, "git");
        File.WriteAllText(gitPath, "");
        File.WriteAllText(Path.Combine(git, "notgit"), "");
        using var settings = new SettingsViewModel(h.Services, null);
        var page = settings.Git;

        // Set: the next git Deventry runs is that one.
        page.GitPath = $"  {gitPath} ";
        await page.Checking;
        Assert.Equal(gitPath, h.Services.Settings.Git.Path);
        Assert.Equal($"git 2.50.1.windows.1 at {gitPath}", page.GitFoundText);
        Assert.Null(page.GitProblem);
        Assert.Equal(gitPath, launcher.Started.Last(s => s.Arguments is ["--version"]).FileName);
        Assert.True(page.FindSetting("git.path")!.IsChanged);

        // Nothing there, a folder, or a program that isn't git: the row says so.
        page.GitPath = Path.Combine(git, "missing");
        await page.Checking;
        Assert.Equal("There's no file at that path.", page.GitProblem);
        Assert.Null(page.GitFoundText);
        page.GitPath = git;
        await page.Checking;
        Assert.Equal("That's a folder. Choose the git program in it.", page.GitProblem);
        page.GitPath = Path.Combine(git, "notgit");
        await page.Checking;
        Assert.Equal("It isn't git: it didn't answer as git does.", page.GitProblem);

        // Browse… picks the program; empty goes back to the PATH's.
        h.Platform.FileToPick = gitPath;
        await page.BrowseGitPathCommand.ExecuteAsync(null);
        await page.Checking;
        Assert.Equal(gitPath, h.Services.Settings.Git.Path);
        Assert.Null(page.GitProblem);
        page.GitPath = "";
        Assert.Null(h.Services.Settings.Git.Path);
        Assert.False(page.FindSetting("git.path")!.IsChanged);
        await page.Checking;
    }

    /// <summary>Every git Deventry runs is the one Path to git names, from the next run on (DESIGN.md §14).</summary>
    [Fact]
    public async Task Every_git_Deventry_runs_uses_the_path_set()
    {
        var launcher = new FakeLauncher { OnStart = (_, process) => process.Exit(0) };
        await using var h = new TabTestHarness(launcher: launcher);
        var path = Path.Combine(h.Root, "tools", "git");
        List<ProcessStartSpec> Gits() => [.. launcher.Started.Where(s => s.Arguments is ["-c", "core.quotepath=false", ..])];

        await h.Services.Git.HasUncommittedChangesAsync(h.WorkFolder, TestContext.Current.CancellationToken);
        Assert.Equal("git", Assert.Single(Gits()).FileName);

        h.Services.Settings.Git.Path = $"{path} ";
        await h.Services.Git.HasUncommittedChangesAsync(h.WorkFolder, TestContext.Current.CancellationToken);
        await h.Services.Git.GetRepositoryRootAsync(h.WorkFolder, TestContext.Current.CancellationToken);
        await h.Services.Git.CheckIgnoredAsync(h.WorkFolder, ["a.txt"], TestContext.Current.CancellationToken);
        Assert.Equal([path, path, path], Gits().Skip(1).Select(s => s.FileName));
        Assert.All(Gits(), s => Assert.Equal(h.WorkFolder, s.WorkingDirectory));
    }

    // ---- The Project Files tab (DESIGN.md §18, "Project files") ------------------------------------------------------

    private static async Task<ProjectFilesViewModel> OpenFilesAsync(TabTestHarness h)
    {
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        InlineDispatcher.Run(() => h.Shell.OpenProjectFilesCommand.Execute(group));
        var files = group.Files!;
        await TabTestHarness.Eventually(() => h.FolderWatcher.Folders.Contains(files.Folder), "the explorer");
        return files;
    }

    [Theory]
    [InlineData(FilesTabChanges.ClaudesChanges, false)]
    [InlineData(FilesTabChanges.GitHead, true)]
    public async Task A_new_Files_tab_starts_with_the_changes_Settings_chose(FilesTabChanges choice, bool againstHead)
    {
        await using var h = new TabTestHarness();
        using (var settings = new SettingsViewModel(h.Services, null))
        {
            settings.Git.NewFilesTabsShowGitHead = choice == FilesTabChanges.GitHead;
            settings.Git.NewFilesTabsShowClaudesChanges = choice == FilesTabChanges.ClaudesChanges;
        }
        Assert.Equal(choice, h.Services.Settings.Git.NewFilesTabsShow);

        var files = await OpenFilesAsync(h);

        Assert.Equal(againstHead, files.AgainstGitHead);
        // The tab then has its own, which the setting doesn't change.
        InlineDispatcher.Run(() => files.AgainstGitHead = !againstHead);
        h.Services.Settings.Git.NewFilesTabsShow = choice;
        h.Services.SaveSettings();
        Assert.Equal(!againstHead, files.AgainstGitHead);
    }

    /// <summary>A Files tab saved with its own changes, and only the changed files listed, comes back so, whatever Settings says.</summary>
    [Theory]
    [InlineData(FilesTabChanges.GitHead, false)]
    [InlineData(FilesTabChanges.ClaudesChanges, true)]
    public async Task A_Files_tab_brought_back_keeps_its_own_changes(FilesTabChanges setting, bool saved)
    {
        await using var h = new TabTestHarness(s => s.Git.NewFilesTabsShow = setting);
        h.Services.State.ProjectFiles = [new Core.ProjectFiles.ProjectFilesState { Folder = h.WorkFolder, AgainstGitHead = saved, ShowOnlyChanged = saved }];

        InlineDispatcher.Run(() => h.Shell.Restore(null));

        var files = h.Shell.Groups.Single().Files!;
        Assert.Equal(saved, files.AgainstGitHead);
        Assert.Equal(saved, files.ShowOnlyChanged);
    }

    /// <summary>
    /// <b>Ignored files</b>, on Settings → Files and diffs: dimmed by default; hidden, they leave the tree of every open
    /// Files tab at once, and come back dimmed. Outside a repository, as here, those are the folders the <c>@</c> list skips.
    /// </summary>
    [Fact]
    public async Task Hidden_ignored_files_leave_the_tree_at_once_and_come_back_dimmed()
    {
        await using var h = new TabTestHarness();
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "node_modules", "lib"));
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "src"));
        File.WriteAllText(Path.Combine(h.WorkFolder, "src", "app.js"), "");
        var files = await OpenFilesAsync(h);
        IReadOnlyList<(string, bool)> Tree() => InlineDispatcher.Read(() => files.Rows.Select(r => (r.Name, r.IsIgnored)).ToList());
        await TabTestHarness.Eventually(() => Tree().Contains(("node_modules", true)), "node_modules, dimmed");
        Assert.Contains(("src", false), Tree());
        using var settings = new SettingsViewModel(h.Services, null);

        InlineDispatcher.Run(() => settings.Files.IgnoredFilesHidden = true);
        Assert.Equal(IgnoredFiles.Hidden, h.Services.Settings.Git.IgnoredFiles);
        await TabTestHarness.Eventually(() => Tree().All(r => r.Item1 != "node_modules"), "node_modules hidden");
        Assert.Contains(("src", false), Tree());

        InlineDispatcher.Run(() => settings.Files.IgnoredFilesDimmed = true);
        await TabTestHarness.Eventually(() => Tree().Contains(("node_modules", true)), "node_modules back, dimmed");
    }

    [Fact]
    public async Task A_Files_tab_opened_with_ignored_files_hidden_never_lists_them()
    {
        await using var h = new TabTestHarness(s => s.Git.IgnoredFiles = IgnoredFiles.Hidden);
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "bin"));
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "src"));

        var files = await OpenFilesAsync(h);

        await TabTestHarness.Eventually(() => InlineDispatcher.Read(() => files.Rows.Any(r => r.Name == "src")), "src");
        Assert.DoesNotContain(InlineDispatcher.Read(() => files.Rows.ToList()), r => r.Name == "bin" || r.IsIgnored);
    }
}
