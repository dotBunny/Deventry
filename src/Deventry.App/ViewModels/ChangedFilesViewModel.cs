using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Diffs;
using Deventry.App.Services;
using Deventry.Core.Diffs;
using Deventry.Core.Git;
using Deventry.Core.ProjectFiles;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>One file in the Changed files panel (DESIGN.md §8).</summary>
public sealed partial class ChangedFileRow : ChangedTreeRow
{
    public required string Path { get; init; }

    public required string DisplayPath { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => (Stats is null ? $"{DisplayPath}: {StatusText}" : $"{DisplayPath}: {StatusText}, {Stats}") + (Note is null ? "" : $". {Note}");

    /// <summary>A, M, D or R.</summary>
    public required string Status { get; init; }

    public required string StatusText { get; init; }

    public string? Stats { get; init; }

    /// <summary>The lines added, when they're counted: what its folders' counts add up (DESIGN.md §8, "The tree").</summary>
    public int? Added { get; init; }

    /// <summary>The lines removed, when they're counted.</summary>
    public int? Removed { get; init; }

    /// <summary>
    /// It's in the tree, under its folder's row (DESIGN.md §8, "The tree"): its path isn't under its name, and its working
    /// tree's heading is a row of its own.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Indent), nameof(ShowsGroup))]
    public partial bool InTree { get; set; }

    /// <summary>In the tree, past its folder's arrow, as far as its folder's row is in.</summary>
    public override double Indent => InTree ? Depth * TreeLevel + ArrowWidth : 0;

    /// <summary>The list heads its working tree's group on this row.</summary>
    public bool ShowsGroup => Group is not null && !InTree;

    /// <summary>
    /// What changed that the lines don't show, for the line under the row's folder: <i>"Only its line endings changed:
    /// CRLF to LF"</i> (DESIGN.md §8, "What the lines don't show"). Null when nothing did.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>What <see cref="Note"/> says, which a git row's diff says too: git's copy at HEAD comes without its line endings.</summary>
    public HiddenChanges? Hidden { get; init; }

    /// <summary>The line endings the file had before Claude's first change (<see cref="ChangedFile.BeforeLineEndings"/>).</summary>
    public LineEndingKind? BeforeLineEndings { get; init; }

    public bool IsAdded => Status is "A" or "?";

    public bool IsDeleted => Status == "D";

    /// <summary>The file isn't there now: deleted, or gone since Claude's latest change, which left it there.</summary>
    public bool IsMissing => IsDeleted || IsGone;

    /// <summary>Gone since the session's latest change to it, which left it there (DESIGN.md §8, "This session's changes").</summary>
    public bool IsGone { get; init; }

    /// <summary>
    /// The working tree it's in is gone (DESIGN.md §8, "Working trees"): the row, and its diff, are the session's changes
    /// as they were, from <see cref="Before"/> to <see cref="After"/>.
    /// </summary>
    public bool InGoneWorkTree { get; init; }

    /// <summary>
    /// The "before" side: the file before Claude's first change, or null for a new file or when it's unknown. Git rows
    /// load it from HEAD.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>False when what the file held before Claude's first change isn't known (DESIGN.md §8, "Before content").</summary>
    public bool BeforeKnown { get; init; } = true;

    /// <summary>
    /// The file as the session's latest change left it, where its diff ends (DESIGN.md §8, "This session's changes"),
    /// when <see cref="AfterKnown"/>; otherwise the diff ends at the file now.
    /// </summary>
    public string? After { get; init; }

    public bool AfterKnown { get; init; }

    /// <summary>
    /// What changed in the file since the session's latest change, by whom when it was another tab's Claude, for the
    /// line under the row's folder: <i>"Changed since: +3 −1, by Claude in Fix login"</i>. Null when nothing has.
    /// </summary>
    public string? Since { get; set; }

    public bool FromGit { get; init; }

    /// <summary>
    /// The top of the working tree the file is in, when it isn't the tab's own (DESIGN.md §8, "Working trees"): its
    /// path is shown from there, and git is asked there.
    /// </summary>
    public string? WorkTree { get; init; }

    /// <summary>On the first row of each working tree's group, when there's more than one: its heading.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// The id of Claude's latest change to the file when the row was made, or null when Claude hasn't changed it: what
    /// the row's checkbox marks as reviewed.
    /// </summary>
    public string? LatestChange { get; set; }

    /// <summary>The user marked the file as reviewed, and Claude hasn't changed it since (DESIGN.md §8, "Reviewed").</summary>
    [ObservableProperty]
    public partial bool IsReviewed { get; set; }

    /// <summary>Shows the same as <paramref name="other"/>, so the list can keep this row.</summary>
    public bool SameAs(ChangedFileRow other) =>
        Path == other.Path && DisplayPath == other.DisplayPath && Status == other.Status && StatusText == other.StatusText
        && Stats == other.Stats && Added == other.Added && Removed == other.Removed && Note == other.Note && BeforeLineEndings == other.BeforeLineEndings && ReferenceEquals(Before, other.Before) && BeforeKnown == other.BeforeKnown && FromGit == other.FromGit
        && LatestChange == other.LatestChange && IsReviewed == other.IsReviewed
        && ReferenceEquals(After, other.After) && AfterKnown == other.AfterKnown && Since == other.Since && IsGone == other.IsGone
        && InGoneWorkTree == other.InGoneWorkTree && WorkTree == other.WorkTree && Group == other.Group;
}

/// <summary>What the changed files panel needs from its tab.</summary>
internal interface IChangedFilesHost : ITabAreaHost
{
    /// <summary>The tab can copy its session to the library now (DESIGN.md §9, "Writing").</summary>
    bool CanSyncNow { get; }

    /// <summary>The tab is the one showing: a background tab's rows wait until it is (DESIGN.md §8).</summary>
    bool IsSelected { get; }

    /// <summary>Copies the session to the library in the background, for a tab that syncs.</summary>
    void CopyToLibrary();

    /// <summary><see cref="ChangedFilesViewModel.AllReviewed"/> changed: the tab's row shows the reviewed icon, or its mark again.</summary>
    void AllReviewedChanged();

    /// <summary>Opens a file, in the project's Files tab or the OS's app, as Settings → Files and diffs says (DESIGN.md §18, "Project files").</summary>
    Task<OpenedPath> OpenPathAsync(string path);

