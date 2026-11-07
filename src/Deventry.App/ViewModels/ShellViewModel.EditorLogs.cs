using Deventry.App.Services;
using Deventry.Core.ProjectTools;
using Deventry.Core.ProjectTools.EditorLogs;

namespace Deventry.App.ViewModels;

/// <summary>
/// Editor logs (DESIGN.md §18, "Editor logs"): while one of a project's engine processes runs, its log is a row under one
/// of the project's tabs, and a tab of the panes without a composer, as a run's log is. The log goes under the tab whose
/// own process started it (Claude running a commandlet), else the tab whose launch started it, else the first of the
/// project's tabs in the sidebar; it stays there while that tab has the project, moves to the next when it no longer
/// does, and closes with it.
/// Each file is one log for the whole app, read by <see cref="EditorLogService"/>. A log whose processes move to another
/// file, as Unity's editor does once it has the project open, stays as it was, saying where, until they end; the other
/// file is a log of its own under the same tab.
/// </summary>
public sealed partial class ShellViewModel : IEditorLogHost
{
    /// <summary>A launch from a tab counts for the project's next process to start within this long.</summary>
    private static readonly TimeSpan LaunchCounts = TimeSpan.FromMinutes(2);

    /// <summary>The logs being shown or remembered, by file.</summary>
    private Dictionary<string, EditorLogViewModel> EditorLogs => field ??= new(_services.ProjectTools.OS.PathComparer());

    /// <summary>
    /// Logs whose processes moved to another file: no longer their file's log, which something else may write, and
    /// kept until those processes end.
    /// </summary>
    private List<EditorLogViewModel> MovedLogs => field ??= [];

    /// <summary>Every log under a tab or remembered: those of their file, and those moved from it.</summary>
    private EditorLogViewModel[] AllEditorLogs() => [.. EditorLogs.Values, .. MovedLogs];

    /// <summary>The tab that last launched each project's engine, and when, by the project's path.</summary>
    private Dictionary<string, (TabViewModel Tab, DateTimeOffset At)> LaunchedFrom => field ??= new(_services.ProjectTools.OS.PathComparer());

    private bool _watchingEditorLogs;

    /// <summary>The focused tab when it's an editor's log.</summary>
    public EditorLogViewModel? SelectedEditorLog => Selected as EditorLogViewModel;

    /// <summary>The focused tab when it's a log, a run's or an editor's.</summary>
    public LogTabViewModel? SelectedLog => Selected as LogTabViewModel;

    /// <summary>The session tab a log is under.</summary>
    internal TabViewModel? TabOf(LogTabViewModel log) => log switch
    {
        ProjectRunViewModel run => TabOf(run),
        EditorLogViewModel editorLog => editorLog.Owner,
        _ => null,
    };

    /// <summary>
    /// The tabs' projects changed, a tab opened or closed, or Settings did: the service follows the projects with logs,
    /// and each log goes under the tab it belongs to now.
    /// </summary>
    internal void FollowEditorLogs()
    {
        if (!_watchingEditorLogs)
        {
            _watchingEditorLogs = true;
            _services.EditorLogs.Checked += OnEditorLogsChecked;
            _services.EditorLogs.Read += OnEditorLogRead;
            _services.EditorLogs.Moved += OnEditorLogMoved;
        }
        var comparer = _services.ProjectTools.OS.PathComparer();
        var withLogs = _services.Settings.ProjectTools.ShowEditorLogs
            ? AllTabs.Where(t => t.ProjectTools.Project is { Logs: not null }).ToArray()
            : [];
        var projects = withLogs
            .Select(t => t.ProjectTools.Project!)
            .DistinctBy(p => p.ProjectPath, comparer)
            .Select(p => new FollowedProject(p.ProjectPath, p.Root, p.Logs!))
            .ToArray();
        var tabs = withLogs.Select(t => new EditorLogTab(t.Id, t.ProjectTools.Project!.ProjectPath, t.StartedProcess)).ToArray();
        _services.EditorLogs.Follow(projects, tabs);
        if (projects.Length == 0)
        {
            // Turned off, or no project has logs now: they all go at once.
            foreach (var log in AllEditorLogs())
            {
                ForgetLog(log);
            }
        }
        PlaceEditorLogs();
    }

