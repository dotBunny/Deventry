using Deventry.App.ViewModels;
using Deventry.Core.ProjectTools;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.Tests;

/// <summary>
/// The menu of the project's icon on its group's row (DESIGN.md §18, "Project tools"), built from what the tab knows
/// about its project, without a tab: in sections under titles, each entry with its icon, and the folder's own actions and
/// links in submenus.
/// </summary>
public class ProjectMenuTests
{
    private static readonly ProjectMenuCommands Commands = new(
        new RelayCommand(() => { }), new RelayCommand(() => { }), new RelayCommand(() => { }), new RelayCommand(() => { }),
        new RelayCommand(() => { }), new RelayCommand(() => { }), new RelayCommand(() => { }), new RelayCommand(() => { }));

    /// <summary>Show in Explorer, Finder or the file manager, as the OS the tests run on names it.</summary>
    private static readonly string ShowFolder = ProjectMenu.ShowFolderLabel(ToolOSExtensions.Current);

    private const string Folder = "/work/NightOwl";

    private static readonly ProjectAction Build = new("build", "Build editor", ProjectActionKind.Run)
    {
        Description = "Builds the editor", Group = ProjectActionGroups.Build, Icon = MenuIcons.Build,
    };

    private static readonly ProjectAction Launch = new("launch", "Launch editor", ProjectActionKind.Launch) { Group = ProjectActionGroups.Editor, Icon = MenuIcons.Play };

    private static readonly ProjectAction Tests = new("custom:tests", "Run tests", ProjectActionKind.Run);

    private static readonly ProjectAction Solution = new("solution", "Open Tools.sln", ProjectActionKind.Open);

    private static readonly ProjectChoice Configuration = new("configuration", "Editor configuration",
        [new ProjectChoiceOption("Development", "Development"), new ProjectChoiceOption("DebugGame", "DebugGame")], "DebugGame");

    private static readonly ProjectFix ChooseEngine = new("engine", "Choose engine folder…", "Where is Unreal Engine?", PickFolder: true, path => (path, null));

    private static ProjectInfo Project(string path = "/work/NightOwl/NightOwl.uproject") => new()
    {
        Kind = "unreal",
        KindName = "Unreal Engine",
        Name = "NightOwl",
        Root = "/work/NightOwl",
        ProjectPath = path,
        Actions = [Build, Launch],
        Choice = Configuration,
        Fix = ChooseEngine,
    };

    private static IReadOnlyList<ProjectMenuEntry> Menu(
        ProjectInfo? project = null, IReadOnlyList<ProjectCandidate>? candidates = null, IReadOnlyList<ProjectAction>? custom = null,
        IReadOnlyList<ResolvedLink>? links = null, bool hasTools = false, Func<ProjectAction, ProjectAction>? forJob = null, bool hasRuns = false,
        ProjectAction? solution = null) =>
        ProjectMenu.Build(project, candidates ?? [], solution, custom ?? [], links ?? [], hasTools, hasRuns, forJob ?? (a => a), Folder, Commands);

    private static ProjectMenuEntry Submenu(IEnumerable<ProjectMenuEntry> menu, string label) => menu.Single(e => e.IsSubmenu && e.Label == label);

    private static string[] Labels(IEnumerable<ProjectMenuEntry> menu) => menu.Select(e => e.IsSeparator ? "-" : e.Label).ToArray();

    [Fact]
    public void Without_a_project_the_menu_has_only_its_own_commands()
    {
        var menu = Menu();

        Assert.Equal(["Folder", ShowFolder, "Refresh"], Labels(menu));
        Assert.True(menu[0].IsHeader);
        var commands = menu.Skip(1).ToArray();
        Assert.All(commands, e => Assert.Equal(ProjectMenuKind.Command, e.Kind));
        Assert.Equal([Commands.ShowFolder, Commands.Refresh], commands.Select(e => e.Command));
        Assert.Equal([Folder, "Look for the project and its files again"], commands.Select(e => e.Tip));
        Assert.Equal([MenuIcons.Folder, MenuIcons.Refresh], commands.Select(e => e.Icon));
    }

