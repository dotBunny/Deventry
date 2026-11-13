using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.ViewModels.Settings;
using Deventry.Core.ProjectTools;
using Deventry.Core.ProjectTools.Godot;
using Deventry.Core.ProjectTools.Unity;
using Deventry.Core.ProjectTools.Unreal;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// A project's settings, in their own window (DESIGN.md §14, "The project's settings"): its Links, Actions, Tools and
/// Group pages, opened from a tab's or a group's menu. Project files are written in temporary folders; engines are a few
/// files; nothing runs.
/// </summary>
public class ProjectSettingsTests
{
    private static void Write(string path, string text) => UnrealFixture.WriteFile(path, text);

    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    private static string[] Names(JsonNode root, string key) => [.. root[key]!.AsArray().Select(e => e!["name"]?.GetValue<string>() ?? "")];

    /// <summary>
    /// Opens a tab's project settings the way its menu does (the selected tab's without one), and makes the window's view
    /// model from what the shell asked for.
    /// </summary>
    private static async Task<ProjectSettingsWindowViewModel> OpenSettingsAsync(TabTestHarness h, TabViewModel? tab = null)
    {
        var opening = await OpeningAsync(h, () => h.Shell.OpenTabProjectSettingsCommand.ExecuteAsync(tab ?? h.Shell.SelectedTab));
        return Window(h, opening!);
    }

    /// <summary>What the shell asked its view to open while <paramref name="open"/> ran; null for nothing.</summary>
    private static async Task<ProjectSettingsOpening?> OpeningAsync(TabTestHarness h, Func<Task> open)
    {
        ProjectSettingsOpening? opening = null;
        h.Shell.ShowProjectSettingsWindow = o =>
        {
            opening = o;
            return Task.CompletedTask;
        };
        await open();
        return opening;
    }

    /// <summary>The window's view model, as the view makes it for <paramref name="opening"/>.</summary>
    private static ProjectSettingsWindowViewModel Window(TabTestHarness h, ProjectSettingsOpening opening) =>
        new(h.Services, h.Shell.ProjectSettingsFor(opening.Folder)!, opening.Page);

    /// <summary>A tab that isn't started: its folder is gone, so selecting it doesn't start a session.</summary>
    private static TabViewModel AddUnstartedTab(TabTestHarness h, string name)
    {
        var tab = new TabViewModel(h.Services, h.Shell, new TabState { Folder = Path.Combine(h.Root, name) }, isRestored: false);
        var group = new TabGroupViewModel(tab.Folder, TabGroupViewModel.Palette[1].Color, isCollapsed: false);
        group.Tabs.Add(tab);
        h.Shell.Groups.Add(group);
        return tab;
    }

    // ---- The group -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_tabs_menu_opens_its_folders_project_settings_in_a_window_named_after_its_project()
    {
        await using var h = new TabTestHarness();

        var tab = await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h, tab);
        var project = settings.Project;
        Assert.Equal("work", project.Heading);
        Assert.Equal("work · Project settings", project.WindowTitle);
        Assert.Equal(h.WorkFolder, project.Folder);

        // Its heading has the color of the folder's group in the main window.
        Assert.Same(h.Shell.Groups.Single(g => g.Tabs.Contains(tab)), project.Group);

        // Its pages, in its sidebar; it opens at Links, and a page picked there shows.
        Assert.Equal([ProjectSettingsWindowViewModel.LinksPage, ProjectSettingsWindowViewModel.ActionsPage, ProjectSettingsWindowViewModel.SolutionPage, ProjectSettingsWindowViewModel.ToolsPage, ProjectSettingsWindowViewModel.GroupPage],
            settings.NavEntries.Select(e => e.Title));
        Assert.IsType<ProjectLinksPage>(settings.CurrentPage);
        settings.SelectedNavEntry = settings.NavEntries.Single(e => e.Title == ProjectSettingsWindowViewModel.ToolsPage);
        Assert.IsType<ProjectToolChoicesPage>(settings.CurrentPage);
        Assert.Equal(ProjectSettingsWindowViewModel.ToolsPage, settings.SelectedNavEntry?.Title);

        // A provider finds a project: the window is named after it.
        UnrealFixture.Write(h.Root, h.WorkFolder);
        await tab.ProjectTools.RefreshAsync();
        await TabTestHarness.Eventually(() => project.Heading == "NightOwl", "the project's name");
        Assert.Equal("NightOwl · Project settings", project.WindowTitle);

