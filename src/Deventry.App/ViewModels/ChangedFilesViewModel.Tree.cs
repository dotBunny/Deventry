using System.Collections.ObjectModel;
using Deventry.Core.Diffs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>A row the Changed files panel shows: a file, or in the tree a folder (DESIGN.md §8, "The tree").</summary>
public abstract partial class ChangedTreeRow : ObservableObject
{
    /// <summary>How far in a level of the tree is drawn.</summary>
    public const double TreeLevel = 16;

    /// <summary>The room a folder's arrow takes, before its box; a file in the tree leaves it, so the boxes of one level line up.</summary>
    public const double ArrowWidth = 14;

    /// <summary>How deep in the tree it is: 0 at its top, and in the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Indent))]
    public partial int Depth { get; set; }

    /// <summary>How far in it's drawn.</summary>
    public abstract double Indent { get; }
}

/// <summary>
/// A folder in Changed files' tree (DESIGN.md §8, "The tree"): a chain of folders that each hold only the next, or a
/// working tree's heading when the session changed files in more than one. Kept from one listing to the next by its
/// <see cref="Key"/>, so its control and its place in the list stay.
/// </summary>
public sealed partial class ChangedFolderRow : ChangedTreeRow
{
    internal ChangedFolderRow(string key, string path)
    {
        Key = key;
        Path = path;
    }

    /// <summary>What its collapse is remembered by: its full path, or for a working tree's heading <c>worktree:</c> and its path.</summary>
    public string Key { get; }

    /// <summary>The folder it holds the files of, for <b>Reveal in file manager</b> and <b>Copy path</b>, and its tip.</summary>
    public string Path { get; }

    /// <summary>Its name, <i>Source/Starfall</i>, or a working tree's heading, <i>"brisk-otter · worktree-brisk-otter"</i>.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>A working tree's heading, at the top of the files changed in it.</summary>
    [ObservableProperty]
    public partial bool IsWorkTree { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    public partial bool IsExpanded { get; set; } = true;

    public bool IsCollapsed => !IsExpanded;

    /// <summary>How many files are in it, at any depth: <i>"3 files"</i>.</summary>
    [ObservableProperty]
    public partial string FilesText { get; set; } = "";

    /// <summary>Its files' counts added up, <c>+87 −1</c>; null when none of them is counted.</summary>
    [ObservableProperty]
    public partial string? Stats { get; set; }

    /// <summary>Every file in it is ticked as reviewed (true), none is (false), or some are (null, drawn partly ticked).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllReviewed))]
    public partial bool? IsReviewed { get; set; }

    /// <summary>Every file in it is ticked: it's drawn faintly, as a reviewed file is.</summary>
    public bool IsAllReviewed => IsReviewed == true;

    public override double Indent => Depth * TreeLevel;

    /// <summary>Every file in it, at any depth, in the tree's order: what its box ticks.</summary>
    internal IReadOnlyList<ChangedFileRow> Files { get; set; } = [];

    /// <summary>The keys of the folders under it, at any depth, shown or not: what Alt+click on it folds along with it.</summary>
    internal IReadOnlyList<string> KeysUnder { get; set; } = [];

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() =>
        $"{Name}, folder: {FilesText}{(Stats is null ? "" : $", {Stats}")}, {(IsExpanded ? "expanded" : "collapsed")}";
}

/// <summary>
/// The tree of Changed files (DESIGN.md §8, "The tree"): the files by their folders, each folder folding, or the list as
/// it was, as its switch says for every tab. <see cref="ChangedFilesViewModel.Files"/> stays every file; <see cref="Rows"/>
/// is what's shown.
/// </summary>
public sealed partial class ChangedFilesViewModel
{
    /// <summary>The most collapsed folders a tab remembers; the oldest go first.</summary>
    internal const int MaxCollapsedFolders = 500;

    private const string WorkTreeKey = "worktree:";

    /// <summary>What the panel shows, top to bottom: in the tree, each folder's row and, while it's expanded, what's in it; in the list, <see cref="Files"/>.</summary>
    public ObservableCollection<ChangedTreeRow> Rows { get; } = [];

    /// <summary>The panel shows a tree of the files' folders (the default), rather than the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewSwitchText))]
    public partial bool ShowAsTree { get; private set; } = true;

    /// <summary>The switch's tip: what it does.</summary>
    public string ViewSwitchText => ShowAsTree ? "Show as a list" : "Show as a tree";

    /// <summary>The tree has folders, for <b>Expand all</b> and <b>Collapse all</b>.</summary>
    [ObservableProperty]
    public partial bool HasFolders { get; private set; }

    /// <summary>The folder rows of the tree as last shown, by key, so the next listing keeps them.</summary>
    private Dictionary<string, ChangedFolderRow> _folderRows = new(ChangedFiles.PlatformPathComparer);

