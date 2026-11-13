namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// One of a project's pages, in its own window (DESIGN.md §14, "The project's settings"). The pages show
/// <see cref="Project"/>, which holds what they edit. Nothing on them is a setting with a default: they have no
/// <b>Reset to defaults</b>.
/// </summary>
public abstract class ProjectPage(SettingsContext context, string title, ProjectSettingsViewModel project) : SettingsPage(context, title)
{
    public ProjectSettingsViewModel Project { get; } = project;
}

/// <summary>The project's <b>Links</b> (DESIGN.md §18, "Links"), from both of its files.</summary>
public sealed class ProjectLinksPage : ProjectPage
{
    public ProjectLinksPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, ProjectSettingsWindowViewModel.LinksPage, project) =>
        Section("links", "Links",
        [
            Info("project.links", "Web links for this project", keywords: ["add a link", "url", "bookmark", "deventry.json"]),
        ]);

    public override string IconKey => "IconSettingsLinks";

    public override string Description => "Web links in the project's menu in the sidebar; a click opens one in the browser.";
}

/// <summary>The project's <b>Tickets</b> (DESIGN.md §18, "Tickets"): where its tickets come from, from both of its files.</summary>
public sealed class ProjectTicketsPage : ProjectPage
{
    public ProjectTicketsPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, ProjectSettingsWindowViewModel.TicketsPage, project) =>
        Section("tickets", "Ticket sources",
        [
            Info("project.tickets", "Where the project's tickets come from", keywords: ["tickets", "issues", "github", "youtrack", "tracker", "repository", "deventry.json"]),
        ]);

    public override string IconKey => "IconTicket";

    public override string Description => "Where the project's Tickets tab reads its tickets from: GitHub repositories and YouTrack projects.";
}

/// <summary>The project's <b>Actions</b> (DESIGN.md §18, "Custom actions"), the editor that was in Tab settings….</summary>
public sealed class ProjectActionsPage : ProjectPage
{
    public ProjectActionsPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, ProjectSettingsWindowViewModel.ActionsPage, project) =>
        Section("actions", "Actions",
        [
            Info("project.actions", "Commands of your own", keywords: ["add an action", "project actions", "deventry.json", "command"]),
        ]);

    public override string IconKey => "IconSettingsActions";

    public override string Description => "Commands of your own for this folder, in the project's menu in the sidebar.";
}

/// <summary>
/// The project's <b>Solution</b> (DESIGN.md §18, "deventry.json"): the file that defines it, usually its solution, which
/// its menu offers to open, from either of its files.
/// </summary>
public sealed class ProjectSolutionPage : ProjectPage
{
    public ProjectSolutionPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, ProjectSettingsWindowViewModel.SolutionPage, project) =>
        Section("solution", "Solution",
        [
            Info("project.solution", "The project's solution", keywords: ["solution", "sln", "slnx", "workspace", "project file", "deventry.json"]),
        ]);

    public override string IconKey => "IconToolRead";

    public override string Description => "The file that defines the project, usually its solution, which the project's menu opens.";
}

/// <summary>
/// The project's <b>Group</b> (DESIGN.md §4, "Grouped by folder"): how its group looks in the sidebar, its color and the
/// picture in place of its dot, from either of its files.
/// </summary>
public sealed class ProjectGroupPage : ProjectPage
{
    public ProjectGroupPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, ProjectSettingsWindowViewModel.GroupPage, project) =>
        Section("group", "Group",
        [
            Info("project.group", "The group's color and icon", keywords: ["color", "colour", "icon", "picture", "image", "logo", "png", "ico", "dot", "deventry.json"]),
        ]);

    public override string IconKey => "IconImage";

    public override string Description => "How the project's group looks in the sidebar: its color, and a picture in place of its dot.";
}

/// <summary>
/// The project's <b>Tools</b> page: this machine's choices for it, as the project menus make them (DESIGN.md §18). Its
/// entries depend on the project's kind: "Engine folder", "Editor configuration".
/// </summary>
public sealed class ProjectToolChoicesPage : ProjectPage
{
    public ProjectToolChoicesPage(SettingsContext context, ProjectSettingsViewModel project) : base(context, ProjectSettingsWindowViewModel.ToolsPage, project) =>
        Section("tools", "This project on this computer",
        [
            Info("project.tools", "This project's engine, editor or Godot executable", keywords: ["choose", "engine folder", "unity editor", "editor configuration"]),
        ]);

    public override string IconKey => "IconSettingsTools";

    public override string Description => "This computer's choices for the project, as its menu makes them.";
}
