using Deventry.App.Conversation;

namespace Deventry.App.ViewModels.ToolWindows;

/// <summary>
/// The tool windows Deventry has (DESIGN.md §3, "Tool windows"), in the order their icons take on the strip at first:
/// the side panel's pages and Media at the right, Scratch pad, Processes and Games splitting it, and Find at the bottom.
/// A feature adds its kind to <see cref="All"/> and its view to <c>Views/ToolWindowView</c>.
/// </summary>
public static class ToolWindowKinds
{
    /// <summary>The files changed in the session (DESIGN.md §8).</summary>
    public static readonly ToolWindowKind Files = new()
    {
        Id = "Files",
        Title = "Changed files",
        Icon = "IconChangedFiles",
        PaletteText = "Show changed files",
        FollowsSession = true,
    };

    /// <summary>The agent map (DESIGN.md §18, "Agent map").</summary>
    public static readonly ToolWindowKind Agents = new()
    {
        Id = "Agents",
        Title = "Agents",
        Icon = "IconToolAgent",
        PaletteText = "Show the agent map",
        FollowsSession = true,
    };

    /// <summary>The project's details, for a tab with project tools (DESIGN.md §18, "Project tools").</summary>
    public static readonly ToolWindowKind Project = new()
    {
        Id = "Project",
        Title = "Project",
        Icon = "IconSettingsAllTools",
        FollowsSession = true,
        ShowsWhen = new(t => t.ProjectTools, nameof(ProjectToolsViewModel.HasTools), t => t.ProjectTools.HasTools),
    };

    /// <summary>The plan, the tasks and the Claude Docs, once the session has any (DESIGN.md §5, "Tasks").</summary>
    public static readonly ToolWindowKind Tasks = new()
    {
        Id = "Tasks",
        Title = "Tasks",
        Icon = "IconToolTodo",
        PaletteText = "Show tasks",
        FollowsSession = true,
        ShowsWhen = new(t => t.TodoList, nameof(TodoList.HasAnything), t => t.TodoList.HasAnything),
    };

    /// <summary>The session's MCP servers, when it has any (DESIGN.md §4, "MCP servers").</summary>
    public static readonly ToolWindowKind Mcp = new()
    {
        Id = "Mcp",
        Title = "MCP",
        Icon = "IconToolMcp",
        PaletteText = "Show MCP servers",
        FollowsSession = true,
        ShowsWhen = new(t => t, nameof(TabViewModel.HasMcpServers), t => t.HasMcpServers),
    };

    /// <summary>
    /// The images, video and sound in the session's conversation, the user's and the agent's, once it has any (DESIGN.md
    /// §18, "Media").
    /// </summary>
    public static readonly ToolWindowKind Media = new()
    {
        Id = "Media",
        Title = "Media",
        Icon = "IconMedia",
        PaletteText = "Show media",
        FollowsSession = true,
        ShowsWhen = new(t => t.Media, nameof(MediaGallery.HasAnything), t => t.Media.HasAnything),
    };

    /// <summary>The project's scratch pad (DESIGN.md §18, "Scratch pad").</summary>
    public static readonly ToolWindowKind ScratchPad = new()
    {
        Id = "ScratchPad",
        Title = "Scratch pad",
        Icon = "IconScratchPad",
        PaletteText = "Show scratch pad",
        DefaultGroup = ToolGroup.Split,
        FollowsSession = true,
    };

    /// <summary>The session's processes, while the process monitor is on (DESIGN.md §4, "Process monitor").</summary>
    public static readonly ToolWindowKind Processes = new()
    {
        Id = "Processes",
        Title = "Processes",
        Icon = "IconToolMonitor",
        PaletteText = "Show processes",
        DefaultGroup = ToolGroup.Split,
        FollowsSession = true,
        ShowsWhen = new(t => t.ProcessMonitor, nameof(ProcessMonitorViewModel.IsOn), t => t.ProcessMonitor.IsOn),
    };

    /// <summary>
    /// The game against Joulene, or the games to start one, while games are on (DESIGN.md §18, "Games with Joulene"). The
    /// app's, not a tab's: the shell gives it the games.
    /// </summary>
    public static readonly ToolWindowKind Games = new()
    {
        Id = "Games",
        Title = "Games",
        Icon = "IconGames",
        DefaultGroup = ToolGroup.Split,
    };

    /// <summary>
    /// The searches kept by Find in files' <b>Open in Find window</b> (DESIGN.md §18, "Finding in files"), a tab in its
    /// header for each, while there are any. The app's: the shell gives it its <see cref="FindWindowViewModel"/>.
    /// </summary>
    public static readonly ToolWindowKind Find = new()
    {
        Id = "Find",
        Title = "Find",
        Icon = "IconToolSearch",
        DefaultGroup = ToolGroup.Bottom,
    };

    /// <summary>Every kind, in the order their icons take on the strip at first.</summary>
    public static IReadOnlyList<ToolWindowKind> All { get; } = [Files, Agents, Project, Tasks, Mcp, Media, ScratchPad, Processes, Games, Find];
}
