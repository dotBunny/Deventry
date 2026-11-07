using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.Core.Claude;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;

namespace Deventry.App.ViewModels;

/// <summary>The working line above the composer while a turn runs (DESIGN.md §5, "Working line").</summary>
public sealed partial class TabViewModel
{
    private IReadOnlyList<string> _spinnerVerbs = SpinnerVerbs.BuiltIn;

    /// <summary>The reads of the verbs started, counted: only the latest one's verbs are kept.</summary>
    private int _spinnerVerbsReads;

    private readonly Lock _spinnerVerbsLock = new();

    /// <summary>The glyph, verb, time and tokens of the turn in progress. It only ticks while this tab is selected.</summary>
    public WorkingLine Working => field ??= CreateWorkingLine();

    private WorkingLine CreateWorkingLine()
    {
        var line = new WorkingLine(
            _services.Time,
            _services.Dispatcher,
            () => _spinnerVerbs,
            () => _services.Settings.Appearance.FunWorkingWords,
            () => Context.TurnTokens,
            () => _services.Tips.Text(Core.Settings.KeyboardShortcuts.Stop),
            _services.Random,
            () => _services.Settings.Appearance.ShowToolInWorkingLine,
            () => _services.ReduceMotion);
        line.SetShown(IsShown);
        return line;
    }

    /// <summary>The main agent's tool calls that haven't had their result yet, oldest first.</summary>
    private readonly List<(string Id, string Name, JsonObject Input)> _runningTools = [];

    /// <summary>
    /// Follows the main agent's tool calls for the working line: a call runs from its <c>tool_use</c> to its result.
    /// A subagent's own calls show on the agent map instead; its <c>Agent</c> call is what runs here.
    /// </summary>
    private void TrackToolsForWorkingLine(SessionEvent sessionEvent)
    {
        var changed = false;
        switch (sessionEvent)
        {
            case AssistantMessageReceived { Message: { ParentToolUseId: null } message }:
                foreach (var block in message.Content.OfType<ToolUseBlock>())
                {
                    if (_runningTools.All(t => t.Id != block.Id))
                    {
                        _runningTools.Add((block.Id, block.Name, block.Input));
                        changed = true;
                    }
                }
                break;
            case ToolResultsReceived { Message: { ParentToolUseId: null } results }:
                foreach (var result in results.Content.OfType<ToolResultBlock>())
                {
                    changed |= _runningTools.RemoveAll(t => t.Id == result.ToolUseId) > 0;
                }
                break;
            case TurnCompleted or SessionExited:
                changed = _runningTools.Count > 0;
                _runningTools.Clear();
                break;
        }
        if (changed)
        {
            (string Name, JsonObject Input)[] running = [.. _runningTools.Select(t => (t.Name, t.Input))];
            Working.SetActivity(ToolActivity.Describe(running), ToolActivity.Details(running));
            // What Joulene on the composer works with goes by the newest tool (DESIGN.md §5).
            TellMascot();
        }
    }

    /// <summary>Shown while Claude works; hidden while a prompt waits on the user, though the turn's time runs on.</summary>
    public bool IsWorkingLineShown => Status == TabStatus.Working;

    /// <summary>
    /// The row's running highlight (Settings → Appearance → Tab activity, DESIGN.md §4): while Claude works, and while its
    /// subagents do after the turn that started them in the background has ended.
    /// </summary>
    public bool ShowsWorkBar => UsesBars && (Status == TabStatus.Working || HasActiveAgents && Status is TabStatus.Idle or TabStatus.Unread);

    /// <summary>
    /// A still green wash on a row Claude finished in while the user wasn't looking, with Tab activity's bars; it waits
    /// for the subagents still running in the background.
    /// </summary>
    public bool ShowsDoneBar => UsesBars && !HasActiveAgents
        && (Status == TabStatus.Unread || State.Unseen && Status is TabStatus.NotStarted or TabStatus.Starting or TabStatus.Idle or TabStatus.Exited);

    internal bool UsesBars => _services.Settings.Appearance.TabActivity == Core.Settings.TabActivity.Bars;

    /// <summary>The status icon before the name: only with Tab activity's dots; with its bars the row says it all.</summary>
    public bool ShowsStatusGlyph => !UsesBars;

    /// <summary>
    /// The row's lead column, where the status icon and the mark sit: with Tab activity's bars and no mark it has
    /// nothing in it, and gives its room to the name.
    /// </summary>
    public bool HasRowLead => ShowsStatusGlyph || ShowsMarkIcon;

    public Avalonia.Controls.GridLength RowLeadWidth => new(HasRowLead ? 14 : 0);

    public double RowLeadSpacing => HasRowLead ? 6 : 0;

    /// <summary>
    /// Where the row's name line starts: after the status icon, or with Tab activity's bars over the lead column too,
    /// so a mark, under it level with the second line, moves only that line over.
    /// </summary>
    public int NameLineColumn => ShowsStatusGlyph ? 1 : 0;

    public int NameLineSpan => ShowsStatusGlyph ? 1 : 2;

    /// <summary>A still red line under a tab whose Claude Code stopped with an error, with Tab activity's bars.</summary>
    public bool ShowsErrorBar => Status == TabStatus.Error && UsesBars;

    /// <summary>The tab's name shimmering while its session starts and isn't ready yet, with Tab activity's bars.</summary>
    public bool ShowsStartingShimmer => Status == TabStatus.Starting && UsesBars;

    /// <summary>The row blinking orange while Claude waits for the user, with Tab activity's bars.</summary>
    public bool ShowsWaitBlink => Status == TabStatus.NeedsInput && _services.Settings.Appearance.TabActivity == Core.Settings.TabActivity.Bars;

    partial void OnStatusChanged(TabStatus oldValue, TabStatus newValue)
    {
        // How long an idle tab has been idle, for unloading it (DESIGN.md §9, "Unloading idle tabs").
        _statusSince = _services.Time.GetUtcNow();
        // Unseen outlives a restart, which brings the tab back not started; a new turn, or the user looking, ends it.
        if (newValue == TabStatus.Unread && !State.Unseen)
        {
            State.Unseen = true;
            _services.SaveState();
        }
        else if (newValue is TabStatus.Working or TabStatus.NeedsInput && State.Unseen)
        {
            State.Unseen = false;
            _services.SaveState();
        }
        switch (newValue)
        {
            case TabStatus.Working:
                Working.Start();
                break;
            case TabStatus.NeedsInput:
                break;
            default:
                Working.Stop();
                break;
        }
        OnPropertyChanged(nameof(IsWorkingLineShown));
        TellMascot();
        // The prompt cache's chip hides while a turn runs (DESIGN.md §6, "Prompt cache").
        RefreshPromptCache();
    }

    /// <summary>
    /// Reads the <c>spinnerVerbs</c> of Claude Code's settings for this folder, in the background, as the session
    /// starts: a verb added for the terminal shows here from the next turn. A read that finishes after a later one has
    /// started is dropped, so it can't put back what the later one found.
    /// </summary>
    internal Task LoadSpinnerVerbsAsync()
    {
        var files = SpinnerVerbs.SettingsFiles(_services.ClaudeConfigDirectory, Folder);
        var read = Interlocked.Increment(ref _spinnerVerbsReads);
        return Task.Run(() =>
        {
            var verbs = SpinnerVerbs.Resolve(files);
            // Checked and kept as one step: a later read that finishes meanwhile keeps its own after this.
            lock (_spinnerVerbsLock)
            {
                if (read == Volatile.Read(ref _spinnerVerbsReads))
                {
                    _spinnerVerbs = verbs;
                }
            }
        });
    }
}
