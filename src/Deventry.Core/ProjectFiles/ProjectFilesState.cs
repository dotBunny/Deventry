namespace Deventry.Core.ProjectFiles;

/// <summary>
/// A folder group's Project Files tab as it's saved (DESIGN.md §18, "Project files"): the files it has open, which
/// shows, and what the explorer had expanded.
/// </summary>
public sealed class ProjectFilesState
{
    /// <summary>Its id among the tabs, for the selection and the panes it's kept in.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The group's folder.</summary>
    public string Folder { get; set; } = "";

    /// <summary>The open files, in their tabs' order.</summary>
    public List<OpenFileState> Files { get; set; } = [];

    /// <summary>The full path of the file showing, if any.</summary>
    public string? ShowingPath { get; set; }

    /// <summary>The explorer's expanded folders, relative to <see cref="Folder"/>, with forward slashes.</summary>
    public List<string> ExpandedFolders { get; set; } = [];

    /// <summary>The explorer shows only the files Claude changed.</summary>
    public bool ShowOnlyChanged { get; set; }

    /// <summary>Changes are against git's HEAD rather than Claude's "before".</summary>
    public bool AgainstGitHead { get; set; }

    /// <summary>
    /// The explorer is hidden in this tab alone, whatever the others do: a conversation opened its first file beside it,
    /// so the file has the pane. Showing the explorer clears it.
    /// </summary>
    public bool ExplorerHidden { get; set; }
}

/// <summary>An open file's tab, as it's saved.</summary>
public sealed class OpenFileState
{
    public string Path { get; set; } = "";

    public bool IsPinned { get; set; }

    /// <summary>The preview tab, which the next file opened from the explorer replaces.</summary>
    public bool IsPreview { get; set; }

    /// <summary>The cursor, 1-based.</summary>
    public int Line { get; set; } = 1;

    public int Column { get; set; } = 1;

    /// <summary>How far down it was scrolled, in pixels.</summary>
    public double Scroll { get; set; }

    public FileViewMode Mode { get; set; } = FileViewMode.Edit;

    /// <summary>
    /// The file's own <b>Wrap lines</b>, once it's been turned on or off; null follows Settings → Files and diffs →
    /// Wrap long lines.
    /// </summary>
    public bool? WordWrap { get; set; }
}

/// <summary>
/// What an open file shows: the editor, its changes as §8's diff view does, or for a Markdown file, the file rendered in
/// the editor's place or beside it (DESIGN.md §18, "Markdown preview").
/// </summary>
public enum FileViewMode
{
    Edit,
    Inline,
    SideBySide,
    Preview,
    EditAndPreview,
}