    /// <summary>A tab launched its project's editor, or another of its programs: the project's next process goes under it.</summary>
    internal void OnProjectLaunched(TabViewModel tab)
    {
        if (tab.ProjectTools.Project is { Logs: not null } project)
        {
            LaunchedFrom[project.ProjectPath] = (tab, _services.Time.GetUtcNow());
            _services.EditorLogs.CheckSoon();
        }
    }

    private void OnEditorLogsChecked(IReadOnlyList<EditorLogCheck> checks)
    {
        if (!_services.Settings.ProjectTools.ShowEditorLogs)
        {
            return;
        }
        var live = new HashSet<string>(EditorLogs.Comparer);
        foreach (var check in checks)
        {
            live.Add(check.Log.Path);
            if (!EditorLogs.TryGetValue(check.Log.Path, out var log))
            {
                log = new EditorLogViewModel(_services, this, check.Log.Path, check.Log.Project);
                EditorLogs[check.Log.Path] = log;
            }
            if (check.NewWriters)
            {
                // Starting, or starting again: a log ×'d away comes back, under the tab that started it, if one did.
                log.IsDismissed = false;
                log.PreferredTab = AllTabs.FirstOrDefault(t => t.Id == check.StartedBy) ?? LaunchedTab(check.Log.Project.ProjectPath);
            }
            log.Writing(check.Log.Writers, check.NewWriters);
        }
        foreach (var log in EditorLogs.Values.ToArray())
        {
            if (!live.Contains(log.LogPath))
            {
                log.Stopped();
                if (log.IsDismissed)
                {
                    // Gone until something next writes it, which is a log of its own.
                    EditorLogs.Remove(log.LogPath);
                }
            }
        }
        var running = checks.SelectMany(c => c.Log.Writers).Select(w => w.Key).ToHashSet();
        foreach (var moved in MovedLogs.ToArray())
        {
            if (!moved.Writers.Any(w => running.Contains(w.Key)))
            {
                // The editor that moved from it has ended: the log it moved to has everything.
                ForgetLog(moved);
            }
        }
        PlaceEditorLogs();
    }

    /// <summary>
    /// A log's processes write another file now (DESIGN.md §18, "Editor logs", "A log that moves"): that file is a log
    /// under the same tab, read from its start, and the old one says where it went, until they end.
    /// </summary>
    private void OnEditorLogMoved(EditorLogMove move)
    {
        if (!_services.Settings.ProjectTools.ShowEditorLogs)
        {
            return;
        }
        EditorLogs.TryGetValue(move.From, out var old);
        if (!EditorLogs.TryGetValue(move.To.Path, out var log))
        {
            log = new EditorLogViewModel(_services, this, move.To.Path, move.To.Project);
            EditorLogs[move.To.Path] = log;
        }
        // The same editor's log: ×'d away there, it's away here too, until the editor next starts.
        log.IsDismissed = old?.IsDismissed ?? false;
        log.PreferredTab = old?.Owner ?? old?.PreferredTab ?? LaunchedTab(move.To.Project.ProjectPath);
        log.Writing(move.To.Writers, newWriters: true);
        if (old is not null)
        {
            EditorLogs.Remove(move.From);
            old.Moved(move.To.Path, move.To.Writers);
            if (old.IsDismissed)
            {
                ForgetLog(old);
            }
            else
            {
                MovedLogs.Add(old);
            }
        }
        PlaceEditorLogs();
    }

    /// <summary>The tab that launched the project's engine a moment ago, if one did.</summary>
    private TabViewModel? LaunchedTab(string projectPath)
    {
        if (!LaunchedFrom.Remove(projectPath, out var launched) || _services.Time.GetUtcNow() - launched.At > LaunchCounts)
        {
            return null;
        }
        return launched.Tab;
    }

    private void OnEditorLogRead(string path, EditorLogRead read)
    {
        if (EditorLogs.TryGetValue(path, out var log))
        {
            log.Receive(read);
        }
    }

    /// <summary>
    /// Puts each log under the tab it belongs to now, out of the sidebar while it's ×'d away, and forgets one whose project
    /// no open tab has.
    /// </summary>
    private void PlaceEditorLogs()
    {
        foreach (var log in AllEditorLogs())
        {
            var current = log.Owner;
            var owner = log.IsDismissed ? null : OwnerFor(log, current);
            if (!ReferenceEquals(owner, current))
            {
                current?.ProjectTools.RemoveLog(log);
                owner?.ProjectTools.AddLog(log);
                log.Owner = owner;
            }
            if (owner is null)
            {
                RemoveLogTab(log, current);
                if (!log.IsDismissed || !log.IsRunning)
                {
                    Untrack(log);
                }
            }
            else if (!OpenTabs.Contains(log))
            {
                OpenTabs.Add(log);
            }
        }
    }

