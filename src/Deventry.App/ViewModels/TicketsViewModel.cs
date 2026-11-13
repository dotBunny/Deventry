using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Protocol;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// A folder group's Tickets tab (DESIGN.md §18, "Tickets"): the project's tickets from the trackers it uses, the one
/// chosen with its text and comments, and <b>Work on it</b>, which starts a tab on it. It's a tab like a session's in the
/// sidebar and the panes, without a session, as the Project Files tab is. Partials hold the list (<c>.List.cs</c>) and
/// the ticket, with writing back (<c>.Ticket.cs</c>).
/// </summary>
public sealed partial class TicketsViewModel : ViewModelBase, IShellTab, IMarkdownHost, IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _closing = new();
    private bool _started;
    private bool _disposed;

    internal TicketsViewModel(AppServices services, ShellViewModel shell, TabGroupViewModel group, TicketsTabState state)
    {
        _services = services;
        _shell = shell;
        Group = group;
        State = state;
        Folder = state.Folder;
        _filterWait = new UiTimeout(services.Time, services.Dispatcher);
        _stateWait = new UiTimeout(services.Time, services.Dispatcher);
        _stateFilter = state.State;
        _mine = state.Mine;
        _sort = state.Sort;
        _filterText = state.Filter;
        _services.Tickets.Changed += OnTicketsServiceChanged;
        _services.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>What's saved for the tab; <see cref="CaptureState"/> brings it up to date.</summary>
    internal TicketsTabState State { get; }

    /// <summary>The group it belongs to, whose row it's under the Project Files tab's.</summary>
    public TabGroupViewModel Group { get; }

    public string Id => State.Id;

    /// <summary>The project's folder: the group's.</summary>
    public string Folder { get; }

    public string GroupFolder => Folder;

    public string DisplayName => "Tickets";

    // ---- In the sidebar ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Its row's second line: how many shown tickets are open, with those opened since it was last looked at; or that
    /// it's loading, has no sources, or what went wrong.
    /// </summary>
    public string RowDetail
    {
        get
        {
            if (!_started)
            {
                return "Not read yet";
            }
            if (IsLoading && Rows.Count == 0)
            {
                return "Loading…";
            }
            if (ListError is { } error && Rows.Count == 0)
            {
                return error;
            }
            if (!HasSources)
            {
                return "No ticket sources";
            }
            var open = Rows.Count(r => r.Ticket.IsOpen);
            var text = $"{open}{(CanLoadMore ? "+" : "")} open";
            return NewCount > 0 ? $"{text} · {NewCount} new" : text;
        }
    }

    /// <summary>The row's second line is a problem, in the caution color.</summary>
    public bool RowIsWarning => ListError is not null && Rows.Count == 0;

    /// <summary>The row's tip.</summary>
    public string RowTip => $"The tickets of {Group.Label}";

    /// <summary>Tickets opened since the tab last showed them.</summary>
    public int NewCount => State.LastSeen is { } seen ? Rows.Count(r => r.Ticket.Created > seen) : 0;

    private void OnRowStateChanged()
    {
        OnPropertyChanged(nameof(RowDetail));
        OnPropertyChanged(nameof(RowIsWarning));
        OnPropertyChanged(nameof(NewCount));
    }

    // ---- Selection and panes (DESIGN.md §18, "Split panes") --------------------------------------------------------

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!IsInSplit)
        {
            IsShown = value;
        }
    }

    [ObservableProperty]
    public partial bool IsShown { get; set; }

    partial void OnIsShownChanged(bool value)
    {
        if (value)
        {
            Shown();
        }
        else if (_started)
        {
            // What was shown has been seen.
            State.LastSeen = _services.Time.GetUtcNow();
            OnFilterStateChanged();
        }
    }

    [ObservableProperty]
    public partial bool IsInSplit { get; set; }

    [ObservableProperty]
    public partial int PaneIndex { get; set; } = -1;

    [ObservableProperty]
    public partial Avalonia.Media.Color? PaneColor { get; set; }

    [ObservableProperty]
    public partial bool IsDragged { get; set; }

    /// <summary>A click or the focus in its pane: the keyboard and the header follow it now.</summary>
    public void FocusPane()
    {
        if (IsShown && !IsSelected)
        {
            _shell.Selected = this;
        }
    }

    [RelayCommand]
    private void ClosePane() => _shell.ClosePaneCommand.Execute(this);

    /// <summary>Its row's × and menu's <b>Close</b>.</summary>
    [RelayCommand]
    private void Close() => _shell.CloseTickets(this);

    /// <summary>
    /// It came into view: the first time, its tickets are read; after that, again when the last read is more than five
    /// minutes old, keeping the ticket that's chosen.
    /// </summary>
    private void Shown()
    {
        if (!_started)
        {
            _started = true;
            _ = RefreshAsync();
            return;
        }
        if (_lastRead is { } read && _services.Time.GetUtcNow() - read > StaleAfter && !IsLoading)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>How old a read may be when the tab comes back into view before it's read again.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    // ---- Narrow panes ---------------------------------------------------------------------------------------------------

    /// <summary>Its pane is too narrow for the list and the ticket side by side; the view says so.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsList), nameof(ShowsTicket))]
    public partial bool IsNarrow { get; set; }

    /// <summary>In a narrow pane, the ticket has the list's place, with <b>← Tickets</b> over it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsList), nameof(ShowsTicket))]
    public partial bool IsReading { get; set; }

    public bool ShowsList => !IsNarrow || !IsReading;

    public bool ShowsTicket => !IsNarrow || IsReading;

    /// <summary><b>← Tickets</b>: back to the list, in a narrow pane.</summary>
    [RelayCommand]
    private void BackToList() => IsReading = false;

    // ---- Kept with the app state -------------------------------------------------------------------------------------

    private readonly UiTimeout _stateWait;

    /// <summary>Something saved with the tab changed: the state is written once things settle.</summary>
    private void OnFilterStateChanged() => _stateWait.Restart(TimeSpan.FromSeconds(1), () =>
    {
        CaptureState();
        _services.SaveState();
    });

    /// <summary>Brings <see cref="State"/> up to date: the source, the filter and the ticket shown.</summary>
    internal TicketsTabState CaptureState()
    {
        State.State = StateFilter;
        State.Mine = Mine;
        State.Sort = Sort;
        State.Filter = FilterText;
        State.AllSources = SelectedSource is { IsAll: true };
        State.SourceId = SelectedSource?.Source?.Id ?? State.SourceId;
        if (SelectedRow is { } row)
        {
            State.SelectedSourceId = row.Source.Id;
            State.SelectedId = row.Ticket.Id;
        }
        return State;
    }

    private void OnTicketsServiceChanged() => _services.Dispatcher.Post(() =>
    {
        OnPropertyChanged(nameof(CanFilterMine));
        OnPropertyChanged(nameof(CanChange));
    });

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(WorkOnItTip));
    }

    // ---- Markdown (IMarkdownHost) --------------------------------------------------------------------------------------

    private ReplyImages? _replyImages;

    /// <summary>A ticket's images: none in the project's files, so every one on the web waits for <b>Load image</b>.</summary>
    public ReplyImages ReplyImages => _replyImages ??= new ReplyImages(new ReplyImageLoader(_services.Http, _services.Time), () => Folder);

    public void ShowImage(ToolImage image) => _shell.ShowImage(image);

    private CopiedMarks? _copied;

    public async Task CopyCodeAsync(string code, object block, Action<bool> showCopied)
    {
        await _services.Platform.SetClipboardTextAsync(code.TrimEnd('\r', '\n'));
        (_copied ??= new CopiedMarks(_services.Time, _services.Dispatcher)).Show(block, showCopied);
    }

    /// <summary>
    /// A link in a ticket's text: a web address opens in the browser, or the image window for an image. Nothing else
    /// does: a ticket is anyone's text, and its paths aren't this computer's.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenLinkAsync(object? link)
    {
        var url = link is LiveMarkdown.Avalonia.LinkClickedEventArgs clicked ? clicked.HRef?.OriginalString : link?.ToString();
        return url is { Length: > 0 } && Uri.TryCreate(url, UriKind.Absolute, out var uri) && Core.ProjectTools.ProjectLinks.AllowedSchemes.Contains(uri.Scheme)
            ? _shell.OpenWebLinkAsync(uri, ReplyImages)
            : Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        _services.Tickets.Changed -= OnTicketsServiceChanged;
        _services.SettingsChanged -= OnSettingsChanged;
        _closing.Cancel();
        _closing.Dispose();
        _filterWait.Dispose();
        _stateWait.Dispose();
        return ValueTask.CompletedTask;
    }
}