    /// <summary>
    /// The other tab whose Claude changed <paramref name="path"/> after <paramref name="since"/>, by name, or null
    /// when none did (DESIGN.md §8, "This session's changes").
    /// </summary>
    string? ChangedSinceBy(string path, DateTimeOffset since);

    /// <summary>The session's Claude changed <paramref name="path"/>: the other tabs listing it look again.</summary>
    void ClaudeChangedFile(string path);

    /// <summary>The session's Claude created <paramref name="path"/>: Perforce marks it for add (DESIGN.md §18, "Opening files in Perforce").</summary>
    void ClaudeCreatedFile(string path);
}

/// <summary>
/// The tab's changed files and diffs (DESIGN.md §8), with partials for the working trees they're in
/// (<c>.WorkTrees.cs</c>) and the tree the panel shows them in (<c>.Tree.cs</c>).
/// </summary>
public sealed partial class ChangedFilesViewModel : ViewModelBase
{
    /// <summary>How long a tab that syncs waits after a tick before writing its record, so ticking several files writes it once.</summary>
    internal static readonly TimeSpan ReviewSyncDelay = TimeSpan.FromSeconds(2);

    private readonly AppServices _services;
    private readonly IChangedFilesHost _host;
    private ChangedFiles? _changes;
    private ReviewedFiles? _reviewed;
    /// <summary>Writes the library record once the ticking stops, for a tab that syncs.</summary>
    private UiTimeout ReviewSync => field ??= new(_services.Time, _services.Dispatcher);
    private bool _reviewSyncStopped;
    private bool _refreshQueued;

    /// <summary>
    /// Each changed file's inspection (reading it and diffing it against its "before"), kept until the file on disk or
    /// what's known of its "before" changes, so a refresh after one edit doesn't read and diff every file again.
    /// </summary>
    private readonly ConcurrentDictionary<string, (InspectionKey Key, ChangedFileState State)> _inspections = new(StringComparer.Ordinal);

    private readonly record struct InspectionKey(bool Exists, long Length, DateTime LastWriteUtc, string? Before, bool BeforeKnown, bool IsNew, string? After, bool AfterKnown, bool TreeGone);
    /// <summary>Counts refreshes started, so one that finishes after a later one leaves that one's newer rows alone.</summary>
    private int _refreshGeneration;

    /// <summary>
    /// How long git mode waits after a change before running git: an edit at a time, a turn's dozen edits would each
    /// start their own <c>git status</c> and <c>git diff</c>.
    /// </summary>
    internal static readonly TimeSpan GitRefreshDelay = TimeSpan.FromMilliseconds(500);

    private UiTimeout GitRefreshWait => field ??= new(_services.Time, _services.Dispatcher);

    /// <summary>The git listing under way; a newer refresh stops it.</summary>
    private CancellationTokenSource? _gitRefresh;

    internal ChangedFilesViewModel(AppServices services, IChangedFilesHost host)
    {
        _services = services;
        _host = host;
        // As saved, until the session's changes are read back (ChangesRead).
        AllReviewed = host.State.AllFilesReviewed;
        ShowAsTree = !services.State.ChangedFilesAsList;
    }

    private ChangedFiles Changes
    {
        get
        {
            if (_changes is null)
            {
                _changes = new ChangedFiles(_services.Time) { Befores = new BeforeContentStore(_services.Paths.BeforeContentDirectory, _services.Time) };
                _changes.Changed += QueueRefresh;
                _changes.Changed += QueueAllReviewed;
                _changes.Changed += OnClaudeChanged;
            }
            return _changes;
        }
    }

    private void OnClaudeChanged() => ClaudeChangesUpdated?.Invoke();

    // ---- For the project's Project Files tab (DESIGN.md §18, "Project files", "Claude's changes") ---------------------

    /// <summary>The files Claude changed in the session, each with its "before".</summary>
    internal IReadOnlyList<ChangedFile> ClaudeChanges => Changes.Files;

    /// <summary>Claude changed a file, or the changes were read back again.</summary>
    internal event Action? ClaudeChangesUpdated;

    /// <summary>A file was ticked as reviewed, or unticked.</summary>
    internal event Action? ReviewedChanged;

    /// <summary>Whether Claude changed <paramref name="path"/> in the session.</summary>
    internal bool HasClaudeChange(string path) => Changes.Find(path) is not null;

    /// <summary>When Claude last changed <paramref name="path"/> in the session, or null when it hasn't.</summary>
    internal DateTimeOffset? LastClaudeChange(string path) => _changes?.Find(path)?.LastChanged;

    /// <summary>Whether <paramref name="path"/> is ticked as reviewed, as of Claude's latest change to it.</summary>
    internal bool IsFileReviewed(string path) => Reviewed.IsReviewed(path, Changes.Find(path));

    /// <summary>Ticks <paramref name="path"/> as reviewed at Claude's latest change to it, or unticks it: the Files tab's <b>Reviewed</b>.</summary>
    internal void SetFileReviewedFromProject(string path, bool reviewed) => SetFileReviewed(path, reviewed, ReviewedFiles.LatestChange(Changes.Find(path)));

    /// <summary>The files marked as reviewed, kept in the tab's state (DESIGN.md §8, "Reviewed").</summary>
    private ReviewedFiles Reviewed => _reviewed ??= new ReviewedFiles(_host.State.ReviewedFiles);

    public ObservableCollection<ChangedFileRow> Files { get; } = [];

    /// <summary>For example "5 files changed · 2 reviewed".</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "No changes yet";

    /// <summary>"Working tree vs HEAD": all changes in the repository, including ones made by commands or by you.</summary>
    [ObservableProperty]
    public partial bool ShowGitChanges { get; set; }

    partial void OnShowGitChangesChanged(bool value)
    {
        _ = RefreshAsync();
        UpdateWatches();
    }

    public bool IsGitRepository => GitInfo.TryGetBranch(_host.Folder) is not null;

    /// <summary>
    /// The tab moved to another folder, which may or may not be a repository: the rows' paths are shown from there,
    /// and the git view lists that folder's working tree, or goes back to the session's changes outside a repository.
    /// </summary>
    internal void OnFolderChanged()
    {
        OnPropertyChanged(nameof(IsGitRepository));
        _inspections.Clear();
        UpdateWatches();
        if (ShowGitChanges && !IsGitRepository)
        {
            ShowGitChanges = false; // Refreshes.
            return;
        }
        QueueRefresh();
    }

