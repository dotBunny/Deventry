using System.Collections.Specialized;
using System.ComponentModel;
using Deventry.App.Conversation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// How urgent the most urgent of some sidebar rows is, for a collapsed group's label and a collapsed tab's badge
/// (DESIGN.md §4, "Sidebar"): waiting on the user beats an error, which beats work going on, which beats something that
/// finished unseen.
/// </summary>
public enum RowUrgency
{
    None,
    Unread,
    Busy,
    Error,
    NeedsInput,
}

internal static class RowUrgencies
{
    public static RowUrgency Of(TabStatus status) => status switch
    {
        TabStatus.NeedsInput => RowUrgency.NeedsInput,
        TabStatus.Error => RowUrgency.Error,
        TabStatus.Working or TabStatus.Starting => RowUrgency.Busy,
        TabStatus.Unread => RowUrgency.Unread,
        _ => RowUrgency.None,
    };

    /// <summary>A subagent's row: one from an earlier turn that failed is history, dimmed already.</summary>
    public static RowUrgency Of(AgentNode node) =>
        node.IsWaiting ? RowUrgency.NeedsInput
        : node.IsFailed && !node.IsEarlier ? RowUrgency.Error
        : node.IsRunning ? RowUrgency.Busy
        : RowUrgency.None;

    public static RowUrgency Of(ProjectRunViewModel run) =>
        run.Failed ? RowUrgency.Error : run.IsRunning ? RowUrgency.Busy : RowUrgency.None;

    /// <summary>An editor's log: an editor open for hours isn't work going on, so only a crash or errors not seen yet count.</summary>
    public static RowUrgency Of(EditorLogViewModel log) => log.IsError ? RowUrgency.Error : RowUrgency.None;

    /// <summary>The status icon that stands for it, as a tab's row draws its own.</summary>
    public static string Glyph(RowUrgency urgency) => urgency switch
    {
        RowUrgency.NeedsInput => "!",
        RowUrgency.Error => "✕",
        RowUrgency.Busy => "●",
        RowUrgency.Unread => "•",
        _ => "",
    };
}