    /// <summary>Every folder row of the tree as last shown, for their boxes.</summary>
    private List<ChangedFolderRow> _shownFolders = [];

    /// <summary>Every folder's key in the tree as last shown, under collapsed ones too: what <b>Expand all</b> and <b>Collapse all</b> fold.</summary>
    private List<string> _treeKeys = [];

    /// <summary>The tab's collapsed folders (<see cref="Core.Settings.TabState.CollapsedChangedFolders"/>), to look up.</summary>
    private HashSet<string> Collapsed => _collapsed ??= new(_host.State.CollapsedChangedFolders, ChangedFiles.PlatformPathComparer);

    private HashSet<string>? _collapsed;

    /// <summary>
    /// Shows <see cref="Files"/> as the switch says: in the tree, by working tree when there's more than one, then by
    /// folder, a collapsed folder's files left out; or as the list. The rows already shown stay.
    /// </summary>
    private void ShowRows()
    {
        ShowAsTree = !_services.State.ChangedFilesAsList;
        var shown = new List<ChangedTreeRow>(Files.Count);
        var folders = new Dictionary<string, ChangedFolderRow>(ChangedFiles.PlatformPathComparer);
        var made = new List<ChangedFolderRow>();
        var keys = new List<string>();
        if (!ShowAsTree)
        {
            foreach (var file in Files)
            {
                file.InTree = false;
                file.Depth = 0;
                shown.Add(file);
            }
        }
        else
        {
            // The working trees in the list's order, the tab's own first; headed only when there's more than one.
            var headed = Files.Any(f => f.Group is not null);
            foreach (var group in Files.GroupBy(f => f.WorkTree ?? "", ChangedFiles.PlatformPathComparer))
            {
                var root = group.Key.Length == 0 ? _host.Folder : group.Key;
                var tree = ChangedFileTree.Build(group, f => f.Path, f => f.DisplayPath, root);
                var depth = 0;
                if (headed)
                {
                    var heading = Folder(WorkTreeKey + root, root, group.First().Group ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), 0, tree);
                    heading.IsWorkTree = true;
                    shown.Add(heading);
                    depth = 1;
                    if (!heading.IsExpanded)
                    {
                        continue;
                    }
                }
                Add(tree, depth);
            }
        }
        _folderRows = folders;
        _shownFolders = made;
        _treeKeys = keys;
        HasFolders = made.Count > 0;
        UpdateFolderReviewed();
        SyncShown(shown);

        void Add(ChangedFolder<ChangedFileRow> folder, int depth)
        {
            foreach (var sub in folder.Folders)
            {
                var row = Folder(sub.Path, sub.Path, sub.Name, depth, sub);
                shown.Add(row);
                if (row.IsExpanded)
                {
                    Add(sub, depth + 1);
                }
            }
            foreach (var file in folder.Files)
            {
                file.InTree = true;
                file.Depth = depth;
                shown.Add(file);
            }
        }

        ChangedFolderRow Folder(string key, string path, string name, int depth, ChangedFolder<ChangedFileRow> node)
        {
            // A key that came up already this time (two folders that are one, as far as the paths go) gets a row of its own.
            var row = !folders.ContainsKey(key) && _folderRows.TryGetValue(key, out var kept) ? kept : new ChangedFolderRow(key, path);
            var files = node.AllFiles().ToList();
            row.Name = name;
            row.Depth = depth;
            row.Files = files;
            row.KeysUnder = [.. node.AllFolders().Skip(1).Select(f => f.Path)];
            row.FilesText = files.Count == 1 ? "1 file" : $"{files.Count} files";
            row.Stats = files.Any(f => f.Added is not null && f.Removed is not null)
                ? $"+{files.Sum(f => f.Added ?? 0)} −{files.Sum(f => f.Removed ?? 0)}"
                : null;
            row.IsExpanded = !Collapsed.Contains(key);
            folders.TryAdd(key, row);
            made.Add(row);
            keys.Add(key);
            if (!row.IsExpanded)
            {
                // Its folders aren't shown, but Expand all and Collapse all fold them too.
                keys.AddRange(row.KeysUnder);
            }
            return row;
        }
    }

