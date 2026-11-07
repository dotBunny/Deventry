using Deventry.App.Conversation;
using Deventry.Core.Claude;
using Deventry.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The tab's running tasks (DESIGN.md §5, "Running tasks"): work Claude Code keeps going in the background, such as a
/// backgrounded shell command, a background subagent, a Monitor watch or a remote agent. A chip in the composer bar
/// lists them, but for subagents, which the Agents button and page have; the tab's row counts them all once the turn
/// is over (DESIGN.md §4), and the info card has a row for those the chip lists. And how far Claude's own task list has
/// got (DESIGN.md §5, "Tasks"): the row's badge and line, going to where a task started, and <b>Continue</b>; and keeping
/// that list in step with the one Claude Code keeps.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>A running task's time ticks every second while the list shows.</summary>
    private UiTicker TaskTicker => field ??= new(_services.Time, _services.Dispatcher, TimeSpan.FromSeconds(1), Tasks.Tick);

    /// <summary>Kept by the conversation builder from Claude Code's task messages.</summary>
    public RunningTasks Tasks { get; }

    /// <summary>Every running task, subagents too: the tab row's count.</summary>
    public int RunningTaskCount => Tasks.Count;

    /// <summary>The composer bar shows the chip: tasks other than subagents are running.</summary>
    public bool HasRunningTasks => Tasks.Listed.Count > 0;

    /// <summary>"1 running task", "3 running tasks": the chip, and the head of its list.</summary>
    public string RunningTasksText => Tasks.Listed.Count == 1 ? "1 running task" : $"{Tasks.Listed.Count} running tasks";

    /// <summary>
    /// The row's count once the turn is over: every running task, less the subagents while their own chip counts them
    /// (DESIGN.md §4, "Sidebar").
    /// </summary>
    public int RowTaskCount => ShowsAgentsBadge ? Tasks.Listed.Count : Tasks.Count;

    /// <summary>The turn is over but tasks are still running: the tab's row shows how many, from any tab.</summary>
    public bool ShowTaskBadge => RowTaskCount > 0 && !IsWorking;

    /// <summary>"2 agents and 1 task still running": the row's count, subagents told apart.</summary>
    public string TaskBadgeTip
    {
        get
        {
            var (agents, others) = (ShowsAgentsBadge ? 0 : Tasks.AgentCount, Tasks.Listed.Count);
            var parts = new List<string>(2);
            if (agents > 0)
            {
                parts.Add(agents == 1 ? "1 agent" : $"{agents} agents");
            }
            if (others > 0 || agents == 0)
            {
                parts.Add(others == 1 ? "1 task" : $"{others} tasks");
            }
            return $"{string.Join(" and ", parts)} still running";
        }
    }

    /// <summary>The chip's list is open: running times tick every second, from the injected clock.</summary>
    [ObservableProperty]
    public partial bool IsTaskListOpen { get; set; }

    partial void OnIsTaskListOpenChanged(bool value)
    {
        if (value)
        {
            Tasks.Tick();
        }
        UpdateTaskTicker();
    }

    /// <summary>Clicking a task: the card of the call that started it, with the groups it's nested in expanded.</summary>
    [RelayCommand]
    private void ShowTask(RunningTask? task)
    {
        if (task?.Tool is not { } tool)
        {
            return;
        }
        if (task.Agent is { Item: not null } node)
        {
            ShowAgent(node);
            return;
        }
        for (var owner = Agents.OwnerOf(tool.ToolUseId); owner?.Item is { } group; owner = owner.Parent)
        {
            group.IsExpanded = true;
        }
        ScrollToRequested?.Invoke(tool);
    }

    /// <summary>
    /// Stops one task, after confirming, through Claude Code's <c>stop_task</c>, so Claude is told it ended: the same
    /// path as the process monitor's Stop (DESIGN.md §4) and, for a subagent, the agent map's (DESIGN.md §18).
    /// </summary>
    [RelayCommand]
    private void StopTask(RunningTask? task)
    {
        if (task is null || _session is not { } session)
        {
            return;
        }
        if (task.Agent is { CanStop: true } node)
        {
            StopAgent(node);
            return;
        }
        _shell.Confirm(
            $"Stop \"{task.Title}\"?",
            task.Kind switch
            {
                TaskKind.Shell => "Claude Code stops this background command and tells Claude it ended.",
                TaskKind.Monitor => "Claude Code ends this watch and tells Claude it was stopped.",
                TaskKind.Agent => "Claude Code stops this subagent, and any subagents it started, and tells Claude it was stopped.",
                _ => "Claude Code stops this task and tells Claude it ended.",
            },
            "Stop",
            async () =>
            {
                try
                {
                    await session.StopTaskAsync(task.TaskId);
                }
                catch (Exception ex)
                {
                    _conversation.AddNote($"Couldn't stop {task.Title}: {ex.Message}", NoteKind.Error);
                }
            });
    }

    /// <summary>The row's count and its tip, which leave subagents out while their chip shows.</summary>
    private void OnRowTasksChanged()
    {
        OnPropertyChanged(nameof(RowTaskCount));
        OnPropertyChanged(nameof(ShowTaskBadge));
        OnPropertyChanged(nameof(TaskBadgeTip));
    }

    private void OnTasksChanged()
    {
        OnPropertyChanged(nameof(RunningTaskCount));
        OnPropertyChanged(nameof(HasRunningTasks));
        OnPropertyChanged(nameof(RunningTasksText));
        OnRowTasksChanged();
        OnPropertyChanged(nameof(InfoRows));
        UpdateTaskTicker();
        // Work going on in the background can keep the computer awake (DESIGN.md §9, "Keeping the computer awake").
        _shell.OnTabWorkChanged();
    }

    private void UpdateTaskTicker() => TaskTicker.Run(IsTaskListOpen && HasRunningTasks);

    // ---- Claude's task list and plan (DESIGN.md §5, "Tasks") ---------------------------------------------------------

    /// <summary>Settings → Conversation → Show task progress.</summary>
    private bool TaskProgressOnRows => _services.Settings.Appearance.ShowTaskProgressOnTabs;

    /// <summary>The row's "3/7" badge: Claude's tasks aren't all done, and Settings shows it.</summary>
    public bool ShowTaskProgressBadge => TaskProgressOnRows && TodoList.HasTasksLeft;

    /// <summary>The task the row's second line says while the tab works with it in progress; null otherwise.</summary>
    private string? WorkingTaskText => TaskProgressOnRows && Status == TabStatus.Working && TodoList.Current is { } current ? current.DisplayText : null;

    /// <summary>The latest row at a turn's end about the task list: the one that may offer <b>Continue</b>.</summary>
    private TasksSummaryItem? _tasksSummary;

    private void OnTodoListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Conversation.TodoList.HasTasksLeft):
                OnPropertyChanged(nameof(ShowTaskProgressBadge));
                // The last of Claude's tasks just done, as it works (not a transcript loading): confetti (DESIGN.md §5).
                if (TodoList.AllDone && Status is TabStatus.Working or TabStatus.NeedsInput)
                {
                    TellMascot(mascot => mascot.AllTasksDone());
                }
                SaveTodoListClosed();
                break;
            case nameof(Conversation.TodoList.IsExpanded):
                SaveTodoListClosed();
                break;
            case nameof(Conversation.TodoList.Current) or nameof(Conversation.TodoList.InfoText):
                OnPropertyChanged(nameof(RowDetail));
                OnPropertyChanged(nameof(InfoRows));
                break;
        }
    }

    /// <summary>The user closed the pinned list while it has tasks left: kept with the tab, so a restart leaves it closed.</summary>
    private void SaveTodoListClosed()
    {
        var closed = !TodoList.IsExpanded && TodoList.HasTasksLeft;
        if (State.TodoListClosed != closed)
        {
            State.TodoListClosed = closed;
            _services.SaveState();
        }
    }

    /// <summary>
    /// The pinned list as the user left it, once a restored tab's conversation is read back: reading it opens a list with
    /// tasks left, so one they'd closed closes again.
    /// </summary>
    private void RestoreTodoListClosed(bool closed)
    {
        if (closed && TodoList.HasTasksLeft)
        {
            TodoList.IsExpanded = false;
        }
    }

    /// <summary>A turn ended: only its row about the task list, if it has one, offers <b>Continue</b>, and only with tasks left.</summary>
    private void OnTurnEndedForTasks()
    {
        WithdrawContinue();
        if (_conversation.TurnTasksSummary is { } summary)
        {
            summary.IsContinueOffered = !summary.AllDone;
            _tasksSummary = summary;
        }
    }

    /// <summary>A turn started: <b>Continue</b> isn't offered any more.</summary>
    private void WithdrawContinue()
    {
        if (_tasksSummary is not null)
        {
            _tasksSummary.IsContinueOffered = false;
        }
    }

    /// <summary>The row's badge: selects the tab and shows the Tasks tool window.</summary>
    [RelayCommand]
    private void ShowTasksFromRow()
    {
        _shell.SelectedTab = this;
        ShowToolWindow(ToolWindows.ToolWindowKinds.Tasks);
    }

    /// <summary>A task on the Tasks page or the pinned list: where it started in the conversation, its groups expanded.</summary>
    [RelayCommand]
    private void GoToTask(TodoItem? task)
    {
        if (task?.Start is not { } row)
        {
            return;
        }
        Expand(Items, row);
        ScrollToRequested?.Invoke(row);

        static bool Expand(IEnumerable<ConversationItem> items, ConversationItem target)
        {
            foreach (var item in items)
            {
                if (ReferenceEquals(item, target))
                {
                    return true;
                }
                if (item is SubagentItem group && Expand(group.Items, target))
                {
                    group.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>A file a task changed: in the diff view, as Changed files shows it.</summary>
    [RelayCommand]
    private Task OpenTaskFileAsync(TaskFileRow? file) =>
        file is null ? Task.CompletedTask : ChangedFiles.OpenChangeDiffAsync(file.ToolUseId);

    /// <summary>The pinned list's <b>Use as the plan</b>: the suggested reply, as <b>Show as the plan</b> makes it.</summary>
    [RelayCommand]
    private void UsePlanSuggestion()
    {
        if (TodoList.SuggestedReply is { } reply)
        {
            ShowAsPlan(reply);
        }
        TodoList.DismissSuggestion();
    }

    [RelayCommand]
    private void DismissPlanSuggestion() => TodoList.DismissSuggestion();

    // ---- The list Claude Code keeps (DESIGN.md §5, "Tasks") ---------------------------------------------------------

    /// <summary>Keeps the task list in step with the folder Claude Code keeps it in, which is the list Claude sees.</summary>
    private TodoListFolder TaskFolder => field ??= new(TodoList, _services.FolderWatcher, _services.Dispatcher);

    /// <summary>The folder of Claude Code's task list the tab reads; null while it reads none, or it hasn't this session's list.</summary>
    internal string? TaskFolderRead => TaskFolder.Following;

    /// <summary><c>CLAUDE_CODE_TASK_LIST_ID</c> as the tab's <c>claude</c> has it, once read as it starts; null when it's unset.</summary>
    private string? _taskListVariable;

    /// <summary>The tab's <c>claude</c> has started, and which list it uses has been read.</summary>
    private bool _taskListKnown;

    /// <summary>
    /// Reads, in the background as the session starts, which list it uses: <c>CLAUDE_CODE_TASK_LIST_ID</c> from the
    /// environment it starts with and its settings files' <c>env</c>, else its own. Then follows that list's folder.
    /// </summary>
    private async Task LoadTaskListAsync()
    {
        _taskListKnown = false;
        var files = ClaudeSettingsFiles.ForSession(_services.ClaudeConfigDirectory, State.WithoutProjectSettings ? null : Folder);
        string? variable;
        try
        {
            var environment = await _services.ClaudeEnvironmentFor(_claudeVariables, _closing.Token).ConfigureAwait(false);
            variable = await Task.Run(() => ClaudeTaskFolder.ListVariableFor(environment, files), _closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        _services.Dispatcher.Post(() =>
        {
            _taskListVariable = variable;
            _taskListKnown = true;
            FollowTaskFolder();
        });
    }

    /// <summary>
    /// Follows the folder of the list the session uses, named for the session unless <c>CLAUDE_CODE_TASK_LIST_ID</c>
    /// names another. None while Claude Code isn't running, before a copy has its own id, or before Claude Code's config
    /// folder is known.
    /// </summary>
    private void FollowTaskFolder() =>
        TaskFolder.Follow(_session is not null && _taskListKnown && !_forkAwaitingId && _services.ClaudeConfigDirectory is { } config && State.SessionId is { } sessionId
            ? ClaudeTaskFolder.Folder(config, ClaudeTaskFolder.ListId(sessionId, _taskListVariable))
            : null);

    /// <summary>The message <b>Continue</b> sends, as the user's own.</summary>
    internal const string ContinueTasksMessage = "Carry on with the tasks that are left.";

    /// <summary><b>Continue</b> on the row at a turn's end: asks Claude to carry on with the tasks that are left.</summary>
    [RelayCommand]
    private async Task ContinueTasksAsync(TasksSummaryItem? row)
    {
        if (row is not { IsContinueOffered: true } || IsWorking || IsReadOnly)
        {
            return;
        }
        row.IsContinueOffered = false;
        _autoContinue.UserSent();
        var stamp = NewStamp(fromUser: true);
        var card = _conversation.AddUserMessage(ContinueTasksMessage);
        card.SentId = stamp.Uuid;
        if (HoldForCredits(card, new PendingMessage(ContinueTasksMessage, [], null, stamp)))
        {
            return;
        }
        await SendRawAsync(ContinueTasksMessage, stamp: stamp);
    }
}