/// <summary>
/// Collapsing a tab's row in the sidebar (DESIGN.md §4, "Sidebar"): the chevron beside its name folds away what's under
/// it, its sub-threads with what's under them, its subagents' rows, its editor logs and its project runs, and the row's
/// badge says how many and the most urgent of them. It opens again by itself when one of them is gone to, a sub-thread joins, or one
/// starts waiting on the user.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>
    /// The row is picked out for a moment, having just been found with <b>Find a tab</b> (DESIGN.md §4, "Find a tab"):
    /// a few soft pulses of the accent behind it, or with motion reduced a still highlight that fades.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDrawingAttention { get; set; }

    /// <summary>The fold had rows to hide, so it goes once the last of them does.</summary>
    private bool _hadRowsUnder;

    /// <summary>The subagents waiting on the user when the rows were last looked at: a new one opens the fold.</summary>
    private readonly HashSet<AgentNode> _waitingSubagents = [];

    /// <summary>Something is under the tab's row: a sub-thread, a subagent's row, an editor's log or a project run. Its chevron shows.</summary>
    public bool HasRowsUnder => HasSubThreads || HasSubagentRows || ProjectTools.HasLogs || ProjectTools.Runs.HasRuns;

    /// <summary>What's under the row is folded away. Saved with the tab.</summary>
    public bool AreRowsCollapsed => State.RowsCollapsed && HasRowsUnder;

    /// <summary>A sub-thread whose thread is collapsed: its row, and everything under it, are out of the sidebar.</summary>
    public bool IsFoldedAway => ThreadHead is { AreRowsCollapsed: true };

    /// <summary>The subagents' rows are under the tab's: there are some, and they aren't folded away.</summary>
    public bool ShowsSubagentRows => HasSubagentRows && !AreRowsCollapsed;

    /// <summary>The project runs are under the tab's row: there are some, and they aren't folded away.</summary>
    public bool ShowsRuns => ProjectTools.Runs.HasRuns && !AreRowsCollapsed;

    /// <summary>The editor logs are under the tab's row: there are some, and they aren't folded away (DESIGN.md §18, "Editor logs").</summary>
    public bool ShowsLogs => ProjectTools.HasLogs && !AreRowsCollapsed;

    /// <summary><b>Collapse tab</b> or <b>Expand tab</b>: the menu's item, the palette's and the chevron's name.</summary>
    public string CollapseMenuText => AreRowsCollapsed ? "Expand tab" : "Collapse tab";

    /// <summary>The chevron's tip: "Collapse: hides 2 sub-threads and 1 run".</summary>
    public string CollapseTip => AreRowsCollapsed ? $"Expand: shows {RowsUnderText}" : $"Collapse: hides {RowsUnderText}";

    /// <summary>What's under the row: "2 sub-threads, 3 subagents, 1 log and 1 run".</summary>
    public string RowsUnderText
    {
        get
        {
            var parts = new List<string>(4);
            Add(SubThreads.Count, "sub-thread");
            Add(SubagentRows.Count, "subagent");
            Add(ProjectTools.Logs.Count, "log");
            Add(ProjectTools.Runs.Items.Count, "run");
            return parts.Count switch
            {
                0 => "nothing",
                1 => parts[0],
                _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}",
            };

            void Add(int count, string noun)
            {
                if (count > 0)
                {
                    parts.Add(count == 1 ? $"1 {noun}" : $"{count} {noun}s");
                }
            }
        }
    }

    /// <summary>How many rows a collapsed tab hides, on its badge: a sub-thread counts once, with what's under it.</summary>
    public int CollapsedRowCount => SubThreads.Count + SubagentRows.Count + ProjectTools.Logs.Count + ProjectTools.Runs.Items.Count;

    /// <summary>The most urgent of what's under the row, a sub-thread's own rows included.</summary>
    public RowUrgency RowsUrgency
    {
        get
        {
            var most = RowUrgency.None;
            foreach (var subThread in SubThreads)
            {
                most = Max(most, Max(RowUrgencies.Of(subThread.Status), subThread.RowsUrgency));
            }
            foreach (var row in SubagentRows)
            {
                most = Max(most, RowUrgencies.Of(row.Node));
            }
            foreach (var log in ProjectTools.Logs)
            {
                most = Max(most, RowUrgencies.Of(log));
            }
            foreach (var run in ProjectTools.Runs.Items)
            {
                most = Max(most, RowUrgencies.Of(run));
            }
            return most;

            static RowUrgency Max(RowUrgency a, RowUrgency b) => a > b ? a : b;
        }
    }

    /// <summary>The badge's status icon, the most urgent of what it hides; empty when nothing hidden needs saying.</summary>
    public string CollapsedGlyph => RowUrgencies.Glyph(RowsUrgency);

    public bool CollapsedIsBusy => RowsUrgency == RowUrgency.Busy;

    public bool CollapsedIsAlert => RowsUrgency == RowUrgency.NeedsInput;

    public bool CollapsedIsError => RowsUrgency == RowUrgency.Error;

    public bool CollapsedIsUnread => RowsUrgency == RowUrgency.Unread;

    /// <summary>The badge's tip: what it hides, then each hidden row that waits on the user or failed.</summary>
    public string CollapsedTip
    {
        get
        {
            var lines = new List<string> { $"Collapsed: {RowsUnderText}" };
            foreach (var subThread in SubThreads)
            {
                if (subThread.Status is TabStatus.NeedsInput or TabStatus.Error)
                {
                    lines.Add($"{subThread.DisplayName} · {subThread.StatusTip}");
                }
            }
            foreach (var row in SubagentRows)
            {
                if (RowUrgencies.Of(row.Node) is RowUrgency.NeedsInput or RowUrgency.Error)
                {
                    lines.Add($"{row.Node.Title} · {row.Node.StatusText}");
                }
            }
            foreach (var log in ProjectTools.Logs)
            {
                if (log.IsError)
                {
                    lines.Add($"{log.FileName} · {log.Detail}");
                }
            }
            foreach (var run in ProjectTools.Runs.Items)
            {
                if (run.Failed)
                {
                    lines.Add($"{run.Name} · {run.Detail}");
                }
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>The chevron, <b>Collapse tab</b> and <b>Expand tab</b>.</summary>
    [RelayCommand]
    private void ToggleRowsCollapsed() => SetRowsCollapsed(!AreRowsCollapsed);

    /// <summary>Opens the fold: something under it is gone to, joins it, or waits on the user.</summary>
    internal void ExpandRows() => SetRowsCollapsed(false);

    /// <summary>Something under this row, or under its thread's, is gone to: it comes out from under both.</summary>
    internal void RevealRowsUnder()
    {
        ExpandRows();
        ThreadHead?.ExpandRows();
    }

    private void SetRowsCollapsed(bool collapsed)
    {
        collapsed &= HasRowsUnder;
        if (State.RowsCollapsed == collapsed)
        {
            return;
        }
        State.RowsCollapsed = collapsed;
        RowsUnderChanged();
        _services.SaveState();
    }

    /// <summary>The thread this sub-thread is in collapsed or expanded.</summary>
    internal void ThreadFoldChanged() => OnPropertyChanged(nameof(IsFoldedAway));

    /// <summary>The project runs and editor logs come and go, and change how they're going, under the tab's row.</summary>
    private void WatchRunsUnder()
    {
        ProjectTools.Runs.Items.CollectionChanged += OnRunsUnderChanged;
        ProjectTools.Logs.CollectionChanged += OnLogsUnderChanged;
    }

    private void OnLogsUnderChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var log in e.NewItems?.OfType<EditorLogViewModel>() ?? [])
        {
            log.PropertyChanged += OnLogUnderChanged;
        }
        foreach (var log in e.OldItems?.OfType<EditorLogViewModel>() ?? [])
        {
            log.PropertyChanged -= OnLogUnderChanged;
        }
        RowsUnderChanged();
    }

    private void OnLogUnderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorLogViewModel.IsError) or nameof(EditorLogViewModel.Detail))
        {
            RowsUnderChanged();
        }
    }

    private void OnRunsUnderChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var run in e.NewItems?.OfType<ProjectRunViewModel>() ?? [])
        {
            run.PropertyChanged += OnRunUnderChanged;
        }
        foreach (var run in e.OldItems?.OfType<ProjectRunViewModel>() ?? [])
        {
            run.PropertyChanged -= OnRunUnderChanged;
        }
        RowsUnderChanged();
    }

    private void OnRunUnderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectRunViewModel.State))
        {
            RowsUnderChanged();
        }
    }

    /// <summary>The subagents' rows changed: one that starts waiting on the user comes out from under the fold.</summary>
    private void SubagentRowsUnderChanged()
    {
        var waiting = SubagentRows.Select(r => r.Node).Where(n => n.IsWaiting).ToHashSet();
        var newlyWaiting = waiting.Any(n => !_waitingSubagents.Contains(n));
        _waitingSubagents.Clear();
        _waitingSubagents.UnionWith(waiting);
        if (newlyWaiting)
        {
            ExpandRows();
        }
        RowsUnderChanged();
    }

    /// <summary>
    /// What's under the row changed, or the fold did: the chevron, the badge and the rows follow. Once nothing is left
    /// under it, the fold goes, so the next row to come shows.
    /// </summary>
    private void RowsUnderChanged()
    {
        var has = HasRowsUnder;
        if (_hadRowsUnder && !has && State.RowsCollapsed)
        {
            State.RowsCollapsed = false;
            _services.SaveState();
        }
        _hadRowsUnder = has;
        OnPropertyChanged(nameof(HasRowsUnder));
        OnPropertyChanged(nameof(AreRowsCollapsed));
        OnPropertyChanged(nameof(ShowsSubagentRows));
        OnPropertyChanged(nameof(ShowsRuns));
        OnPropertyChanged(nameof(ShowsLogs));
        OnPropertyChanged(nameof(CollapseMenuText));
        OnPropertyChanged(nameof(CollapseTip));
        OnPropertyChanged(nameof(RowsUnderText));
        OnPropertyChanged(nameof(CollapsedRowCount));
        OnPropertyChanged(nameof(RowsUrgency));
        OnPropertyChanged(nameof(CollapsedGlyph));
        OnPropertyChanged(nameof(CollapsedIsBusy));
        OnPropertyChanged(nameof(CollapsedIsAlert));
        OnPropertyChanged(nameof(CollapsedIsError));
        OnPropertyChanged(nameof(CollapsedIsUnread));
        OnPropertyChanged(nameof(CollapsedTip));
        foreach (var subThread in SubThreads)
        {
            subThread.ThreadFoldChanged();
        }
    }
}
