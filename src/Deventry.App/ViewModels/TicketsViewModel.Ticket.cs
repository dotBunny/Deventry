using Deventry.Core.Settings;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The ticket the Tickets tab shows (DESIGN.md §18, "Tickets", "A ticket"), <b>Work on it</b>, and writing back to its
/// tracker, which Settings turns on.
/// </summary>
public sealed partial class TicketsViewModel
{
    private int _detailVersion;

    /// <summary>The row chosen in the list.</summary>
    [ObservableProperty]
    public partial TicketRowViewModel? SelectedRow { get; set; }

    partial void OnSelectedRowChanged(TicketRowViewModel? value)
    {
        CommentText = "";
        IsCommenting = false;
        StateChange.Cancel();
        if (value is null)
        {
            return;
        }
        IsReading = true;
        OnFilterStateChanged();
        if (Detail is { } shown && shown.Source.Id == value.Source.Id && shown.Ticket.Id == value.Ticket.Id && shown.Ticket.Updated == value.Ticket.Updated)
        {
            return;
        }
        _ = LoadDetailAsync(value.Source, value.Ticket.Id);
    }

    /// <summary>The ticket shown, with its text and comments; null until one is read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(CanWorkOnIt))]
    public partial TicketDetailViewModel? Detail { get; private set; }

    public bool HasDetail => Detail is not null;

    [ObservableProperty]
    public partial bool IsDetailLoading { get; private set; }

    /// <summary>Why the ticket couldn't be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailError))]
    public partial string? DetailError { get; private set; }

    public bool HasDetailError => DetailError is not null;

    /// <summary>Reads a ticket with its comments; one a later read overtakes is dropped.</summary>
    private async Task LoadDetailAsync(TicketSource source, string id)
    {
        var version = ++_detailVersion;
        IsDetailLoading = true;
        DetailError = null;
        try
        {
            var tracker = await _services.Tickets.TrackerAsync(source.Provider, source.Server, _closing.Token).ConfigureAwait(true);
            var detail = await tracker.GetAsync(source, id, Services.TicketsService.MaxComments, _closing.Token).ConfigureAwait(true);
            if (version != _detailVersion || _disposed)
            {
                return;
            }
            Detail = new TicketDetailViewModel(source, detail, _services.Tickets.FindType(source.Provider), TabsOn(source, id), _services.Time);
            States = [];
            if (CanChange)
            {
                _ = LoadStatesAsync(Detail);
            }
        }
        catch (TicketException ex)
        {
            if (version == _detailVersion)
            {
                DetailError = ex.Message;
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (version == _detailVersion)
            {
                IsDetailLoading = false;
            }
        }
    }

    /// <summary><b>Open on GitHub</b>: the ticket's page in the browser.</summary>
    [RelayCommand]
    private Task OpenTicketInBrowserAsync() => Detail is { } detail && detail.Ticket.Url.Length > 0
        ? _services.Platform.OpenUrlAsync(detail.Ticket.Url)
        : Task.CompletedTask;

    [RelayCommand]
    private Task CopyLinkAsync() => Detail is { } detail ? _services.Platform.SetClipboardTextAsync(detail.Ticket.Url) : Task.CompletedTask;

    [RelayCommand]
    private Task CopyKeyAsync() => Detail is { } detail ? _services.Platform.SetClipboardTextAsync(detail.Ticket.Key) : Task.CompletedTask;

    /// <summary><b>Go to tab</b> on a ticket's <b>Tab on it</b> line.</summary>
    [RelayCommand]
    private void GoToTab(TabViewModel? tab)
    {
        if (tab is not null)
        {
            _shell.SelectedTab = tab;
        }
    }

    /// <summary>
    /// Opens the ticket that a tab works on (<b>Open ticket</b> in a tab's menu): its row chosen when the list has it,
    /// else the ticket read by itself.
    /// </summary>
    internal void Show(TicketRef ticket)
    {
        var choose = () =>
        {
            if (Rows.FirstOrDefault(r => ticket.Is(r.Source.Id, r.Ticket.Id)) is { } row)
            {
                SelectedRow = row;
            }
            else if (_sources.FirstOrDefault(s => string.Equals(s.Id, ticket.SourceId, StringComparison.OrdinalIgnoreCase)) is { } source)
            {
                SelectedRow = null;
                IsReading = true;
                _ = LoadDetailAsync(source, ticket.Id);
            }
        };
        State.SelectedSourceId = ticket.SourceId;
        State.SelectedId = ticket.Id;
        if (_started && !IsLoading)
        {
            choose();
        }
        else
        {
            _ = ShowWhenReadAsync(choose);
        }
    }

    private async Task ShowWhenReadAsync(Action choose)
    {
        if (!_started)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
        choose();
    }

    // ---- Work on it (DESIGN.md §18, "Tickets", "Work on it") -----------------------------------------------------------

    /// <summary>The ticket is read, so a tab can start on it.</summary>
    public bool CanWorkOnIt => Detail is not null;

    /// <summary>The group's folder is in git: <b>A new tab in a worktree</b> is offered.</summary>
    public bool CanUseWorktree => Group.CanMakeWorktrees;

    /// <summary>The button's tip: what a click does, as Settings → Tickets says.</summary>
    public string WorkOnItTip => _services.Settings.Tickets.WorkIn == TicketWorkIn.Worktree && CanUseWorktree
        ? "Start a tab on this ticket, in a worktree of its own"
        : "Start a tab on this ticket";

    /// <summary>
    /// <b>Work on it</b>: a new tab in the project's folder, or in a worktree named for the ticket, with the ticket in its
    /// message, ready to send. <paramref name="where"/> is <c>tab</c> or <c>worktree</c> from its menu; none does what
    /// Settings says.
    /// </summary>
    [RelayCommand]
    private Task WorkOnItAsync(string? where)
    {
        if (Detail is not { } detail)
        {
            return Task.CompletedTask;
        }
        var inWorktree = (where switch
        {
            "worktree" => true,
            "tab" => false,
            _ => _services.Settings.Tickets.WorkIn == TicketWorkIn.Worktree,
        }) && CanUseWorktree;
        return _shell.StartWorkOnTicketAsync(Group, detail, inWorktree);
    }

    // ---- Writing back (DESIGN.md §18, "Tickets", "Writing back") -------------------------------------------------------

    /// <summary>Settings → Tickets → the provider's <b>Let Deventry change tickets</b> is on.</summary>
    public bool CanChange => Detail is { } detail && _services.Tickets.AllowsChanges(detail.Source.Provider);

    partial void OnDetailChanged(TicketDetailViewModel? value) => OnPropertyChanged(nameof(CanChange));

    /// <summary>The states the ticket can be put in: <b>Close</b>, <b>Reopen</b>, YouTrack's states.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStates))]
    public partial IReadOnlyList<TicketStateChoice> States { get; private set; } = [];

    public bool HasStates => States.Count > 0;

    /// <summary>A change under way, or what went wrong with the last one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangeError))]
    public partial string? ChangeError { get; private set; }

    public bool HasChangeError => ChangeError is not null;

    [ObservableProperty]
    public partial bool IsChanging { get; private set; }

    private async Task LoadStatesAsync(TicketDetailViewModel detail)
    {
        try
        {
            var tracker = await _services.Tickets.TrackerAsync(detail.Source.Provider, detail.Source.Server, _closing.Token).ConfigureAwait(true);
            var states = await tracker.StatesAsync(detail.Source, detail.Ticket, _closing.Token).ConfigureAwait(true);
            if (ReferenceEquals(Detail, detail))
            {
                States = states;
            }
        }
        catch (TicketException ex)
        {
            if (ReferenceEquals(Detail, detail))
            {
                ChangeError = ex.Message;
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
    }

    /// <summary>A state that resolves the ticket asks first, in place: <i>Close #142 as completed?</i></summary>
    public InlineConfirmation<TicketStateChoice> StateChange => field ??= new InlineConfirmation<TicketStateChoice>(state => ChangeAsync(
        (tracker, detail) => tracker.SetStateAsync(detail.Source, detail.Ticket.Id, state, _closing.Token)));

    /// <summary>The question <see cref="StateChange"/> asks.</summary>
    public string StateChangeQuestion => StateChange.Value is { } state && Detail is { } detail ? $"{state.Label} — {detail.Key} {detail.Title}?" : "";

    /// <summary>A state picked in <b>⋯</b>: one that resolves the ticket asks first; the rest change it at once.</summary>
    [RelayCommand]
    private Task ChooseStateAsync(TicketStateChoice? state)
    {
        if (state is null)
        {
            return Task.CompletedTask;
        }
        if (state.IsResolved)
        {
            StateChange.Ask(state);
            OnPropertyChanged(nameof(StateChangeQuestion));
            return Task.CompletedTask;
        }
        return ChangeAsync((tracker, detail) => tracker.SetStateAsync(detail.Source, detail.Ticket.Id, state, _closing.Token));
    }

    /// <summary><b>Assign to me</b>.</summary>
    [RelayCommand]
    private Task AssignToMeAsync() => ChangeAsync((tracker, detail) => tracker.AssignToMeAsync(detail.Source, detail.Ticket.Id, _closing.Token));

    /// <summary><b>Comment…</b>: the box under the comments.</summary>
    [ObservableProperty]
    public partial bool IsCommenting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostCommentCommand))]
    public partial string CommentText { get; set; } = "";