    /// <summary>
    /// Something other than this tab's Claude may have changed the files: a turn ended (its commands, a commit),
    /// Deventry came back to the front (the user's own edits), a watched folder changed, or another tab's Claude
    /// changed a file listed here. The rows look at the disk again, or git again; a tab in the background does once
    /// it's shown.
    /// </summary>
    internal void CheckDisk()
    {
        if (_changes is null && !ShowGitChanges)
        {
            return;
        }
        QueueRefresh();
    }

    /// <summary>Follows the session's Edit and Write calls, live and from a restored transcript.</summary>
    /// <param name="live">From the running session, so its changes count toward the turn's footer; false for a transcript read back.</param>
    /// <param name="at">When a transcript's entry was written, so a change read back keeps its time; null for a live change, which is stamped now.</param>
    internal void Record(SessionEvent sessionEvent, bool live = true, DateTimeOffset? at = null)
    {
        switch (sessionEvent)
        {
            case AssistantMessageReceived { Message: var message }:
                foreach (var toolUse in message.Content.OfType<ToolUseBlock>().Where(t => ChangedFiles.IsFileTool(t.Name)))
                {
                    Changes.RecordToolUse(toolUse.Id, toolUse.Name, toolUse.Input);
                }
                break;
            case ToolResultsReceived { Message: var results }:
                // A message has one structured result; with several tool results in it, the changes use it only for
                // the call whose file it names.
                foreach (var result in results.Content.OfType<ToolResultBlock>())
                {
                    if (Changes.RecordToolResult(result.ToolUseId, result.IsError, results.ToolUseResult, at, inTurn: live) is { } file && live)
                    {
                        // The other tabs that changed the file look at it again (DESIGN.md §8, "This session's changes").
                        _host.ClaudeChangedFile(file.Path);
                        // This change made it: the session's first, to a file that wasn't there.
                        if (file.IsNew && file.ToolUseIds.Count == 1 && file.ToolUseIds[0] == result.ToolUseId)
                        {
                            _host.ClaudeCreatedFile(file.Path);
                        }
                    }
                }
                break;
        }
    }

    /// <summary>
    /// <c>/clear</c> started a new conversation (DESIGN.md §8): its changes start from nothing, and the marks, which were
    /// for the old one's, go as <b>Start a new session</b>'s do.
    /// </summary>
    internal void Clear()
    {
        _host.State.ReviewedFiles.Clear();
        _reviewed = null;
        // Its tree starts again too, every folder expanded.
        _host.State.CollapsedChangedFolders.Clear();
        _collapsed = null;
        Reset();
        _services.SaveState();
        ReviewedChanged?.Invoke();
    }

    /// <summary>
    /// The conversation went back to an earlier point (DESIGN.md §5, "Rewind and branch"): the changes are recorded
    /// again as it's read back up to there.
    /// </summary>
    internal void Reset()
    {
        if (_changes is not null)
        {
            _changes.Changed -= QueueRefresh;
            _changes.Changed -= QueueAllReviewed;
            _changes.Changed -= OnClaudeChanged;
            _changes = null;
        }
        ClaudeChangesUpdated?.Invoke();
        _inspections.Clear();
        // The footers are read back without their files: a transcript has no turns to show.
        _turns.Clear();
        QueueRefresh();
        QueueAllReviewed();
    }

    // ---- Changes per turn (DESIGN.md §8) ---------------------------------------------------------------------------

    /// <summary>The footers of the turns that changed files, oldest first: a turn's changes end where a later one's start.</summary>
    private readonly List<TurnSummaryItem> _turns = [];

    /// <summary>
    /// A turn ended: the files it changed go on its footer, <paramref name="summary"/>, and the next turn starts
    /// counting. A turn without a footer (stopped, or ended in an error) keeps its changes only in the list.
    /// </summary>
    internal void EndTurn(TurnSummaryItem? summary)
    {
        // The turn's commands may have changed the files too, or committed them.
        CheckDisk();
        var changes = Changes.TakeTurn();
        if (summary is null || changes.Count == 0)
        {
            return;
        }
        var folder = _host.Folder;
        var rows = changes.Select(c => new TurnFileRow(summary, c, Relative(c.Path, folder))).ToArray();
        summary.Files = rows;
        _turns.Add(summary);
        _ = CountTurnAsync(rows);
    }

    /// <summary>The footer's counts, against the files as the turn left them: on disk, as it has just ended.</summary>
    private async Task CountTurnAsync(IReadOnlyList<TurnFileRow> rows)
    {
        var changes = Changes;
        var counted = await Task.Run(() => rows.Select(row => changes.CountTurn(row.Change)).ToArray());
        for (var i = 0; i < rows.Count; i++)
        {
            if (counted[i] is not { } count)
            {
                continue;
            }
            var (added, removed, hidden) = count;
            // No line changed: what did, in place of +0 −0 (DESIGN.md §8, "What the lines don't show").
            rows[i].Stats = added + removed == 0 && HiddenChangeText.Short(hidden) is { } shown ? shown : $"+{added} −{removed}";
            rows[i].Hidden = hidden;
            rows[i].Note = HiddenChangeText.Note(hidden, linesChanged: added + removed > 0 && hidden is not { WhitespaceOnly: true });
        }
    }

    /// <summary>
    /// A turn's files with their lines added and removed, for the remote clients' message about it (DESIGN.md §18,
    /// "Remote clients"): counted off the UI thread, as the footer's are.
    /// </summary>
    internal async Task<IReadOnlyList<Core.Remote.RemoteFileChange>> CountForRemoteAsync(IReadOnlyList<TurnFileRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }
        var changes = Changes;
        var counted = await Task.Run(() => rows.Select(row => changes.CountTurn(row.Change)).ToArray());
        return [.. rows.Select((row, i) => new Core.Remote.RemoteFileChange(row.DisplayPath, counted[i]?.Added ?? 0, counted[i]?.Removed ?? 0, row.Change.IsNew))];
    }