    /// <summary>The tab it stays under, the tab that started it, else the first with its project; null when no tab has it.</summary>
    private TabViewModel? OwnerFor(EditorLogViewModel log, TabViewModel? current)
    {
        var comparison = _services.ProjectTools.OS.PathComparison();
        bool HasProject(TabViewModel tab) => tab.ProjectTools.Project is { Logs: not null } project && string.Equals(project.ProjectPath, log.ProjectPath, comparison);
        if (current is not null && HasProject(current) && AllTabs.Contains(current))
        {
            return current;
        }
        if (log.PreferredTab is { } preferred && AllTabs.Contains(preferred) && HasProject(preferred))
        {
            return preferred;
        }
        return AllTabs.FirstOrDefault(HasProject);
    }

    /// <summary>A log leaves for good: out of its tab's rows and the panes.</summary>
    private void ForgetLog(EditorLogViewModel log)
    {
        var tab = log.Owner;
        tab?.ProjectTools.RemoveLog(log);
        log.Owner = null;
        Untrack(log);
        RemoveLogTab(log, tab);
    }

    /// <summary>Forgets a log: one moved from its file, or its file's, while it still is.</summary>
    private void Untrack(EditorLogViewModel log)
    {
        if (!MovedLogs.Remove(log) && EditorLogs.TryGetValue(log.LogPath, out var current) && ReferenceEquals(current, log))
        {
            EditorLogs.Remove(log.LogPath);
        }
    }

    /// <summary>A log leaves the panes. While it had the focus, its tab takes its place, or the focus where it's on screen already.</summary>
    private void RemoveLogTab(EditorLogViewModel log, TabViewModel? tab)
    {
        if (!OpenTabs.Remove(log))
        {
            return;
        }
        var pane = _panes.IndexOf(log);
        if (!ReferenceEquals(Selected, log))
        {
            if (pane >= 0)
            {
                _panes.RemoveAt(pane);
                UpdatePanes();
            }
            return;
        }
        var back = tab is not null && OpenTabs.Contains(tab) ? tab : null;
        if (pane >= 0 && back is not null && !_panes.Contains(back))
        {
            _panes[pane] = back;
        }
        else if (pane >= 0)
        {
            _panes.RemoveAt(pane);
        }
        Selected = back ?? (_panes.Count > 0 ? _panes[Math.Min(Math.Max(pane, 0), _panes.Count - 1)] : null) ?? AllTabs.FirstOrDefault();
    }

    // ---- What a log needs from the shell ------------------------------------------------------------------------------

    TabViewModel? IEditorLogHost.OwnerOf(EditorLogViewModel log) => log.Owner;

    void IEditorLogHost.ShowLog(EditorLogViewModel log)
    {
        if (OpenTabs.Contains(log))
        {
            Selected = log;
        }
    }

    void IEditorLogHost.LogSelected(EditorLogViewModel log)
    {
        // Its row comes out from under a collapsed tab, and a collapsed thread (DESIGN.md §4, "Sidebar").
        TabOf(log)?.RevealRowsUnder();
    }

    void IEditorLogHost.CloseLogPane(EditorLogViewModel log) => ClosePaneCommand.Execute(log);

    void IEditorLogHost.DismissLog(EditorLogViewModel log)
    {
        Dismiss(log);
        PlaceEditorLogs();
    }

    /// <summary>
    /// A tab closing takes its logs with it, as their × would, rather than handing them to another of the project's tabs
    /// (DESIGN.md §18, "Editor logs", "Which tab").
    /// </summary>
    private void CloseEditorLogsOf(TabViewModel tab)
    {
        foreach (var log in tab.ProjectTools.Logs.ToArray())
        {
            Dismiss(log);
        }
    }

    /// <summary>One that has ended is gone; one still being written stays away until new processes write its file.</summary>
    private void Dismiss(EditorLogViewModel log)
    {
        if (!log.IsRunning)
        {
            ForgetLog(log);
            return;
        }
        log.IsDismissed = true;
    }
}