    [Fact]
    public void The_project_types_actions_by_what_they_do_and_its_choice_are_sections_and_the_folders_own_are_submenus()
    {
        var docs = new ResolvedLink("Docs", "https://example.com/docs", null, ProjectFileScope.Shared);
        var local = new ResolvedLink("Local file", null, "Only https, http and mailto links open.", ProjectFileScope.Local);

        var menu = Menu(Project(), custom: [Tests], links: [docs, local], hasTools: true);

        // Editor comes before Build, in the order of the project types' sections, whatever order the type lists its actions
        // in; the project's fix goes with the editor.
        Assert.Equal(
        [
            "Editor", "Launch editor", "Choose engine folder…",
            "-", "Build", "Build editor",
            "-", "Editor configuration", "Development", "DebugGame",
            "-", "Folder", "Actions", "Links", ShowFolder, "Show output…", "Refresh",
        ], Labels(menu));
        Assert.All(menu.Where(e => e.Label is "Editor" or "Build" or "Editor configuration" or "Folder"), e => Assert.True(e.IsHeader));

        var build = menu.Single(e => e.Label == "Build editor");
        Assert.Equal(ProjectMenuKind.Action, build.Kind);
        Assert.Same(Commands.RunAction, build.Command);
        Assert.Same(Build, build.Parameter);
        Assert.Equal("Builds the editor", build.Tip);
        // Each entry's icon: its own, else its kind's; a folder for the fix that picks one; the submenus' own.
        Assert.Equal(
            [MenuIcons.Play, MenuIcons.Folder, MenuIcons.Build, MenuIcons.Terminal, MenuIcons.Link, MenuIcons.Folder, MenuIcons.Log, MenuIcons.Refresh],
            menu.Where(e => (e.IsButton || e.IsSubmenu) && !e.IsOption).Select(e => e.Icon));

        // The folder's own actions and links are in submenus, those with no group with no title over them.
        var actions = Submenu(menu, "Actions");
        Assert.False(actions.IsButton);
        Assert.Null(actions.Command);
        Assert.Equal(["Run tests"], Labels(actions.Items));
        var tests = actions.Items.Single();
        Assert.Same(Tests, tests.Parameter);
        Assert.Equal(MenuIcons.Terminal, tests.Icon);
        var links = Submenu(menu, "Links");
        Assert.Equal(["Docs", "Local file"], Labels(links.Items));

        var (development, debugGame) = (menu.Single(e => e.Label == "Development"), menu.Single(e => e.Label == "DebugGame"));
        Assert.True(debugGame.IsOption);
        Assert.True(debugGame.IsChecked);
        Assert.Equal("●", debugGame.Mark);
        Assert.False(development.IsChecked);
        Assert.Equal("○", development.Mark);
        Assert.False(debugGame.HasIcon);
        Assert.Same(Commands.ChooseOption, debugGame.Command);
        Assert.Same(Configuration.Options[1], debugGame.Parameter);

        var fix = menu.Single(e => e.Label == "Choose engine folder…");
        Assert.Equal(ProjectMenuKind.Command, fix.Kind);
        Assert.Equal("Where is Unreal Engine?", fix.Tip);
        Assert.Same(Commands.Fix, fix.Command);

        var (docsEntry, localEntry) = (links.Items.Single(e => e.Label == "Docs"), links.Items.Single(e => e.Label == "Local file"));
        Assert.True(docsEntry.IsLink);
        Assert.True(docsEntry.IsEnabled);
        Assert.Equal("https://example.com/docs", docsEntry.Tip);
        Assert.Equal(MenuIcons.Link, docsEntry.Icon);
        Assert.Same(Commands.OpenLink, docsEntry.Command);
        Assert.Same(docs, docsEntry.Parameter);
        Assert.False(localEntry.IsEnabled);
        Assert.Equal("Only https, http and mailto links open.", localEntry.Tip);

        Assert.Same(Commands.ShowOutput, menu.Single(e => e.Label == "Show output…").Command);
    }

    [Fact]
    public void A_group_is_a_section_of_its_submenu_and_never_joins_the_project_types_own()
    {
        static ProjectAction Custom(string label, string? group, string? icon = null) =>
            new($"custom:{label}", label, ProjectActionKind.Run) { Group = group, Icon = icon };
        static ResolvedLink Link(string name, string? group, string? icon = null) =>
            new(name, $"https://example.com/{name}", null, ProjectFileScope.Shared, group, icon);

        var menu = Menu(
            Project(),
            custom: [Tests, Custom("Deploy staging", "Deploy", MenuIcons.Upload), Custom("Package", "build"), Custom("Deploy prod", "deploy")],
            links: [Link("Board", null), Link("Dashboard", "Deploy", MenuIcons.Web), Link("Wiki", "Docs", MenuIcons.Docs)],
            solution: Solution);

        // The project files' solution goes with the editor; "build" is a group of the folder's own, not the type's Build.
        Assert.Equal(
        [
            "Editor", "Launch editor", "Open Tools.sln", "Choose engine folder…",
            "-", "Build", "Build editor",
            "-", "Editor configuration", "Development", "DebugGame",
            "-", "Folder", "Actions", "Links", ShowFolder, "Refresh",
        ], Labels(menu));
        Assert.Same(Solution, menu.Single(e => e.Label == "Open Tools.sln").Parameter);
        // Those with no group first, untitled; then each group under the name it first comes with, found whatever its case,
        // in the order it first comes. An action's group and a link's of the same name are each in their own submenu.
        var actions = Submenu(menu, "Actions").Items;
        Assert.Equal(["Run tests", "-", "Deploy", "Deploy staging", "Deploy prod", "-", "build", "Package"], Labels(actions));
        Assert.True(actions.Single(e => e.Label == "Deploy").IsHeader);
        Assert.Equal([MenuIcons.Upload, MenuIcons.Terminal], actions.Where(e => e.Label.StartsWith("Deploy ", StringComparison.Ordinal)).Select(e => e.Icon));
        Assert.Equal(["Board", "-", "Deploy", "Dashboard", "-", "Docs", "Wiki"], Labels(Submenu(menu, "Links").Items));

        // A submenu whose entries all have groups starts with the first one's title.
        Assert.Equal(["Deploy", "Dashboard"], Labels(Submenu(Menu(links: [Link("Dashboard", "Deploy")]), "Links").Items));
        // In a folder without a project type, the solution is the editor's section alone.
        Assert.Equal(["Editor", "Open Tools.sln", "-", "Folder", ShowFolder, "Refresh"], Labels(Menu(solution: Solution)));
        // A project type's action with no section of its own goes under the type's name.
        var plain = Project() with { Actions = [new ProjectAction("odd", "Odd one", ProjectActionKind.Open)], Choice = null, Fix = null };
        Assert.Equal(["Unreal Engine", "Odd one", "-", "Folder", ShowFolder, "Refresh"], Labels(Menu(plain)));
    }