    /// <summary>
    /// A file on a turn's footer: its diff from before the turn to how the turn left it, which is how the next turn that
    /// changed it found it, or the file now when none has.
    /// </summary>
    [RelayCommand]
    private void OpenTurnDiff(TurnFileRow? row)
    {
        if (row is null)
        {
            return;
        }
        var change = row.Change;
        var later = _turns.SkipWhile(t => !ReferenceEquals(t, row.Turn)).Skip(1)
            .SelectMany(t => t.Files).FirstOrDefault(f => Changes.SamePath(f.Change.Path, change.Path));
        // How the turn's last change left the file; failing that, how the next turn that changed it found it. A later
        // turn's "before" that isn't known leaves the file now as the best there is.
        var after = change.AfterKnown ? new DiffAfter(change.After)
            : later is { Change.BeforeKnown: true } ? new DiffAfter(later.Change.Before) : null;
        later = change.AfterKnown ? null : later;
        var label = !change.BeforeKnown ? "what it held before this turn isn't known, so there's nothing to compare it with"
            : after is not null ? (change.IsNew ? "this turn's changes: new in it, as it left it" : "this turn's changes: before it, against how it left it")
            : later is not null ? "this turn's changes, with a later turn's: before it, against the file now"
            : change.IsNew ? "this turn's changes: new in it, against the file now"
            : "this turn's changes: before it, against the file now";
        // What changed that the lines don't show: as the tile counted it, against how the turn left the file; against
        // how the next turn found it, with the line endings it found; against the file now, from its text.
        Func<string?, HiddenChanges?> hidden = change.AfterKnown ? _ => row.Hidden
            : later is { Change.BeforeKnown: true } next ? text => HiddenChanges.Between(change.Before, change.BeforeLineEndings, text, next.Change.BeforeLineEndings)
            : text => HiddenChanges.Between(change.Before, change.BeforeLineEndings, text, LineEndingsOf(text));
        var file = new ChangedFileRow { Path = change.Path, DisplayPath = row.DisplayPath, Status = change.IsNew ? "A" : "M", StatusText = "" };
        Func<Task>? inDiffTool = HasDiffTool && change.BeforeKnown ? () => OpenTurnInDiffToolAsync(change.Path, change.Before, after) : null;
        DiffRequested?.Invoke(new DiffSource(
            change.Path,
            row.DisplayPath,
            change.Before,
            label,
            inDiffTool,
            () => OpenFromDiffAsync(file, inDiffTool),
            () => RevealFileAsync(file),
            () => CopyFilePathAsync(file),
            change.BeforeKnown,
            After: after,
            AllowRevert: false,
            OpenInSystem: () => OpenFileInSystemAsync(file),
            WordWrap: DiffWordWrap.From(_services),
            Hidden: hidden));
    }

    private static LineEndingKind? LineEndingsOf(string? text) => text is null ? null : LineEndings.Detect(text);

    /// <summary>A turn's changes in the external diff tool: before the turn, against how it left the file, or the file now.</summary>
    private async Task OpenTurnInDiffToolAsync(string path, string? before, DiffAfter? after)
    {
        try
        {
            var launcher = new DiffToolLauncher(_services.Launcher, _services.Time, environment: _services.UserEnvironment) { GitPath = _services.Settings.Git.Path };
            if (after is null)
            {
                await launcher.LaunchAsync(DiffTool, before, path, DiffTempDirectory);
            }
            else
            {
                await launcher.LaunchBetweenAsync(DiffTool, before, after.Text, Path.GetFileName(path), DiffTempDirectory);
            }
        }
        catch (Exception ex)
        {
            _host.AddNote($"Couldn't open the diff tool: {ex.Message}", NoteKind.Error);
        }
    }

    /// <summary>
    /// The diff window's <b>Open</b>, as Settings → Files and diffs → <b>Open in the diff window</b> says (DESIGN.md §8): the file
    /// as <b>Open files in</b> says, the file in the OS's app, or the changes in the diff tool when there's one for them.
    /// True when the file opened in Deventry's window, which the diff window stays over.
    /// </summary>
    private async Task<bool> OpenFromDiffAsync(ChangedFileRow row, Func<Task>? inDiffTool)
    {
        switch (_services.Settings.Editor.DiffWindowOpen)
        {
            case DiffWindowOpen.System:
                await OpenFileInSystemAsync(row);
                return false;
            case DiffWindowOpen.DiffTool when inDiffTool is not null:
                await inDiffTool();
                return false;
            default:
                return !row.IsMissing && await _host.OpenPathAsync(row.Path) == OpenedPath.InDeventry;
        }
    }

    private void QueueRefresh()
    {
        if (!_host.IsSelected)
        {
            // Nobody sees a background tab's rows: keep the count, and inspect the files (or ask git) once it's shown.
            if (!ShowGitChanges)
            {
                Count = Changes.Files.Count;
            }
            return;
        }
        if (ShowGitChanges)
        {
            // Once the edits pause.
            GitRefreshWait.Restart(GitRefreshDelay, () => _ = RefreshAsync());
            return;
        }
        if (_refreshQueued)
        {
            return;
        }
        _refreshQueued = true;
        _services.Dispatcher.Post(() =>
        {
            _refreshQueued = false;
            _ = RefreshAsync();
        });
    }