    [RelayCommand]
    private void StartComment() => IsCommenting = true;

    [RelayCommand]
    private void CancelComment()
    {
        CommentText = "";
        IsCommenting = false;
    }

    /// <summary><b>Comment</b>: the comment goes to the tracker, and shows once it has it.</summary>
    [RelayCommand(CanExecute = nameof(CanPostComment))]
    private async Task PostCommentAsync()
    {
        var text = CommentText.Trim();
        if (await ChangeAsync((tracker, detail) => tracker.CommentAsync(detail.Source, detail.Ticket.Id, text, _closing.Token)))
        {
            CommentText = "";
            IsCommenting = false;
        }
    }

    private bool CanPostComment() => CommentText.Trim().Length > 0;

    /// <summary>A change to the ticket shown, then the ticket and its row read again. False when it failed, which says why.</summary>
    private async Task<bool> ChangeAsync(Func<ITicketTracker, TicketDetailViewModel, Task> change)
    {
        if (Detail is not { } detail || !CanChange)
        {
            return false;
        }
        IsChanging = true;
        ChangeError = null;
        try
        {
            var tracker = await _services.Tickets.TrackerAsync(detail.Source.Provider, detail.Source.Server, _closing.Token).ConfigureAwait(true);
            await change(tracker, detail).ConfigureAwait(true);
        }
        catch (TicketException ex)
        {
            ChangeError = ex.Message;
            return false;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsChanging = false;
        }
        await LoadDetailAsync(detail.Source, detail.Ticket.Id).ConfigureAwait(true);
        if (Detail is { } changed && Rows.FirstOrDefault(r => r.Source.Id == changed.Source.Id && r.Ticket.Id == changed.Ticket.Id) is { } row)
        {
            var index = Rows.IndexOf(row);
            var updated = Row(changed.Source, changed.Ticket);
            Rows[index] = updated;
            SelectedRow = updated;
        }
        return true;
    }
}
