namespace Deventry.Core.ProjectTools;

/// <summary>
/// The icons an entry of the project's menu can have (DESIGN.md §18, "deventry.json"): what <c>"icon"</c> on an action
/// or a link names, and what the project types give their own actions. The app draws each one.
/// </summary>
public static class MenuIcons
{
    public const string Play = "play";
    public const string Build = "build";
    public const string Test = "test";
    public const string Terminal = "terminal";
    public const string File = "file";
    public const string Files = "files";
    public const string Folder = "folder";
    public const string Log = "log";
    public const string Link = "link";
    public const string Web = "web";
    public const string Docs = "docs";
    public const string Refresh = "refresh";
    public const string Upload = "upload";
    public const string Download = "download";
    public const string Package = "package";
    public const string Trash = "trash";
    public const string Stop = "stop";
    public const string Settings = "settings";
    public const string Bug = "bug";
    public const string Branch = "branch";
    public const string Check = "check";

    /// <summary>Every name, in the order the editors offer them.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        Play, Build, Test, Terminal, File, Files, Folder, Log, Link, Web, Docs, Refresh, Upload, Download, Package, Trash, Stop,
        Settings, Bug, Branch, Check,
    ];

    /// <summary>The name as Deventry has it, whatever its case; null when it isn't one.</summary>
    public static string? Find(string? name) =>
        name is null ? null : Names.FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>An action's icon when it names none: by what it does.</summary>
    public static string For(ProjectActionKind kind) => kind switch
    {
        ProjectActionKind.Launch => Play,
        ProjectActionKind.Open => File,
        ProjectActionKind.Destructive => Trash,
        _ => Terminal,
    };
}

/// <summary>
/// The sections of the project's menu that the project types' own actions go in (DESIGN.md §18, "Project tools"). A
/// folder's own actions and links are in submenus of their own, so a <c>"group"</c> of one of these names is a section
/// there, apart from these.
/// </summary>
public static class ProjectActionGroups
{
    /// <summary>Opening the project: the editor, the solution, the logs.</summary>
    public const string Editor = "Editor";

    /// <summary>Building and testing it.</summary>
    public const string Build = "Build";

    /// <summary>Deleting what it made, and ending its editors.</summary>
    public const string CleanUp = "Clean up";

    /// <summary>The built-in sections, in the menu's order.</summary>
    public static IReadOnlyList<string> All { get; } = [Editor, Build, CleanUp];
}