    [RelayCommand]
    internal async Task RefreshAsync()
    {
        var folder = _host.Folder;
        var generation = ++_refreshGeneration;
        List<ChangedFileRow> rows;
        if (ShowGitChanges)
        {
            GitRefreshWait.Cancel();
            var refresh = new CancellationTokenSource();
            Interlocked.Exchange(ref _gitRefresh, refresh)?.Cancel();
            // The tab's working tree, and each other one Claude changed files in that's still there (DESIGN.md §8, "Working trees").
            var files = _changes?.Files.ToArray() ?? [];
            var (home, others) = await Task.Run(() =>
            {
                var home = GitInfo.TryGetWorkTree(folder)?.WorkTree;
                var others = files.Select(f => WorkTreeOf(f.Path)).OfType<string>()
                    .Where(tree => !IsHome(tree, home) && Directory.Exists(tree) && GitInfo.TryGetWorkTree(tree) is not null)
                    .Distinct(ChangedFiles.PlatformPathComparer).ToArray();
                return (home, others);
            });
            rows = [];
            foreach (var tree in others.Prepend(null))
            {
                IReadOnlyList<GitChange> changes;
                try
                {
                    changes = await _services.Git.GetChangesAsync(tree ?? folder, refresh.Token);
                }
                catch (OperationCanceledException)
                {
                    // A newer refresh took over.
                    return;
                }
                var from = tree ?? folder;
                rows.AddRange(changes.Select(c => new ChangedFileRow
                {
                    Path = c.Path,
                    DisplayPath = Relative(c.Path, from),
                    Status = c.Kind switch { GitChangeKind.Added or GitChangeKind.Untracked => "A", GitChangeKind.Deleted => "D", GitChangeKind.Renamed => "R", _ => "M" },
                    StatusText = c.Kind switch
                    {
                        GitChangeKind.Untracked => "Untracked",
                        GitChangeKind.Renamed => $"Renamed from {Relative(c.OldPath ?? "", from)}",
                        _ => c.Kind.ToString(),
                    },
                    Stats = c.Added is { } a && c.Removed is { } r && (a + r > 0 || c.Hidden is null) ? $"+{a} −{r}" : null,
                    Added = c.Added,
                    Removed = c.Removed,
                    Note = HiddenChangeText.Note(c.Hidden, linesChanged: c.Added + c.Removed > 0, fromGit: true),
                    Hidden = c.Hidden,
                    FromGit = true,
                    WorkTree = tree,
                }));
            }
            rows = await Task.Run(() => InGroups(rows, home, folder));
        }
        else
        {
            var files = Changes.Files.ToArray();
            var changes = Changes;
            rows = await Task.Run(() =>
            {
                var home = GitInfo.TryGetWorkTree(folder)?.WorkTree;
                return InGroups(files.Select(file => Row(changes, file, folder, home)).ToList(), home, folder);
            });
        }
        if (generation != _refreshGeneration)
        {
            return;
        }
        // Marks on files Claude has changed since are forgotten (DESIGN.md §8, "Reviewed").
        if (Reviewed.Prune(Changes))
        {
            _services.SaveState();
        }
        foreach (var row in rows)
        {
            var claudeChanges = Changes.Find(row.Path);
            row.LatestChange = ReviewedFiles.LatestChange(claudeChanges);
            row.IsReviewed = Reviewed.IsReviewed(row.Path, claudeChanges);
            // Changed since by another tab's Claude: which one.
            if (row is { Since: { } since, IsGone: false } && claudeChanges is not null && _host.ChangedSinceBy(row.Path, claudeChanges.LastChanged) is { } by)
            {
                row.Since = $"{since}, by Claude in {by}";
            }
        }
        UpdateWatches();
        SyncRows(rows);
        ShowRows();
        Count = Files.Count;
        UpdateSummary();
        OnPropertyChanged(nameof(IsGitRepository));
    }

    /// <summary>A changed file's row: its changes in the session, from the tab's folder or its own working tree's top. Reads the file, so off the UI thread.</summary>
    private ChangedFileRow Row(ChangedFiles changes, ChangedFile file, string folder, string? home)
    {
        var tree = WorkTreeOf(file.Path);
        var own = IsHome(tree, home);
        // Its worktree is gone: removed, or merged and cleaned up. Its changes are the session's, as they were.
        var treeGone = !own && !Directory.Exists(tree);
        var state = Inspect(changes, file, treeGone);
        return new ChangedFileRow
        {
            Path = file.Path,
            DisplayPath = own ? changes.DisplayPath(file, folder) : changes.DisplayPath(file, tree!),
            Status = state.Status switch { ChangedFileStatus.Added => "A", ChangedFileStatus.Deleted => "D", ChangedFileStatus.Unchanged => "=", _ => "M" },
            StatusText = (state.Status switch
            {
                ChangedFileStatus.Unchanged => "Back to how it was",
                _ when !file.BeforeKnown => $"{state.Status} (Claude Code didn't report the earlier content)",
                _ => state.Status.ToString(),
            }) + (file.ChangedBetween ? ", with changes made outside this session between its edits" : "")
                + (treeGone ? ", in a worktree that's gone" : ""),
            // No line changed: the line under the folder says what did, in place of +0 −0.
            Stats = state.Added is { } a && state.Removed is { } r ? a + r > 0 || state.Hidden is null ? $"+{a} −{r}" : null : state.IsBinary ? "binary" : null,
            Added = state.Added,
            Removed = state.Removed,
            Note = HiddenChangeText.Note(state.Hidden, linesChanged: state.Added + state.Removed > 0 && state.Hidden is not { WhitespaceOnly: true }),
            Hidden = state.Hidden,
            BeforeLineEndings = file.BeforeLineEndings,
            Before = file.Before,
            BeforeKnown = file.BeforeKnown,
            After = file.After,
            AfterKnown = file.AfterKnown,
            IsGone = treeGone || file.AfterKnown && state.Since is { Deleted: true },
            InGoneWorkTree = treeGone,
            Since = treeGone ? null : SinceText(state.Since),
            WorkTree = own ? null : tree,
        };
    }

    /// <summary>The row's line for what changed since the session's latest change (DESIGN.md §8, "This session's changes").</summary>
    private static string? SinceText(ChangedSince? since) => since switch
    {
        null => null,
        { Deleted: true } => "Deleted since",
        { Added: { } added, Removed: { } removed } => $"Changed since: +{added} −{removed}",
        _ => "Changed since",
    };

    /// <summary>"Files (n)" in the composer bar, kept up to date even while the rows wait for the tab to be shown.</summary>
    [ObservableProperty]
    public partial int Count { get; private set; }

    // ---- Subagents' changes (DESIGN.md §8) ----------------------------------------------------------------------------

    /// <summary>The tools whose calls change files, as a hook's matcher.</summary>
    internal const string FileToolMatcher = "Edit|Write|MultiEdit|NotebookEdit";

    /// <summary>
    /// The PreToolUse hook every tab registers on the file tools: a subagent's Edit or Write comes without what the file
    /// held before, live and in its transcript (checked with 2.1.302 and 2.1.308), so the hook reads it first.
    /// </summary>
    internal HookRegistration BeforeHook() => new("PreToolUse", FileToolMatcher, OnFileToolCallAsync);

    /// <summary>
    /// A file tool's call, before it runs: Claude Code waits for the answer, so the file is as the call found it. A
    /// subagent's (<c>agent_id</c>) result won't say what it held, so the hook reads that; the session's own calls report
    /// a file with Windows line endings with <c>\n</c>, so the hook reads its line endings (DESIGN.md §8, "What the
    /// lines don't show"). Called off the UI thread.
    /// </summary>
    private Task<JsonObject> OnFileToolCallAsync(HookInput input, CancellationToken cancellationToken)
    {
        if (input.ToolUseId is { } id
            && (input.ToolInput?.GetString("file_path") ?? input.ToolInput?.GetString("notebook_path")) is { Length: > 0 } path
            && Path.IsPathFullyQualified(path))
        {
            // Posted before the answer goes, so before the tool's result comes.
            if (input.Raw["agent_id"] is not null)
            {
                var (before, known) = ChangedFiles.ReadFromDisk(path);
                _services.Dispatcher.Post(() => Changes.NoteBefore(id, path, before, known));
            }
            else if (ChangedFiles.ReadLineEndings(path) is { } lineEndings)
            {
                _services.Dispatcher.Post(() => Changes.NoteLineEndings(id, lineEndings));
            }
        }
        return Task.FromResult(HookOutputs.Continue());
    }