        // Another tab's menu opens its own folder's.
        var other = AddUnstartedTab(h, "elsewhere");
        using var third = await OpenSettingsAsync(h, other);
        Assert.Equal((other.Folder, "elsewhere"), (third.Project.Folder, third.Project.Heading));
        Assert.Equal(h.WorkFolder, project.Folder);
    }

    /// <summary>The project's pages left Settings for their own window: Settings neither lists them nor finds them.</summary>
    [Fact]
    public async Task Settings_has_no_project_pages()
    {
        var (h, _, _) = ProjectToolsTests.UnrealHarness();
        await using var _h = h;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        using var settings = new SettingsViewModel(h.Services, null, host: h.Shell);

        Assert.DoesNotContain(settings.Pages, p => p is ProjectPage);
        Assert.DoesNotContain(settings.NavEntries, e => e.Title is ProjectSettingsWindowViewModel.LinksPage or ProjectSettingsWindowViewModel.ActionsPage
            or ProjectSettingsWindowViewModel.ToolsPage or ProjectSettingsWindowViewModel.GroupPage or "NightOwl");
        settings.SearchText = "web links";
        Assert.Empty(settings.SearchResults);
        settings.SearchText = "theme";
        Assert.Equal("Appearance → Look", settings.SearchResults[0].Where);
    }

    [Fact]
    public async Task Reset_to_defaults_leaves_the_project_files_and_the_projects_choices_alone()
    {
        var (h, _, uproject) = ProjectToolsTests.UnrealHarness();
        await using var _h = h;
        var shared = Path.Combine(h.WorkFolder, ProjectFile.SharedName);
        Write(shared, """{ "links": [ { "name": "Board", "url": "https://example.com/board" } ] }""");
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        h.Services.ProjectTools.Remember(uproject, UnrealProvider.ConfigurationKey, "DebugGame");
        h.Services.Settings.ProjectTools.SetValue(UnrealProvider.KindId, UnrealProvider.ConfigurationKey, nameof(UnrealConfiguration.DebugGame));
        using var settings = new SettingsViewModel(h.Services, null);
        var before = File.ReadAllText(shared);

        settings.ProjectType(UnrealProvider.KindId).ResetAllSections();
        settings.AllTools.ResetAllSections();
        settings.General.ResetAllSections();
        settings.Advanced.ResetAllSections();

        Assert.Equal(UnrealConfiguration.Development, UnrealProvider.DefaultConfiguration(h.Services.Settings.ProjectTools));
        Assert.Equal(before, File.ReadAllText(shared));
        Assert.False(File.Exists(Path.Combine(h.WorkFolder, ProjectFile.LocalName)));
        Assert.Equal("DebugGame", h.Services.State.ProjectTools.Get(uproject, UnrealProvider.ConfigurationKey));
    }

    // ---- Links -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Links_are_added_edited_moved_and_removed_in_their_own_file_which_keeps_its_other_keys()
    {
        await using var h = new TabTestHarness();
        var shared = Path.Combine(h.WorkFolder, ProjectFile.SharedName);
        var local = Path.Combine(h.WorkFolder, ProjectFile.LocalName);
        Write(shared, """
            {
              // The team's links and actions.
              "$schema": "https://example.com/deventry.schema.json",
              "actions": [ { "name": "Test", "command": "make test", "os": ["linux", "macos", "windows"] } ],
              "links": [ { "name": "Board", "url": "https://example.com/board", "note": "kept" } ],
            }
            """);
        Write(local, """{ "links": [ { "url": "https://mine.example" } ], "extra": 1 }""");
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Links.Count == 2, "the tab's links");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;

        Assert.Equal(["Board", "https://mine.example"], project.Links.Select(r => r.Name));
        Assert.Equal(["Shared (deventry.json)", "Just me (deventry.local.json)"], project.Links.Select(r => r.FileText));
        Assert.All(project.Links, r => Assert.False(r.HasWarning));

        // Add: the dialog asks which file, just the user's by default.
        project.AddLinkCommand.Execute(null);
        var editor = Assert.IsType<ProjectLinkEditorViewModel>(project.Editor);
        Assert.Equal("Add a link", editor.Title);
        Assert.True(editor.AsksForFile);
        Assert.True(editor.SaveToLocal);
        Assert.False(editor.SaveCommand.CanExecute(null));
        editor.Name = "Pull request";
        editor.Url = "https://github.com/org/repo/compare/{branch}?expand=1";
        Assert.Null(editor.UrlProblem);
        editor.SaveToShared = true;
        Assert.Contains("comments in it are dropped", editor.FileNote, StringComparison.Ordinal);
        editor.SaveCommand.Execute(null);

        Assert.Null(project.Editor);
        Assert.Equal(["Board", "Pull request", "https://mine.example"], project.Links.Select(r => r.Name));
        Assert.Equal("Pull request", project.SelectedLink!.Name);
        var root = Read(shared);
        Assert.Equal(["Board", "Pull request"], Names(root, "links"));
        Assert.Equal("https://github.com/org/repo/compare/{branch}?expand=1", root["links"]![1]!["url"]!.GetValue<string>());
        Assert.Equal("make test", root["actions"]![0]!["command"]!.GetValue<string>());
        Assert.Equal(3, root["actions"]![0]!["os"]!.AsArray().Count);
        Assert.Equal("https://example.com/deventry.schema.json", root["$schema"]!.GetValue<string>());
        Assert.DoesNotContain("The team's links", File.ReadAllText(shared), StringComparison.Ordinal);
        // The project's menu picks it up, as it does after the actions editor saves.
        await TabTestHarness.Eventually(() => tab.ProjectTools.Links.Select(l => l.Name).SequenceEqual(["Board", "Pull request", "https://mine.example"]), "the new link in the menu");
        Assert.Equal("No git branch.", InlineDispatcher.Read(() => tab.ProjectTools.Links[1].Problem));

        // Edit: the entry's other fields are kept, and it stays in its file.
        project.SelectedLink = project.Links[0];
        project.EditLinkCommand.Execute(null);
        editor = Assert.IsType<ProjectLinkEditorViewModel>(project.Editor);
        Assert.Equal(("Edit link", false, "Board", "https://example.com/board"), (editor.Title, editor.AsksForFile, editor.Name, editor.Url));
        Assert.Equal("In deventry.json", editor.FileText);
        editor.Name = "Sprint board";
        editor.Url = "https://example.com/sprint";
        editor.SaveCommand.Execute(null);
        root = Read(shared);
        Assert.Equal(["Sprint board", "Pull request"], Names(root, "links"));
        Assert.Equal(("https://example.com/sprint", "kept"), (root["links"]![0]!["url"]!.GetValue<string>(), root["links"]![0]!["note"]!.GetValue<string>()));

        // Move: within its file. The shared file's links come first, so the local one can't move above them.
        project.SelectedLink = project.Links.Single(r => r.Name == "Pull request");
        Assert.False(project.MoveLinkDownCommand.CanExecute(null));
        project.MoveLinkUpCommand.Execute(null);
        Assert.Equal(["Pull request", "Sprint board"], Names(Read(shared), "links"));
        project.SelectedLink = project.Links.Single(r => r.Scope == ProjectFileScope.Local);
        Assert.False(project.MoveLinkUpCommand.CanExecute(null));
        Assert.False(project.MoveLinkDownCommand.CanExecute(null));

        // Remove: the local file keeps its other keys, and a link without a name stayed without one until then.
        Assert.Null(Read(local)["links"]![0]!["name"]);
        project.RemoveLinkCommand.Execute(null);
        root = Read(local);
        Assert.Empty(root["links"]!.AsArray());
        Assert.Equal(1, root["extra"]!.GetValue<int>());
        Assert.Null(project.LinksError);
        await TabTestHarness.Eventually(() => tab.ProjectTools.Links.Select(l => l.Name).SequenceEqual(["Pull request", "Sprint board"]), "the menu after the changes");
    }

    [Theory]
    [InlineData("file:///etc/hosts", "\"file:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("javascript:alert(1)", "\"javascript:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("vscode://file/x", "\"vscode:\" links don't open from Deventry; only https, http and mailto do.")]
    [InlineData("example.com/board", "example.com/board isn't a full web address, such as https://example.com.")]
    [InlineData("https://example.com/{ticket}", "Unknown placeholder {ticket}: use {branch}, {changelist} or {folderName}.")]
    [InlineData("https://github.com/org/repo/compare/{branch}?expand=1", null)]
    [InlineData("https://swarm.example/changes/{changelist}", null)]
    [InlineData("http://docs.example/{folderName}/", null)]
    [InlineData("mailto:team@example.com", null)]
    public async Task A_links_address_is_checked_as_the_menu_opens_links(string url, string? problem)
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;
        project.AddLinkCommand.Execute(null);
        var editor = Assert.IsType<ProjectLinkEditorViewModel>(project.Editor);
        Assert.Equal(ProjectLinks.PlaceholderHint, editor.PlaceholderHint);

        editor.Url = url;

        Assert.Equal(problem, editor.UrlProblem);
        Assert.Equal(problem is null, editor.SaveCommand.CanExecute(null));
        editor.CancelCommand.Execute(null);
        Assert.Null(project.Editor);
        Assert.False(File.Exists(Path.Combine(h.WorkFolder, ProjectFile.LocalName)));
    }

    [Fact]
    public async Task A_link_in_a_file_that_wont_open_is_listed_with_why()
    {
        await using var h = new TabTestHarness();
        Write(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """
            { "links": [ { "name": "Hosts", "url": "file:///etc/hosts" }, { "name": "No address" }, "just text" ] }
            """);
        await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h);
        var rows = settings.Project.Links;

        Assert.Equal(["Hosts", "(can't be read)", "(can't be read)"], rows.Select(r => r.Name));
        Assert.Equal("\"file:\" links don't open from Deventry; only https, http and mailto do.", rows[0].Warning);
        Assert.Equal(["links[1] has no url, so it was skipped.", "links[2] isn't an object, so it was skipped."], rows.Skip(1).Select(r => r.Warning));
        // One that can't be read can only be removed; removing it keeps the others as they were.
        settings.Project.SelectedLink = rows[2];
        Assert.False(settings.Project.EditLinkCommand.CanExecute(null));
        settings.Project.RemoveLinkCommand.Execute(null);
        var root = Read(Path.Combine(h.WorkFolder, ProjectFile.SharedName));
        Assert.Equal(2, root["links"]!.AsArray().Count);
        Assert.Equal("No address", root["links"]![1]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_file_that_isnt_valid_JSON_is_never_rewritten_and_the_page_says_why()
    {
        await using var h = new TabTestHarness();
        var shared = Path.Combine(h.WorkFolder, ProjectFile.SharedName);
        var local = Path.Combine(h.WorkFolder, ProjectFile.LocalName);
        const string broken = "{ \"links\": [ { \"name\": \"Board\" ";
        Write(shared, broken);
        await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;

        var error = Assert.Single(project.LinkFileErrors);
        Assert.StartsWith("deventry.json isn't valid JSON (", error, StringComparison.Ordinal);
        Assert.EndsWith("so Deventry won't rewrite it. Fix it by hand first.", error, StringComparison.Ordinal);

        // The dialog refuses that file, with the reason, and takes the other.
        project.AddLinkCommand.Execute(null);
        var editor = Assert.IsType<ProjectLinkEditorViewModel>(project.Editor);
        editor.Url = "https://example.com";
        editor.SaveToShared = true;
        Assert.Equal(error, editor.FileProblem);
        Assert.False(editor.SaveCommand.CanExecute(null));
        editor.SaveToLocal = true;
        Assert.True(editor.SaveCommand.CanExecute(null));
        editor.CancelCommand.Execute(null);

        // The Actions page won't edit it either.
        project.SelectedProjectActionFile = project.ProjectActionFiles.Single(f => f.Value == ProjectFileScope.Shared);
        Assert.False(project.CanEditProjectActionFile);
        Assert.Equal(error, project.ProjectActionFileError);
        Assert.False(project.AddProjectActionCommand.CanExecute(null));

        // A file that stops being JSON while the page is open is refused when saving, and the page shows it as it is.
        Write(local, "not json");
        project.AddLinkCommand.Execute(null);
        editor = Assert.IsType<ProjectLinkEditorViewModel>(project.Editor);
        editor.Url = "https://example.com";
        editor.SaveCommand.Execute(null);
        Assert.StartsWith("deventry.local.json isn't valid JSON (", project.LinksError, StringComparison.Ordinal);
        Assert.Empty(project.Links);
        Assert.Equal(2, project.LinkFileErrors.Count);

        Assert.Equal(broken, File.ReadAllText(shared));
        Assert.Equal("not json", File.ReadAllText(local));
    }

    // ---- Actions ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Actions_page_edits_either_files_actions_and_keeps_what_it_doesnt_edit()
    {
        await using var h = new TabTestHarness();
        var local = Path.Combine(h.WorkFolder, ProjectFile.LocalName);
        Write(local, """
            {
              "actions": [
                { "name": "First", "command": "one", "os": ["windows", "macos", "linux"] },
                { "name": "Elsewhere", "command": "only there", "os": ["plan9"] },
              ],
              "links": [ { "name": "Mine", "url": "https://mine.example" } ],
            }
            """);
        var tab = await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h);
        settings.SelectedPage = ProjectSettingsWindowViewModel.ActionsPage;
        var project = settings.Project;
        Assert.IsType<ProjectActionsPage>(settings.CurrentPage);
        Assert.Equal(ProjectFileScope.Local, project.SelectedProjectActionFile.Value);
        Assert.Equal(["First", "Elsewhere"], project.ProjectActions.Select(r => r.Name));
        Assert.Contains("only on plan9", project.ProjectActions[1].Detail, StringComparison.Ordinal);
        Assert.Contains("comments in it are dropped", project.ProjectActionsNote, StringComparison.Ordinal);

        // The dialog adds to the file the page has chosen.
        project.AddProjectActionCommand.Execute(null);
        var editor = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        editor.Name = "Second";
        editor.Command = "two";
        editor.WorkingFolder = "sub";
        editor.SaveCommand.Execute(null);
        Assert.Null(project.Editor);
        // Saved at once, as the rest of Settings is.
        Assert.Contains("Second", File.ReadAllText(local), StringComparison.Ordinal);
        project.MoveProjectActionUpCommand.Execute(null);
        project.SelectedProjectAction = project.ProjectActions.Single(r => r.Name == "First");
        project.EditProjectActionCommand.Execute(null);
        editor = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        Assert.Equal("Edit action", editor.Title);
        editor.Name = "First, renamed";
        editor.SaveCommand.Execute(null);
        Assert.Equal(["First, renamed", "Second", "Elsewhere"], project.ProjectActions.Select(r => r.Name));

        var root = Read(local);
        Assert.Equal(["First, renamed", "Second", "Elsewhere"], Names(root, "actions"));
        Assert.Equal(3, root["actions"]![0]!["os"]!.AsArray().Count);
        Assert.Equal("plan9", root["actions"]![2]!["os"]![0]!.GetValue<string>());
        Assert.Equal("sub", root["actions"]![1]!["folder"]!.GetValue<string>());
        Assert.Equal("https://mine.example", root["links"]![0]!["url"]!.GetValue<string>());
        // Each save reloads the tab; it ends up with the last one.
        await TabTestHarness.Eventually(() => tab.ProjectTools.Actions.Select(a => a.Label).SequenceEqual(["First, renamed", "Second"]), "the tab's actions");
        Assert.False(File.Exists(Path.Combine(h.WorkFolder, ProjectFile.SharedName)));

        project.SelectedProjectAction = project.ProjectActions.Single(r => r.Name == "Elsewhere");
        project.RemoveProjectActionCommand.Execute(null);
        Assert.Equal(["First, renamed", "Second"], Names(Read(local), "actions"));
        await project.OpenProjectActionFileCommand.ExecuteAsync(null);
        Assert.Equal([local], h.Platform.OpenedFiles);
    }

    [Fact]
    public async Task An_action_with_ifExists_follows_its_file_and_the_Actions_page_lists_it_either_way()
    {
        await using var h = new TabTestHarness();
        var local = Path.Combine(h.WorkFolder, ProjectFile.LocalName);
        Write(local, """
            {
              "actions": [
                { "name": "Build", "command": "make" },
                { "name": "Play", "command": "Build/Game", "ifExists": "Build/Game" },
              ],
            }
            """);
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Actions.Select(a => a.Label).SequenceEqual(["Build"]), "the tab's actions");
        using var settings = await OpenSettingsAsync(h);
        settings.SelectedPage = ProjectSettingsWindowViewModel.ActionsPage;
        var project = settings.Project;
        Assert.Equal(["Build", "Play"], project.ProjectActions.Select(r => r.Name));
        Assert.EndsWith("  · only when Build/Game exists", project.ProjectActions[1].Detail, StringComparison.Ordinal);

        // The dialog shows it, and keeps it when something else changes.
        project.SelectedProjectAction = project.ProjectActions[1];
        project.EditProjectActionCommand.Execute(null);
        var editor = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        Assert.Equal("Build/Game", editor.IfExists);
        editor.Name = "Play the build";
        editor.SaveCommand.Execute(null);
        Assert.EndsWith("only when Build/Game exists", project.ProjectActions[1].Detail, StringComparison.Ordinal);
        Assert.Equal("Build/Game", Read(local)["actions"]![1]!["ifExists"]!.GetValue<string>());

        // Browse… writes a file under the tab's folder relative to it; several are separated by semicolons.
        project.SelectedProjectAction = project.ProjectActions[0];
        project.EditProjectActionCommand.Execute(null);
        editor = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        Assert.Equal("", editor.IfExists);
        h.Platform.FileToPick = Path.Combine(h.WorkFolder, "Makefile");
        await editor.BrowseIfExistsCommand.ExecuteAsync(null);
        Assert.Equal("Makefile", editor.IfExists);
        editor.IfExists += " ; src/ ;";
        editor.SaveCommand.Execute(null);
        Assert.Equal(["Makefile", "src/"], Read(local)["actions"]![0]!["ifExists"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.EndsWith("only when Makefile and src/ exist", project.ProjectActions[0].Detail, StringComparison.Ordinal);
        await TabTestHarness.Eventually(() => !tab.ProjectTools.Actions.Any(), "the tab's actions");

        // Emptied, the field is gone from the file and the action is always shown.
        project.SelectedProjectAction = project.ProjectActions[0];
        project.EditProjectActionCommand.Execute(null);
        editor = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        Assert.Equal("Makefile; src/", editor.IfExists);
        editor.IfExists = "  ";
        editor.SaveCommand.Execute(null);
        Assert.Null(Read(local)["actions"]![0]!["ifExists"]);
        await TabTestHarness.Eventually(() => tab.ProjectTools.Actions.Select(a => a.Label).SequenceEqual(["Build"]), "the tab's actions");

        // A build makes it; the next read shows the action.
        Write(Path.Combine(h.WorkFolder, "Build", "Game"), "");
        await tab.ProjectTools.RefreshFileAsync();
        Assert.Equal(["Build", "Play the build"], tab.ProjectTools.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task An_action_added_with_a_group_and_icon_shows_in_its_section_of_every_tabs_Actions_in_the_folder()
    {
        await using var h = new TabTestHarness();
        Write(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """{ "links": [ { "name": "Board", "url": "https://example.com/board" } ] }""");
        var tab = await h.OpenTabAsync();
        var sibling = new TabViewModel(h.Services, h.Shell, new TabState { Folder = h.WorkFolder }, isRestored: false);
        await sibling.ProjectTools.RefreshAsync();
        h.Shell.Groups.Single().Tabs.Add(sibling);
        var opening = await OpeningAsync(h, () => h.Shell.OpenGroupProjectSettingsCommand.ExecuteAsync(h.Shell.Groups.Single()));
        using var settings = Window(h, opening!);
        settings.SelectedPage = ProjectSettingsWindowViewModel.ActionsPage;
        var project = settings.Project;
        project.SelectedProjectActionFile = project.ProjectActionFiles.Single(f => f.Value == ProjectFileScope.Shared);

        project.AddProjectActionCommand.Execute(null);
        var editor = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        Assert.Equal("Add an action", editor.Title);
        Assert.False(editor.SaveCommand.CanExecute(null));
        // No groups yet to offer; no icon is its mode's, which follows the mode.
        Assert.Empty(editor.Groups);
        Assert.Equal((null, MenuIcons.Terminal), (editor.SelectedIcon!.Name, editor.SelectedIcon.Shown));
        editor.Name = "Serve";
        editor.Command = "npm run dev";
        editor.LaunchAndForget = true;
        Assert.Equal((null, MenuIcons.Play), (editor.SelectedIcon!.Name, editor.SelectedIcon.Shown));
        editor.Group = " Run ";
        editor.SelectedIcon = editor.IconChoices.Single(c => c.Name == MenuIcons.Upload);
        editor.SaveCommand.Execute(null);

        Assert.Null(project.Editor);
        Assert.Equal("Serve", project.SelectedProjectAction!.Name);
        Assert.Contains("· in Run", project.SelectedProjectAction.Detail, StringComparison.Ordinal);
        var written = Read(Path.Combine(h.WorkFolder, ProjectFile.SharedName));
        Assert.Equal("https://example.com/board", written["links"]![0]!["url"]!.GetValue<string>());
        var action = written["actions"]![0]!;
        Assert.Equal(("Serve", "npm run dev", "launch", "Run", "upload"),
            (action["name"]!.GetValue<string>(), action["command"]!.GetValue<string>(), action["mode"]!.GetValue<string>(), action["group"]!.GetValue<string>(), action["icon"]!.GetValue<string>()));
        await TabTestHarness.Eventually(() => tab.ProjectTools.Actions.Count == 1 && sibling.ProjectTools.Actions.Count == 1, "both tabs");
        Assert.Equal(ProjectActionKind.Launch, sibling.ProjectTools.Actions.Single().Kind);
        var actions = InlineDispatcher.Read(() => sibling.ProjectTools.Menu.Single(e => e.IsSubmenu && e.Label == "Actions").Items);
        Assert.Equal(["Run", "Serve"], actions.Select(e => e.Label));
        Assert.Equal(MenuIcons.Upload, actions[1].Icon);

        // The next action's dialog offers the group; the link dialog doesn't, as links are in a submenu of their own.
        project.AddProjectActionCommand.Execute(null);
        var next = Assert.IsType<ProjectActionEditorViewModel>(project.Editor);
        Assert.Equal(["Run"], next.Groups);
        next.CancelCommand.Execute(null);
        settings.SelectedPage = ProjectSettingsWindowViewModel.LinksPage;
        project.AddLinkCommand.Execute(null);
        Assert.Empty(Assert.IsType<ProjectLinkEditorViewModel>(project.Editor).Groups);
    }

    // ---- The solution (issue #112) -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Solution_page_saves_a_file_from_the_projects_folder_and_the_menu_offers_it()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var sln = Path.Combine(h.WorkFolder, "Code", "Game.sln");
        Write(sln, "");
        using var settings = await OpenSettingsAsync(h);
        settings.SelectedPage = ProjectSettingsWindowViewModel.SolutionPage;
        Assert.IsType<ProjectSolutionPage>(settings.CurrentPage);
        var project = settings.Project;
        Assert.Equal(ProjectFileScope.Shared, project.SelectedSolutionFile.Value);
        Assert.Equal("None in deventry.json.", project.SolutionText);
        Assert.False(project.RemoveSolutionCommand.CanExecute(null));

        h.Platform.FileToPick = sln;
        await project.ChooseSolutionCommand.ExecuteAsync(null);

        // Offered solutions first, and written from the project's folder.
        Assert.Contains("*.sln", h.Platform.FileTypesOffered![0].Patterns);
        Assert.Equal("Code/Game.sln", ProjectFile.ReadSolution(h.WorkFolder, ProjectFileScope.Shared));
        Assert.Equal("Code/Game.sln", project.SolutionText);
        Assert.Contains("as Open Game.sln, its main action", project.SolutionNote, StringComparison.Ordinal);
        await TabTestHarness.Eventually(() => InlineDispatcher.Read(() => tab.ProjectTools.MainAction?.Label) == "Open Game.sln", "the menu's solution");

        // The user's own file's shows in place of the shared one's.
        project.SelectedSolutionFile = project.ProjectActionFiles[1];
        Write(Path.Combine(h.WorkFolder, "Mine.sln"), "");
        h.Platform.FileToPick = Path.Combine(h.WorkFolder, "Mine.sln");
        await project.ChooseSolutionCommand.ExecuteAsync(null);
        project.SelectedSolutionFile = project.ProjectActionFiles[0];
        Assert.Equal("deventry.local.json's solution shows in its place, for you.", project.SolutionNote);
        await TabTestHarness.Eventually(() => InlineDispatcher.Read(() => tab.ProjectTools.MainAction?.Label) == "Open Mine.sln", "the user's solution");

        project.RemoveSolutionCommand.Execute(null);
        Assert.Null(ProjectFile.ReadSolution(h.WorkFolder, ProjectFileScope.Shared));
        project.SelectedSolutionFile = project.ProjectActionFiles[1];
        project.RemoveSolutionCommand.Execute(null);
        await TabTestHarness.Eventually(() => InlineDispatcher.Read(() => tab.ProjectTools.MainAction) is null, "no solution");
    }

    // ---- The group's icon --------------------------------------------------------------------------------------------

    /// <summary>A picture with a PNG's first bytes; the project files check only its name and size.</summary>
    private static string Picture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }

    [Fact]
    public async Task A_picture_chosen_on_the_Group_page_is_saved_from_the_projects_folder_and_the_group_shows_it()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var logo = Picture(Path.Combine(h.WorkFolder, "Assets", "logo.png"));
        Write(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """{ "links": [ { "url": "https://example.com" } ] }""");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;
        Assert.Equal(ProjectFileScope.Shared, project.SelectedGroupFile.Value);
        Assert.Equal("None in deventry.json: the group shows its colored dot.", project.IconText);
        Assert.False(project.RemoveIconCommand.CanExecute(null));

        h.Platform.FileToPick = logo;
        await project.ChooseIconCommand.ExecuteAsync(null);

        // Offered pictures alone, and written from the project's folder, beside what the file had.
        Assert.Contains("*.png", h.Platform.FileTypesOffered!.Single().Patterns);
        Assert.Equal("Assets/logo.png", ProjectFile.ReadIcon(h.WorkFolder, ProjectFileScope.Shared));
        Assert.Single(ProjectFile.Read(h.WorkFolder, ToolOS.Linux).Links);
        Assert.Equal(("Assets/logo.png", logo, null), (project.IconText, project.IconPreview, project.GroupError));
        await TabTestHarness.Eventually(() => tab.ProjectTools.GroupIcon == logo, "the group's icon");
        Assert.Equal(logo, project.GroupIcon);

        project.RemoveIconCommand.Execute(null);

        Assert.Null(ProjectFile.ReadIcon(h.WorkFolder, ProjectFileScope.Shared));
        await TabTestHarness.Eventually(() => !tab.ProjectTools.HasGroupIcon, "the group's dot again");
    }

    [Fact]
    public async Task Choose_picks_the_groups_color_at_once_and_saves_it_in_the_chosen_file_as_it_closes_and_Remove_takes_it_out()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;
        var group = project.Group!;
        project.SelectedGroupFile = project.ProjectActionFiles[1];
        Assert.Equal("None in deventry.local.json: the group has the color remembered on this computer.", project.ColorText);
        Assert.False(project.RemoveColorCommand.CanExecute(null));
        Assert.True(project.CanChooseColor);

        // The picker opens on the group's color; each pick shows on the group at once, and nothing's written yet.
        var picker = project.PickColor()!;
        Assert.Equal(TabGroupViewModel.ToHex(group.Color), picker.Hex);
        picker.HsvColor = new Avalonia.Media.HsvColor(1, 30, 1, 1);
        picker.PickCommand.Execute(picker.Swatches[3]);
        Assert.Equal(TabGroupViewModel.Palette[3].Color, group.Color);
        Assert.Equal("#A66BFF", h.Services.State.FolderColors[group.Folder]);
        Assert.Null(ProjectFile.ReadColor(h.WorkFolder, ProjectFileScope.Local));

        // Closed, the color it was left on is saved in the chosen file, once.
        picker.Close();
        Assert.Equal("#A66BFF", ProjectFile.ReadColor(h.WorkFolder, ProjectFileScope.Local));
        Assert.Equal(("#A66BFF", true), (project.ColorText, project.HasColorBrush));
        Assert.False(File.Exists(Path.Combine(h.WorkFolder, ProjectFile.SharedName)));

        // Taken out, the group keeps the color it has, remembered on this computer.
        project.RemoveColorCommand.Execute(null);
        Assert.Null(ProjectFile.ReadColor(h.WorkFolder, ProjectFileScope.Local));
        Assert.Equal("#A66BFF", TabGroupViewModel.ToHex(group.Color));
    }

    [Fact]
    public async Task A_picture_that_cant_be_the_icon_isnt_saved_and_the_page_says_why()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        var drawing = Path.Combine(h.WorkFolder, "logo.svg");
        Write(drawing, "<svg/>");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;

        h.Platform.FileToPick = drawing;
        await project.ChooseIconCommand.ExecuteAsync(null);

        Assert.Equal($"Icon {drawing} isn't a PNG, ICO, JPEG, BMP, GIF or WebP picture.", project.GroupError);
        Assert.False(File.Exists(Path.Combine(h.WorkFolder, ProjectFile.SharedName)));
    }

    [Fact]
    public async Task The_page_opens_on_the_users_own_icon_which_shows_in_place_of_the_shared_one()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Picture(Path.Combine(h.WorkFolder, "Assets", "logo.png"));
        var mine = Picture(Path.Combine(h.WorkFolder, "mine.png"));
        Write(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """{ "icon": "Assets/logo.png" }""");
        Write(Path.Combine(h.WorkFolder, ProjectFile.LocalName), """{ "icon": "mine.png" }""");
        await tab.ProjectTools.RefreshFileAsync();
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;

        Assert.Equal((ProjectFileScope.Local, "mine.png"), (project.SelectedGroupFile.Value, project.IconText));
        Assert.Equal(mine, tab.ProjectTools.GroupIcon);

        project.SelectedGroupFile = project.ProjectActionFiles[0];
        Assert.Equal("Assets/logo.png", project.IconText);
        Assert.Equal("deventry.local.json's icon shows in its place, for you.", project.IconNote);
    }

    [Fact]
    public async Task A_groups_menu_opens_its_folders_project_settings_tab_or_no_tab()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var group = h.Shell.Groups.Single(g => g.Tabs.Contains(tab));

        var opening = await OpeningAsync(h, () => h.Shell.OpenGroupProjectSettingsCommand.ExecuteAsync(group));

        Assert.Equal((h.WorkFolder, ProjectSettingsWindowViewModel.LinksPage), (opening!.Folder, opening.Page));
        using (var settings = Window(h, opening))
        {
            Assert.Same(group, settings.Project.Group);
        }

        // With its Files tab open and no session tab, the folder's own project tools stand in for a tab's.
        h.Shell.OpenProjectFilesCommand.Execute(group);
        await h.Shell.CloseTabCommand.ExecuteAsync(tab);
        Assert.Null(group.ProjectTab);
        opening = await OpeningAsync(h, () => h.Shell.OpenGroupProjectSettingsCommand.ExecuteAsync(group));
        using var tabless = Window(h, opening!);
        Assert.Equal((h.WorkFolder, "work"), (tabless.Project.Folder, tabless.Project.Heading));
        Assert.Same(group, tabless.Project.Group);
    }

    [Fact]
    public async Task A_link_added_on_the_page_goes_in_its_group_and_the_menu_has_no_entries_to_add_them()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        // Actions and links are added in the project's settings, which a group's and a tab's menus open.
        var labels = InlineDispatcher.Read(() => tab.ProjectTools.Menu.Select(e => e.Label).ToArray());
        Assert.DoesNotContain("Add an action…", labels);
        Assert.DoesNotContain("Add a link…", labels);
        var opening = await OpeningAsync(h, () => h.Shell.OpenTabProjectSettingsCommand.ExecuteAsync(tab));
        using var settings = Window(h, opening!);
        Assert.IsType<ProjectLinksPage>(settings.CurrentPage);

        settings.Project.AddLinkCommand.Execute(null);
        var editor = Assert.IsType<ProjectLinkEditorViewModel>(settings.Project.Editor);
        Assert.Equal("Add a link", editor.Title);
        Assert.True(editor.AsksForFile);
        Assert.True(editor.SaveToLocal);
        Assert.Equal((null, MenuIcons.Link), (editor.SelectedIcon!.Name, editor.SelectedIcon.Shown));
        editor.Name = "Board";
        editor.Url = "https://example.com/board";
        editor.Group = "Planning";
        editor.SelectedIcon = editor.IconChoices.Single(c => c.Name == MenuIcons.Docs);
        editor.SaveCommand.Execute(null);

        Assert.Null(settings.Project.Editor);
        Assert.Equal(("Board", "In Planning"), (settings.Project.SelectedLink!.Name, settings.Project.SelectedLink.GroupText));
        var link = Read(Path.Combine(h.WorkFolder, ProjectFile.LocalName))["links"]![0]!;
        Assert.Equal(("Board", "Planning", "docs"), (link["name"]!.GetValue<string>(), link["group"]!.GetValue<string>(), link["icon"]!.GetValue<string>()));
        await TabTestHarness.Eventually(() => tab.ProjectTools.Links.Select(l => l.Name).SequenceEqual(["Board"]), "the new link in the menu");
        var links = InlineDispatcher.Read(() => tab.ProjectTools.Menu.Single(e => e.IsSubmenu && e.Label == "Links").Items);
        Assert.Equal(["Planning", "Board"], links.Select(e => e.Label));
        Assert.Equal(MenuIcons.Docs, links[1].Icon);

        // Edited again, its group and icon come back as saved.
        settings.Project.EditLinkCommand.Execute(null);
        var again = Assert.IsType<ProjectLinkEditorViewModel>(settings.Project.Editor);
        Assert.Equal(("Planning", MenuIcons.Docs), (again.Group, again.SelectedIcon!.Name));
    }

    [Fact]
    public async Task Tab_settings_says_where_the_actions_went_and_opens_that_page()
    {
        var (h, _, _) = ProjectToolsTests.UnrealHarness();
        await using var _h = h;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        h.Shell.OpenTabSettingsCommand.Execute(tab);
        var tabSettings = h.Shell.TabSettings!;

        Assert.Equal("In NightOwl's project settings, on Actions.", tabSettings.ProjectActionsText);
        var opening = await OpeningAsync(h, () => tabSettings.OpenProjectActionsCommand.ExecuteAsync(null));

        Assert.Equal((tab.Folder, ProjectSettingsWindowViewModel.ActionsPage), (opening!.Folder, opening.Page));
        // Tab settings stays open behind the window, with nothing applied yet.
        Assert.Same(tabSettings, h.Shell.TabSettings);
    }

    // ---- Tools -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Tools_page_changes_the_Unreal_configuration_and_engine_folder_the_menu_uses()
    {
        var (h, _, uproject) = ProjectToolsTests.UnrealHarness();
        await using var _h = h;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;

        Assert.True(project.HasToolsProject);
        Assert.Equal("NightOwl · Unreal Engine", project.ToolsTitle);
        Assert.Equal($"Remembered on this machine for {uproject}.", project.ToolsKeyText);
        Assert.False(project.HasSeveralProjects);
        Assert.Equal("Editor configuration", project.ToolsChoiceLabel);
        Assert.Equal(["Development", "DebugGame"], project.ToolsChoiceOptions.Select(o => o.Label));
        Assert.Equal("Development", project.SelectedToolsChoice!.Value);

        project.SelectedToolsChoice = project.ToolsChoiceOptions.Single(o => o.Value == "DebugGame");

        Assert.Equal("DebugGame", h.Services.State.ProjectTools.Get(uproject, UnrealProvider.ConfigurationKey));
        await TabTestHarness.Eventually(() => project.SelectedToolsChoice?.Value == "DebugGame", "the configuration");
        Assert.True(InlineDispatcher.Read(() => tab.ProjectTools.Menu.Single(e => e.Label == "DebugGame").IsChecked));
        Assert.Equal("Launch editor (DebugGame)", InlineDispatcher.Read(() => tab.ProjectTools.Actions[0].Label));

        // The engine folder: found in a parent folder until one is chosen.
        Assert.Equal(("Engine folder", h.Root, "Found automatically."), (project.ToolsFixName, project.ToolsFixCurrent, project.ToolsFixSource));
        Assert.False(project.ClearToolsFixCommand.CanExecute(null));
        h.Platform.FolderToPick = h.WorkFolder;
        await project.ChooseToolsFixCommand.ExecuteAsync(null);
        Assert.Contains("isn't an Unreal Engine folder", project.ToolsFixError, StringComparison.Ordinal);
        Assert.Null(h.Services.State.ProjectTools.Get(uproject, UnrealEngineLocator.EngineKey));

        var engine = Path.Combine(h.Root, "Engines", "UE_5.5");
        UnrealFixture.Write(engine, Path.Combine(h.Root, "unused"));
        h.Platform.FolderToPick = engine;
        await project.ChooseToolsFixCommand.ExecuteAsync(null);

        Assert.Null(project.ToolsFixError);
        Assert.Equal(engine, h.Services.State.ProjectTools.Get(uproject, UnrealEngineLocator.EngineKey));
        await TabTestHarness.Eventually(() => project.ToolsFixCurrent == engine, "the chosen engine");
        Assert.StartsWith("Chosen by you.", InlineDispatcher.Read(() => project.ToolsFixSource), StringComparison.Ordinal);
        Assert.Contains("chosen by you", InlineDispatcher.Read(() => tab.ProjectTools.HeaderLines[0]), StringComparison.Ordinal);
        Assert.True(project.ClearToolsFixCommand.CanExecute(null));

        await project.ClearToolsFixCommand.ExecuteAsync(null);

        Assert.Null(h.Services.State.ProjectTools.Get(uproject, UnrealEngineLocator.EngineKey));
        await TabTestHarness.Eventually(() => project.ToolsFixCurrent == h.Root, "the engine found again");
        Assert.Equal("Found automatically.", project.ToolsFixSource);
        Assert.False(project.ClearToolsFixCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_folder_with_several_projects_picks_one_on_the_Tools_page()
    {
        await using var h = new TabTestHarness(launcher: new FakeLauncher());
        UnrealFixture.Write(h.Root, Path.Combine(h.WorkFolder, "Alpha"));
        var alpha = Path.Combine(h.WorkFolder, "Alpha", "Alpha.uproject");
        File.Move(Path.Combine(h.WorkFolder, "Alpha", "NightOwl.uproject"), alpha);
        File.WriteAllText(Path.Combine(h.WorkFolder, "Beta.uproject"), """{ "EngineAssociation": "" }""");
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;

        Assert.True(project.HasSeveralProjects);
        Assert.Equal(["Beta (Beta.uproject)", $"Alpha ({Path.Combine("Alpha", "Alpha.uproject")})"], project.ProjectChoices.Select(c => c.Label));
        Assert.Equal("Beta", project.Heading);

        project.SelectedProjectChoice = project.ProjectChoices[1];

        Assert.Equal(alpha, h.Services.State.ProjectTools.ChosenProjectFor(h.WorkFolder));
        await TabTestHarness.Eventually(() => project.Heading == "Alpha", "the other project");
        Assert.True(InlineDispatcher.Read(() => project.SelectedProjectChoice?.Label.StartsWith("Alpha", StringComparison.Ordinal) == true));
        Assert.True(InlineDispatcher.Read(() => tab.ProjectTools.Menu.Single(e => e.Label == "Alpha").IsChecked));
    }

    [Fact]
    public async Task A_Unity_project_has_its_editor_and_code_optimization_on_the_Tools_page()
    {
        await using var h = new TabTestHarness(launcher: new FakeLauncher());
        h.Services.ProjectTools.OS = ToolOSExtensions.Current == ToolOS.Windows ? ToolOS.Windows : ToolOS.Linux;
        h.Services.ProjectTools.Paths = new ProjectToolPaths(Path.Combine(h.Root, "home"), ProgramFiles: Path.Combine(h.Root, "programs"));
        Write(Path.Combine(h.WorkFolder, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.20f1\n");
        Directory.CreateDirectory(Path.Combine(h.WorkFolder, "Assets"));
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;
        var root = tab.ProjectTools.Project!.ProjectPath;

        Assert.Equal(("Code optimization", "Release"), (project.ToolsChoiceLabel, project.SelectedToolsChoice!.Value));
        Assert.Equal("Unity editor", project.ToolsFixName);
        Assert.Equal(tab.ProjectTools.Project.Problem, project.ToolsFixCurrent);
        Assert.StartsWith("Not chosen", project.ToolsFixSource, StringComparison.Ordinal);

        project.SelectedToolsChoice = project.ToolsChoiceOptions.Single(o => o.Value == "Debug");
        Assert.Equal("Debug", h.Services.State.ProjectTools.Get(root, UnityProvider.OptimizationKey));

        var editor = Path.Combine(h.Root, "Editors", "2022.3.20f1", OperatingSystem.IsWindows() ? "Unity.exe" : "Unity");
        Write(editor, "");
        h.Platform.FileToPick = editor;
        await project.ChooseToolsFixCommand.ExecuteAsync(null);

        Assert.Equal(editor, h.Services.State.ProjectTools.Get(root, UnityEditors.EditorKey));
        await TabTestHarness.Eventually(() => project.ToolsFixCurrent == editor, "the chosen editor");
        await TabTestHarness.Eventually(() => project.SelectedToolsChoice?.Value == "Debug", "the optimization");
        Assert.Equal("Open in Unity (Debug)", InlineDispatcher.Read(() => tab.ProjectTools.Actions[0].Label));

        await project.ClearToolsFixCommand.ExecuteAsync(null);
        Assert.Null(h.Services.State.ProjectTools.Get(root, UnityEditors.EditorKey));
        await TabTestHarness.Eventually(() => project.ToolsFixCurrent == tab.ProjectTools.Project?.Problem, "the editor forgotten");
    }

    [Fact]
    public async Task A_Godot_project_has_its_executable_on_the_Tools_page_and_no_other_choice()
    {
        await using var h = new TabTestHarness(launcher: new FakeLauncher());
        Write(Path.Combine(h.WorkFolder, "project.godot"), """
            config_version=5

            [application]
            config/name="Night Owl"
            config/features=PackedStringArray("4.3", "Forward Plus")
            """);
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        using var settings = await OpenSettingsAsync(h);
        var project = settings.Project;
        var file = tab.ProjectTools.Project!.ProjectPath;

        Assert.Equal("Night Owl", project.Heading);
        Assert.False(project.HasToolsChoice);
        Assert.Equal("Godot executable", project.ToolsFixName);

        h.Platform.FileToPick = Path.Combine(h.Root, "not-it.txt");
        Write(h.Platform.FileToPick, "");
        await project.ChooseToolsFixCommand.ExecuteAsync(null);
        Assert.Contains("doesn't look like Godot", project.ToolsFixError, StringComparison.Ordinal);

        var godot = Path.Combine(h.Root, "tools", OperatingSystem.IsWindows() ? "Godot_v4.3-stable_win64.exe" : "godot4");
        Write(godot, "");
        h.Platform.FileToPick = godot;
        await project.ChooseToolsFixCommand.ExecuteAsync(null);

        Assert.Null(project.ToolsFixError);
        Assert.Equal(godot, h.Services.State.ProjectTools.Get(file, Core.ProjectTools.Godot.GodotExecutables.ExecutableKey));
        await TabTestHarness.Eventually(() => project.ToolsFixCurrent == godot, "the chosen Godot");

        await project.ClearToolsFixCommand.ExecuteAsync(null);
        Assert.Null(h.Services.State.ProjectTools.Get(file, Core.ProjectTools.Godot.GodotExecutables.ExecutableKey));
    }

    [Fact]
    public async Task Without_a_project_the_Tools_page_says_so_and_points_to_the_defaults()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        using var settings = await OpenSettingsAsync(h);
        settings.SelectedPage = ProjectSettingsWindowViewModel.ToolsPage;

        Assert.False(settings.Project.HasToolsProject);
        Assert.False(settings.Project.HasToolsFix);
        Assert.Equal("Open All tools", settings.Project.DefaultsLinkText);
        SettingsOpening? opening = null;
        h.Shell.ShowSettingsWindow = o =>
        {
            opening = o;
            return Task.CompletedTask;
        };

        await settings.Project.ShowToolsDefaultsCommand.ExecuteAsync(null);

        Assert.Equal(SettingsCategory.AllTools, opening?.Category);
    }

    [Fact]
    public async Task The_Tools_page_points_to_its_project_types_page_for_the_defaults()
    {
        var (h, _, _) = ProjectToolsTests.UnrealHarness();
        await using var _h = h;
        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.ProjectTools.Project is not null, "the project");
        using var settings = await OpenSettingsAsync(h);
        settings.SelectedPage = ProjectSettingsWindowViewModel.ToolsPage;

        Assert.Equal("The defaults every Unreal Engine project starts with are on its page in Project tools.", settings.Project.DefaultsText);
        Assert.Equal("Open Unreal Engine", settings.Project.DefaultsLinkText);
        SettingsOpening? opening = null;
        h.Shell.ShowSettingsWindow = o =>
        {
            opening = o;
            return Task.CompletedTask;
        };

        await settings.Project.ShowToolsDefaultsCommand.ExecuteAsync(null);

        Assert.Equal("Unreal Engine", opening?.Category);
        using var shown = new SettingsViewModel(h.Services, null, opening: opening);
        Assert.Same(shown.ProjectType(UnrealProvider.KindId), shown.CurrentPage);
    }

    // ---- Project tools in the sidebar (DESIGN.md §14, §18 "Project types") ------------------------------------------

    /// <summary>The pages under a heading in the sidebar, up to the next heading.</summary>
    private static string[] PagesUnder(SettingsViewModel settings, string heading) =>
        [.. settings.NavEntries.SkipWhile(e => !(e.IsHeading && e.Title == heading)).Skip(1).TakeWhile(e => !e.IsHeading).Select(e => e.Title)];

    [Fact]
    public async Task Project_tools_is_a_group_with_All_tools_and_a_page_for_each_project_type_then_Source_control_has_Git_and_Perforce()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);

        Assert.Equal(["Deventry", "Claude", "Your work", SettingsSidebar.ProjectTools, SettingsSidebar.SourceControl, SettingsSidebar.RemoteClients],
            settings.NavEntries.Where(e => e.IsHeading).Select(e => e.Title));
        // About is a window of its own, not a page (DESIGN.md §14, "About").
        Assert.Equal(["General", "Appearance", "Conversation", "Notifications", "Keyboard"], PagesUnder(settings, "Deventry"));
        // Advanced is the last of Claude's pages: it's mostly about running Claude Code.
        Assert.Equal(["Claude Code", "Accounts", "Tabs", "Usage", "Advanced"], PagesUnder(settings, "Claude"));
        Assert.Equal(["Sessions", "Files and diffs"], PagesUnder(settings, "Your work"));
        Assert.Equal(["All tools", "Unreal Engine", "Unity", "Godot"], PagesUnder(settings, SettingsSidebar.ProjectTools));
        Assert.Equal(["Git", "Perforce"], PagesUnder(settings, SettingsSidebar.SourceControl));
        // What every remote client shares, then a page for each client (DESIGN.md §18, "Remote clients").
        Assert.Equal(["All clients", "Telegram"], PagesUnder(settings, SettingsSidebar.RemoteClients));
        Assert.Equal(h.Services.ProjectTools.Detector.Types.Select(t => t.Name), settings.ProjectTypes.Select(p => p.Title));

        // Every page has an icon of its own; each project type's is its kind's.
        var pages = settings.NavEntries.Where(e => !e.IsHeading).ToList();
        Assert.Equal(pages.Count, pages.Select(e => e.IconKey).Distinct().Count());
        Assert.All(settings.ProjectTypes, page => Assert.Equal(ProjectTypeIcons.For(page.Kind), pages.Single(e => e.Title == page.Title).IconKey));
        Assert.DoesNotContain(settings.ProjectTypes, page => page.IconKey == ProjectTypeIcons.Other);
        // Unreal Engine's and Godot's are their logos, which their owners let Deventry show, drawn filled; Unity's is drawn
        // for what it's known by.
        Assert.Equal([UnrealProvider.KindId, GodotProvider.KindId], settings.ProjectTypes.Where(page => Logos.IsLogo(page.IconKey)).Select(page => page.Kind));
        // Perforce's page has Perforce's symbol, which Perforce lets Deventry show.
        Assert.Equal(Logos.Perforce, pages.Single(e => e.Title == "Perforce").IconKey);
        Assert.True(Logos.IsLogo(Logos.Perforce));

        // A type's page: its switches, then the defaults it declares, under its own line.
        var unreal = settings.ProjectType(UnrealProvider.KindId);
        settings.SelectedNavEntry = settings.NavEntries.Single(e => e.Title == "Unreal Engine");
        Assert.Same(unreal, settings.CurrentPage);
        Assert.Equal(h.Services.ProjectTools.Detector.Types.Single(t => t.Kind == UnrealProvider.KindId).Description, unreal.Description);
        Assert.Equal(["projectTools.types.unreal.enabled", "projectTools.types.unreal.tellClaude"], unreal.Switches.Select(r => r.Key));
        Assert.Equal(["projectTools.types.unreal.values.configuration", "projectTools.types.unreal.values.projectFiles"], unreal.Defaults.Select(r => r.Key));
        Assert.Equal(["Projects", "Defaults"], unreal.Groups.Select(g => g.Title));
        Assert.True(unreal.IsMachineOnly);

        // Search names the type's page, and the group's name still finds them all.
        settings.SearchText = "default editor configuration";
        Assert.Equal("Unreal Engine → Defaults", settings.SearchResults[0].Where);
        settings.SearchText = "project tools";
        Assert.Equal(["All tools", "Unreal Engine", "Unity", "Godot"], settings.SearchResults.Select(r => r.Category).Distinct());
    }

    /// <summary>Unity's Prefer JSON logging is a switch among its defaults, on to begin with, kept only once it's changed (issue #92).</summary>
    [Fact]
    public async Task Unitys_page_has_Prefer_JSON_logging_on_by_default()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);
        var unity = settings.ProjectType(UnityProvider.KindId);

        var row = Assert.IsType<ProjectTypeSwitchRow>(unity.Defaults.Single(r => r.Item.Title == "Prefer JSON logging"));
        Assert.Equal("projectTools.types.unity.values.preferJsonLogging", row.Key);
        Assert.True(row.IsOn);
        Assert.True(UnityProvider.PreferJson.IsOn(h.Services.Settings.ProjectTools, UnityProvider.KindId));

        row.IsOn = false;
        Assert.Equal("false", h.Services.Settings.ProjectTools.Value(UnityProvider.KindId, UnityProvider.PreferJsonKey));
        Assert.False(UnityProvider.PreferJson.IsOn(h.Services.Settings.ProjectTools, UnityProvider.KindId));

        // Back on, nothing's kept: it's the default again.
        row.IsOn = true;
        Assert.Null(h.Services.Settings.ProjectTools.Value(UnityProvider.KindId, UnityProvider.PreferJsonKey));
    }

    [Fact]
    public async Task A_project_type_switched_off_says_Off_in_the_sidebar_and_its_page_can_still_be_picked()
    {
        await using var h = new TabTestHarness(s => s.ProjectTools.Type(GodotProvider.KindId).Enabled = false);
        using var settings = new SettingsViewModel(h.Services, null);
        SettingsNavEntry Entry(string title) => settings.NavEntries.Single(e => !e.IsHeading && e.Title == title);

        // Off as the window opens.
        Assert.Equal("Off", Entry("Godot").Tag);
        Assert.Null(Entry("Unity").Tag);
        Assert.Null(Entry("All tools").Tag);

        // Switched off on its page: the tag comes at once, and the rest of the page is disabled.
        var unity = settings.ProjectType(UnityProvider.KindId);
        settings.SelectedNavEntry = Entry("Unity");
        unity.Switches[0].IsOn = false;
        Assert.False(h.Services.Settings.ProjectTools.IsEnabled(UnityProvider.KindId));
        Assert.Equal("Off", Entry("Unity").Tag);
        Assert.True(unity.Switches[0].IsAvailable);
        Assert.False(unity.Switches[1].IsAvailable);
        Assert.All(unity.Defaults, row => Assert.False(row.IsAvailable));

        // An Off page is still a page to pick.
        settings.SelectedNavEntry = Entry("Godot");
        Assert.Same(settings.ProjectType(GodotProvider.KindId), settings.CurrentPage);

        // Back on, by its switch or its section's Reset to defaults: the tag goes.
        unity.Switches[0].IsOn = true;
        Assert.Null(Entry("Unity").Tag);
        settings.ProjectType(GodotProvider.KindId).ResetAllSections();
        Assert.Null(Entry("Godot").Tag);
    }
}