    [Fact]
    public void Show_output_waits_for_something_to_have_run()
    {
        var before = Menu(Project(), hasTools: true).Single(e => e.Label == "Show output…");
        var after = Menu(Project(), hasTools: true, hasRuns: true).Single(e => e.Label == "Show output…");

        Assert.Equal((false, "No action has run in this tab yet"), (before.IsEnabled, before.Tip));
        Assert.Equal((true, "The newest action's log"), (after.IsEnabled, after.Tip));
    }

    [Fact]
    public void A_folder_with_several_projects_offers_them_with_the_one_in_use_checked()
    {
        var game = new ProjectCandidate("unreal", "/work/NightOwl/NightOwl.uproject", "NightOwl");
        var tool = new ProjectCandidate("godot", "/work/NightOwl/Tools/project.godot", "Level tool");

        var menu = Menu(Project(game.Path), candidates: [game, tool], hasTools: true);

        Assert.Equal(["Projects in this folder", "NightOwl", "Level tool", "-", "Editor"], Labels(menu).Take(5));
        var (gameEntry, toolEntry) = (menu.Single(e => e.Label == "NightOwl"), menu.Single(e => e.Label == "Level tool"));
        Assert.True(gameEntry.IsChecked);
        Assert.False(toolEntry.IsChecked);
        Assert.Equal(tool.Path, toolEntry.Tip);
        Assert.Same(Commands.ChooseProject, toolEntry.Command);
        Assert.Same(tool, toolEntry.Parameter);

        // A single project is no choice.
        Assert.DoesNotContain("Projects in this folder", Labels(Menu(Project(game.Path), candidates: [game])));
    }

    [Fact]
    public void Separators_only_fall_between_sections_that_are_there()
    {
        var candidates = new[] { new ProjectCandidate("unreal", "/work/a/A.uproject", "A"), new ProjectCandidate("unreal", "/work/b/B.uproject", "B") };

        // None of the projects could be read: no actions, choice or fix between the choice of project and the menu's own.
        Assert.Equal(["Projects in this folder", "A", "B", "-", "Folder", ShowFolder, "Refresh"], Labels(Menu(candidates: candidates)));
        // Only the folder's own actions: no separator before them.
        Assert.Equal(["Folder", "Actions", ShowFolder, "Show output…", "Refresh"], Labels(Menu(custom: [Tests], hasTools: true)));
    }

    [Fact]
    public void The_folder_shows_in_each_OSs_own_file_manager()
    {
        Assert.Equal("Show in Explorer", ProjectMenu.ShowFolderLabel(ToolOS.Windows));
        Assert.Equal("Show in Finder", ProjectMenu.ShowFolderLabel(ToolOS.MacOS));
        Assert.Equal("Show in file manager", ProjectMenu.ShowFolderLabel(ToolOS.Linux));
    }

    [Fact]
    public void Actions_show_as_the_running_job_leaves_them()
    {
        static ProjectAction WhileBuilding(ProjectAction action) =>
            action.Kind == ProjectActionKind.Run ? action with { DisabledReason = "Build editor is still running." } : action;

        var menu = Menu(Project(), custom: [Tests], forJob: WhileBuilding);

        var (build, launch, tests) = (menu.Single(e => e.Label == "Build editor"), menu.Single(e => e.Label == "Launch editor"), Submenu(menu, "Actions").Items.Single());
        Assert.False(build.IsEnabled);
        Assert.Equal("Build editor is still running.", build.Tip);
        Assert.False(Assert.IsType<ProjectAction>(build.Parameter).IsEnabled);
        Assert.False(tests.IsEnabled);
        Assert.True(launch.IsEnabled);
        Assert.Same(Launch, launch.Parameter);
    }
}