    // ---- Following the disk (DESIGN.md §8) ---------------------------------------------------------------------------

    /// <summary>How long the rows wait after the disk changes before looking again: a build or a checkout writes many files at once.</summary>
    internal static readonly TimeSpan DiskSettleDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>The most folders watched at once, so a session that changed files all over doesn't hold a watch on each.</summary>
    internal const int MaxWatchedFolders = 64;

    /// <summary>The folders watched while the tab is on screen, by path.</summary>
    private readonly Dictionary<string, IDisposable> _watches = new(ChangedFiles.PlatformPathComparer);
    private bool _watching;
    private UiTimeout DiskSettle => field ??= new(_services.Time, _services.Dispatcher);

    /// <summary>
    /// The tab came on screen or went off it. On screen, the rows catch up with the disk, and the folders of the files
    /// Claude changed are watched, and in the working tree view the working tree's git folder, so what anything else
    /// does there shows; off it, nothing is watched, and the rows wait.
    /// </summary>
    internal void OnShownChanged(bool shown)
    {
        _watching = shown;
        if (shown)
        {
            UpdateWatches();
            CheckDisk();
        }
        else
        {
            StopWatching();
        }
    }

    /// <summary>Another tab's Claude changed <paramref name="path"/>: a row for it, or the working tree view, looks again.</summary>
    internal void OnOtherTabChanged(string path)
    {
        if (ShowGitChanges || _changes?.Find(path) is not null)
        {
            CheckDisk();
        }
    }

    /// <summary>Watches the folders the rows need, and stops watching those they no longer do.</summary>
    private void UpdateWatches()
    {
        if (!_watching)
        {
            return;
        }
        var folders = new HashSet<string>(ChangedFiles.PlatformPathComparer);
        if (ShowGitChanges && GitInfo.TryGetWorkTree(_host.Folder) is { } tree)
        {
            // Its index and HEAD: staging, commits and checkouts.
            folders.Add(tree.GitDir);
        }
        foreach (var file in _changes?.Files ?? [])
        {
            if (folders.Count >= MaxWatchedFolders)
            {
                break;
            }
            if (Path.GetDirectoryName(file.Path) is { Length: > 0 } folder && Path.IsPathFullyQualified(folder))
            {
                folders.Add(folder);
            }
        }
        foreach (var folder in _watches.Keys.Where(f => !folders.Contains(f)).ToArray())
        {
            _watches[folder].Dispose();
            _watches.Remove(folder);
        }
        foreach (var folder in folders.Where(f => !_watches.ContainsKey(f) && Directory.Exists(f)))
        {
            _watches[folder] = _services.FolderWatcher.Watch(folder, path => _services.Dispatcher.Post(() => OnDiskChanged(path)));
        }
    }

    private void StopWatching()
    {
        foreach (var watch in _watches.Values)
        {
            watch.Dispose();
        }
        _watches.Clear();
        DiskSettle.Cancel();
    }

    /// <summary>Something changed in a watched folder: a changed file, or in the working tree view anything, looks again once the writing pauses.</summary>
    private void OnDiskChanged(string? path)
    {
        if (!_watching || !ShowGitChanges && path is not null && _changes?.Find(path) is null)
        {
            return;
        }
        DiskSettle.Restart(DiskSettleDelay, CheckDisk);
    }

    /// <summary>
    /// A file's inspection, from the cache while neither the file nor what's known of it has changed. In a worktree
    /// that's gone (<paramref name="treeGone"/>), the session's own changes alone, whatever is on disk.
    /// </summary>
    private ChangedFileState Inspect(ChangedFiles changes, ChangedFile file, bool treeGone = false)
    {
        var info = new FileInfo(file.Path);
        var key = info.Exists
            ? new InspectionKey(true, info.Length, info.LastWriteTimeUtc, file.Before, file.BeforeKnown, file.IsNew, file.After, file.AfterKnown, treeGone)
            : new InspectionKey(false, 0, default, file.Before, file.BeforeKnown, file.IsNew, file.After, file.AfterKnown, treeGone);
        if (_inspections.TryGetValue(file.Path, out var cached) && cached.Key == key)
        {
            return cached.State;
        }
        // The rows don't need the file's text, so the cache doesn't keep it.
        var state = (treeGone ? changes.InspectOwn(file) : changes.Inspect(file)) with { Current = null };
        _inspections[file.Path] = (key, state);
        return state;
    }