    /// <summary>
    /// Brings <see cref="Rows"/> in line with <paramref name="shown"/>, taking out, putting in and moving only the rows
    /// that differ, so expanding a folder adds its rows under it rather than rebuilding every row's controls.
    /// </summary>
    private void SyncShown(List<ChangedTreeRow> shown)
    {
        var wanted = new HashSet<ChangedTreeRow>(shown, ReferenceEqualityComparer.Instance);
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Rows[i]))
            {
                Rows.RemoveAt(i);
            }
        }
        var present = new HashSet<ChangedTreeRow>(Rows, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < shown.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], shown[i]))
            {
                continue;
            }
            if (present.Contains(shown[i]) && IndexOf(shown[i], i + 1) is >= 0 and var at)
            {
                Rows.Move(at, i);
            }
            else
            {
                Rows.Insert(i, shown[i]);
            }
        }

        int IndexOf(ChangedTreeRow row, int from)
        {
            for (var i = from; i < Rows.Count; i++)
            {
                if (ReferenceEquals(Rows[i], row))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>Each folder's box: ticked when every file in it is, partly when some are.</summary>
    private void UpdateFolderReviewed()
    {
        foreach (var folder in _shownFolders)
        {
            var reviewed = folder.Files.Count(f => f.IsReviewed);
            folder.IsReviewed = reviewed == 0 ? false : reviewed == folder.Files.Count ? true : null;
        }
    }

    // ---- Folding -------------------------------------------------------------------------------------------------------

    /// <summary>A click on a folder's row, or Right or Left on it: shows what's in it, or hides it.</summary>
    [RelayCommand]
    private void ToggleFolder(ChangedFolderRow? folder)
    {
        if (folder is not null)
        {
            Fold([folder.Key], expand: !folder.IsExpanded);
        }
    }

    /// <summary>Alt+click on a folder's row: it and every folder under it, shown or not, expand or collapse with it.</summary>
    internal void ToggleFolderAllLevels(ChangedFolderRow folder) => Fold([folder.Key, .. folder.KeysUnder], expand: !folder.IsExpanded);

    /// <summary>Expands a folder and every folder under it: its menu's <b>Expand all</b>.</summary>
    [RelayCommand]
    private void ExpandFolderAll(ChangedFolderRow? folder)
    {
        if (folder is not null)
        {
            Fold([folder.Key, .. folder.KeysUnder], expand: true);
        }
    }

    /// <summary>The panel's <b>Expand all</b>: every folder in the tree.</summary>
    [RelayCommand]
    private void ExpandAll() => Fold(_treeKeys, expand: true);

    /// <summary>The panel's <b>Collapse all</b>: every folder in the tree, at every level, so expanding one shows its folders collapsed.</summary>
    [RelayCommand]
    private void CollapseAll() => Fold(_treeKeys, expand: false);

    /// <summary>Expands or collapses the folders <paramref name="keys"/> name, remembers which are collapsed with the tab, and shows the tree again.</summary>
    private void Fold(IReadOnlyList<string> keys, bool expand)
    {
        var collapsed = _host.State.CollapsedChangedFolders;
        var changed = false;
        foreach (var key in keys)
        {
            if (expand ? Collapsed.Remove(key) : Collapsed.Add(key))
            {
                changed = true;
                if (expand)
                {
                    collapsed.RemoveAll(k => ChangedFiles.PlatformPathComparer.Equals(k, key));
                }
                else
                {
                    collapsed.Add(key);
                }
            }
        }
        if (!changed)
        {
            return;
        }
        // A tab that went on for long remembers only its latest.
        if (collapsed.Count > MaxCollapsedFolders)
        {
            foreach (var key in collapsed.Take(collapsed.Count - MaxCollapsedFolders))
            {
                Collapsed.Remove(key);
            }
            collapsed.RemoveRange(0, collapsed.Count - MaxCollapsedFolders);
        }
        _services.SaveState();
        ShowRows();
    }

    // ---- The switch -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The panel's switch: the tree or the list, for every tab, remembered. The other tabs follow as Changed files next
    /// shows theirs, since it lists them again then.
    /// </summary>
    [RelayCommand]
    private void ToggleView()
    {
        _services.State.ChangedFilesAsList = ShowAsTree;
        _services.SaveState();
        ShowRows();
    }

    // ---- A folder's box and menu ----------------------------------------------------------------------------------------

    /// <summary>A folder's box: ticks every file in it as reviewed, or, when they all are, unticks them.</summary>
    [RelayCommand]
    private void ToggleFolderReviewed(ChangedFolderRow? folder)
    {
        if (folder is { Files.Count: > 0 })
        {
            SetFilesReviewed([.. folder.Files.Select(f => (f.Path, f.LatestChange))], reviewed: folder.IsReviewed != true);
        }
    }

    [RelayCommand]
    private Task RevealFolderAsync(ChangedFolderRow? folder) =>
        folder is not null && Directory.Exists(folder.Path) ? _services.Platform.RevealFolderAsync(folder.Path) : Task.CompletedTask;

    [RelayCommand]
    private Task CopyFolderPathAsync(ChangedFolderRow? folder) =>
        folder is null ? Task.CompletedTask : _services.Platform.SetClipboardTextAsync(folder.Path);
}