    /// <summary>
    /// Brings the list in line with <paramref name="rows"/>, replacing only the rows that changed, rather than clearing
    /// and refilling it (which rebuilt every row's controls after each edit).
    /// </summary>
    private void SyncRows(List<ChangedFileRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= Files.Count)
            {
                Files.Add(rows[i]);
            }
            else if (!Files[i].SameAs(rows[i]))
            {
                Files[i] = rows[i];
            }
        }
        while (Files.Count > rows.Count)
        {
            Files.RemoveAt(Files.Count - 1);
        }
    }

    /// <summary>For example "5 files changed · 2 reviewed".</summary>
    private void UpdateSummary()
    {
        // A file back to how it was isn't changed.
        var summary = (Files.Count, Files.Count(r => r.Status != "=")) switch
        {
            (0, _) => ShowGitChanges ? "No changes in the working tree" : "No changes yet",
            (_, 0) => "No files changed",
            (_, 1) => "1 file changed",
            (_, var count) => $"{count} files changed",
        };
        var reviewed = Files.Count(r => r.IsReviewed && r.Status != "=");
        Summary = reviewed > 0 ? $"{summary} · {reviewed} reviewed" : summary;
    }

    private static string Relative(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        // A name such as ..data is inside the folder; .. and ../x lead out of it.
        return (FolderListing.IsUp(relative) || Path.IsPathRooted(relative) ? path : relative).Replace('\\', '/');
    }

    // ---- Reviewed (DESIGN.md §8) -----------------------------------------------------------------------------------

    /// <summary>A row's checkbox: marks the file as reviewed, or clears the mark.</summary>
    [RelayCommand]
    private void ToggleFileReviewed(ChangedFileRow? row)
    {
        if (row is not null)
        {
            SetFileReviewed(row.Path, !row.IsReviewed, row.LatestChange);
        }
    }

    /// <summary>
    /// Marks a file as reviewed as of Claude's change <paramref name="change"/> to it, or clears its mark. It stays
    /// reviewed until Claude changes it again, so marking a change older than Claude's latest leaves it unreviewed.
    /// </summary>
    private void SetFileReviewed(string path, bool reviewed, string? change) => SetFilesReviewed([(path, change)], reviewed);

    /// <summary>Marks several files as reviewed, each as of its change, or clears their marks: a folder's box (DESIGN.md §8, "The tree").</summary>
    private void SetFilesReviewed(IReadOnlyList<(string Path, string? Change)> files, bool reviewed)
    {
        foreach (var (path, change) in files)
        {
            if (reviewed)
            {
                Reviewed.Mark(path, change);
            }
            else
            {
                Reviewed.Unmark(path);
            }
        }
        foreach (var row in Files)
        {
            row.IsReviewed = Reviewed.IsReviewed(row.Path, Changes.Find(row.Path));
        }
        UpdateFolderReviewed();
        UpdateSummary();
        UpdateAllReviewed();
        _services.SaveState();
        QueueReviewSync();
        ReviewedChanged?.Invoke();
    }

    /// <summary>
    /// Every file Claude changed in the session is ticked as reviewed, and there's at least one: the tab's row shows the
    /// reviewed icon in place of its mark (DESIGN.md §4, "Marks"). Files only you or commands changed, in working tree
    /// vs HEAD, don't count, as they don't untick either. Kept up to date in the background too, from Claude's changes
    /// alone, so it doesn't wait for the rows. Saved with the tab, so a restored tab has it before it reads its
    /// conversation back.
    /// </summary>
    [ObservableProperty]
    public partial bool AllReviewed { get; private set; }

    partial void OnAllReviewedChanged(bool value)
    {
        if (_host.State.AllFilesReviewed != value)
        {
            _host.State.AllFilesReviewed = value;
            _services.SaveState();
        }
        _host.AllReviewedChanged();
    }

    /// <summary>
    /// The tab has read its earlier conversation back, or found none: <see cref="AllReviewed"/> goes by the changes
    /// from now on, rather than by what was saved.
    /// </summary>
    internal void ChangesRead() => UpdateAllReviewed();

    private bool _allReviewedQueued;

    /// <summary>
    /// Works out <see cref="AllReviewed"/> again once the changes recorded together are in, rather than after each: a
    /// transcript read back records them by the thousand.
    /// </summary>
    private void QueueAllReviewed()
    {
        if (_allReviewedQueued)
        {
            return;
        }
        _allReviewedQueued = true;
        _services.Dispatcher.Post(() =>
        {
            _allReviewedQueued = false;
            UpdateAllReviewed();
        });
    }

    private void UpdateAllReviewed()
    {
        var files = Changes.Files;
        AllReviewed = files.Count > 0 && files.All(file => Reviewed.IsReviewed(file.Path, file));
    }

    /// <summary>
    /// Brings the library's record up to date with the marks, for a tab that syncs (DESIGN.md §9, "Writing"). The
    /// transcript is only copied again if it changed. While a turn runs, the copy as it ends carries them instead.
    /// </summary>
    private void QueueReviewSync()
    {
        if (!_host.State.SyncToLibrary || _reviewSyncStopped)
        {
            return;
        }
        ReviewSync.Restart(ReviewSyncDelay, () =>
        {
            if (_host.CanSyncNow && !_reviewSyncStopped)
            {
                _host.CopyToLibrary();
            }
        });
    }

    /// <summary>
    /// The tab is closing: a diff view still open can mark files, but nothing more is written to the library, and git
    /// isn't asked again.
    /// </summary>
    internal void StopReviewSync()
    {
        _reviewSyncStopped = true;
        ReviewSync.Cancel();
        GitRefreshWait.Cancel();
        _gitRefresh?.Cancel();
        _watching = false;
        StopWatching();
    }

    // ---- Opening diffs (DESIGN.md §8) ------------------------------------------------------------------------------

    /// <summary>Asks the view to show the built-in diff view.</summary>
    public event Action<DiffSource>? DiffRequested;

    /// <summary>Whether an external diff tool is set in Settings → Files and diffs → Diff tool.</summary>
    public bool HasDiffTool => DiffTool.Kind != DiffToolKind.BuiltIn;

    private DiffToolChoice DiffTool => DiffToolFrom(_services.Settings.DiffTool);

    /// <summary>Settings → Files and diffs → <b>Diff tool</b>, as a choice to launch: the built-in view, a preset, or a command.</summary>
    internal static DiffToolChoice DiffToolFrom(DiffToolSettings settings) => settings.Kind switch
    {
        "preset" when settings.PresetId is { Length: > 0 } => new DiffToolChoice(DiffToolKind.Preset, settings.PresetId),
        "custom" when settings.CustomCommand is { Length: > 0 } => new DiffToolChoice(DiffToolKind.Custom, CustomCommand: settings.CustomCommand),
        _ => DiffToolChoice.BuiltIn,
    };

    private string DiffTempDirectory => Path.Combine(_services.Paths.DiffTempDirectory, _host.Id);

    /// <summary>Selecting a file opens the built-in diff view.</summary>
    [RelayCommand]
    private async Task OpenFileDiffAsync(ChangedFileRow? row)
    {
        if (row is null)
        {
            return;
        }
        var before = row.FromGit ? await _services.Git.GetHeadContentAsync(row.WorkTree ?? _host.Folder, row.Path) : row.Before;
        var beforeKnown = row.FromGit || row.BeforeKnown;
        Func<Task>? inDiffTool = HasDiffTool && beforeKnown ? () => OpenFileInDiffToolAsync(row) : null;
        var review = new DiffReview(
            () => ReviewedFiles.LatestChange(Changes.Find(row.Path)),
            () => Reviewed.IsReviewed(row.Path, Changes.Find(row.Path)),
            change => SetFileReviewed(row.Path, reviewed: true, change));
        var againstNow = new DiffSource(
            row.Path,
            row.DisplayPath,
            before,
            row.FromGit ? "compared with HEAD"
                : !beforeKnown ? "what it held before Claude's first change isn't known, so there's nothing to compare it with"
                : before is null ? row.Since is null ? "new in this session" : "new in this session, against the file now, with the changes since"
                : row.Since is null ? "compared with before Claude's first change in this session"
                : "from before Claude's first change in this session to the file now, with the changes since",
            inDiffTool,
            () => OpenFromDiffAsync(row, inDiffTool),
            () => RevealFileAsync(row),
            () => CopyFilePathAsync(row),
            beforeKnown,
            review,
            FromGit: row.FromGit,
            OpenInSystem: () => OpenFileInSystemAsync(row),
            WordWrap: DiffWordWrap.From(_services),
            // Git's copy at HEAD comes without its line endings, so a git row's are as git said them; the session's are
            // from the file the view reads (DESIGN.md §8, "What the lines don't show").
            Hidden: row.FromGit ? _ => row.Hidden : text => HiddenChanges.Between(before, row.BeforeLineEndings, text, LineEndingsOf(text)));
        // How the session's latest change left the file: with the line endings Claude Code reported, so theirs aren't told.
        var asLeft = againstNow with { After = new DiffAfter(row.After), AllowRevert = false, Hidden = text => HiddenChanges.Between(before, row.BeforeLineEndings, text, null) };
        DiffRequested?.Invoke(
            // In a worktree that's gone, the session's changes as they were (DESIGN.md §8, "Working trees").
            row is { FromGit: false, AfterKnown: true, InGoneWorkTree: true } ? asLeft with
            {
                BeforeLabel = !beforeKnown ? againstNow.BeforeLabel
                    : before is null ? "new in this session, in a worktree that's gone"
                    : "this session's changes, in a worktree that's gone",
            }
            // Changed since the session's latest change: the diff ends where that left it, with the file now to switch to
            // (DESIGN.md §8, "This session's changes").
            : row is { FromGit: false, AfterKnown: true, Since: not null } && beforeKnown ? asLeft with
            {
                BeforeLabel = before is null ? "new in this session, as its last change left it" : "this session's changes: from before its first change to how its last change left the file",
                AgainstNow = againstNow,
            }
            : againstNow);
    }

    /// <summary>
    /// <b>Open diff</b> on an Edit or Write card (DESIGN.md §5): the file in the diff view, as Changed files shows it,
    /// from before Claude's first change in this session to the file now.
    /// </summary>
    [RelayCommand]
    private Task OpenToolDiffAsync(ToolUseItem? tool) => tool is null ? Task.CompletedTask : OpenChangeDiffAsync(tool.ToolUseId);

    /// <summary>The file a change (its tool call's id) was made to, in the diff view as Changed files shows it.</summary>
    internal async Task OpenChangeDiffAsync(string toolUseId)
    {
        if (Changes.Files.FirstOrDefault(f => f.ToolUseIds.Contains(toolUseId)) is not { } file)
        {
            return;
        }
        // With what changed on disk since the session's latest change, as its row has it.
        var changes = Changes;
        var since = file.AfterKnown ? (await Task.Run(() => Inspect(changes, file))).Since : null;
        await OpenFileDiffAsync(new ChangedFileRow
        {
            Path = file.Path,
            DisplayPath = Changes.DisplayPath(file, _host.Folder),
            Status = file.IsNew ? "A" : "M",
            StatusText = file.IsNew ? "Added" : "Modified",
            Before = file.Before,
            BeforeKnown = file.BeforeKnown,
            BeforeLineEndings = file.BeforeLineEndings,
            After = file.After,
            AfterKnown = file.AfterKnown,
            Since = SinceText(since),
        });
    }

    /// <summary>Double-click: the external diff tool when one is set, else the built-in view.</summary>
    [RelayCommand]
    private Task OpenFileAsync(ChangedFileRow? row) => HasDiffTool ? OpenFileInDiffToolAsync(row) : OpenFileDiffAsync(row);

    /// <summary>A file whose "before" is unknown opens in the built-in view instead, which says so.</summary>
    [RelayCommand]
    private async Task OpenFileInDiffToolAsync(ChangedFileRow? row)
    {
        if (row is null || !HasDiffTool)
        {
            return;
        }
        if (!row.FromGit && !row.BeforeKnown)
        {
            await OpenFileDiffAsync(row);
            return;
        }
        try
        {
            var before = row.FromGit ? await _services.Git.GetHeadContentAsync(row.WorkTree ?? _host.Folder, row.Path) : row.Before;
            var launcher = new DiffToolLauncher(_services.Launcher, _services.Time, environment: _services.UserEnvironment) { GitPath = _services.Settings.Git.Path };
            if (row is { FromGit: false, AfterKnown: true, InGoneWorkTree: true })
            {
                // Its worktree is gone: the session's changes as they were.
                await launcher.LaunchBetweenAsync(DiffTool, before, row.After, Path.GetFileName(row.Path), DiffTempDirectory);
            }
            else
            {
                await launcher.LaunchAsync(DiffTool, before, row.Path, DiffTempDirectory);
            }
        }
        catch (Exception ex)
        {
            _host.AddNote($"Couldn't open the diff tool: {ex.Message}", NoteKind.Error);
        }
    }

    /// <summary><b>Open</b>: in the project's Files tab, or the OS's app, as Settings → Files and diffs says (DESIGN.md §18, "Project files").</summary>
    [RelayCommand]
    private Task OpenFileInEditorAsync(ChangedFileRow? row) =>
        row is { IsMissing: false } ? _host.OpenPathAsync(row.Path) : Task.CompletedTask;

    /// <summary><b>Open in the OS's app</b>, whatever Settings → Files and diffs says.</summary>
    [RelayCommand]
    private Task OpenFileInSystemAsync(ChangedFileRow? row) =>
        row is { IsMissing: false } ? _services.Platform.OpenFileAsync(row.Path) : Task.CompletedTask;

    [RelayCommand]
    private Task RevealFileAsync(ChangedFileRow? row) =>
        row is not null && Path.GetDirectoryName(row.Path) is { } folder && Directory.Exists(folder)
            ? _services.Platform.RevealFolderAsync(folder)
            : Task.CompletedTask;

    [RelayCommand]
    private Task CopyFilePathAsync(ChangedFileRow? row) =>
        row is null ? Task.CompletedTask : _services.Platform.SetClipboardTextAsync(row.Path);

    /// <summary>The "before" files handed to diff tools are deleted when the tab closes (DESIGN.md §8).</summary>
    internal void CleanUpDiffFiles()
    {
        try
        {
            DiffTempFiles.Cleanup(DiffTempDirectory);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
