using System.Collections.ObjectModel;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>A choice in the Tickets tab's source menu: one of the project's sources, or <b>All sources</b>.</summary>
public sealed record TicketSourceChoice(TicketSource? Source, string Name)
{
    public bool IsAll => Source is null;

    public override string ToString() => Name;
}

/// <summary>A choice in one of the list's menus: its value, and what it says.</summary>
public sealed record TicketChoice<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>The Tickets tab's list (DESIGN.md §18, "Tickets", "The list"): its sources, its filter, and reading them.</summary>
public sealed partial class TicketsViewModel
{
    private readonly UiTimeout _filterWait;
    private int _listVersion;
    private DateTimeOffset? _lastRead;
    private readonly Dictionary<string, string?> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TicketSource> _sources = [];

    // ---- The sources ---------------------------------------------------------------------------------------------------

    /// <summary>The source menu: each of the project's sources, then <b>All sources</b> when there are several.</summary>
    public ObservableCollection<TicketSourceChoice> SourceChoices { get; } = [];

    public bool HasSeveralSources => SourceChoices.Count > 1;

    public bool HasSources => _sources.Count > 0;

    /// <summary>The source shown.</summary>
    [ObservableProperty]
    public partial TicketSourceChoice? SelectedSource { get; set; }

    private bool _choosingSources;

    partial void OnSelectedSourceChanged(TicketSourceChoice? value)
    {
        if (_choosingSources || value is null)
        {
            return;
        }
        OnPropertyChanged(nameof(FilterTip));
        OnFilterStateChanged();
        _ = RefreshAsync(readSources: false);
    }

    /// <summary>Entries of the project's files that couldn't be read as sources, and why.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourceProblems))]
    public partial IReadOnlyList<string> SourceProblems { get; private set; } = [];

    public bool HasSourceProblems => SourceProblems.Count > 0;

    /// <summary>The sources the list shows: the chosen one, or all of them.</summary>
    private IReadOnlyList<TicketSource> ShownSources => SelectedSource is { Source: { } one } ? [one] : _sources;

    /// <summary>The filter box's tip: the tracker's own search words.</summary>
    public string FilterTip => ShownSources.Select(s => _services.Tickets.FindType(s.Provider)?.FilterTip).OfType<string>().Distinct().FirstOrDefault()
        ?? "Filter the tickets";

    // ---- The filter ----------------------------------------------------------------------------------------------------

    public static IReadOnlyList<TicketChoice<TicketStateFilter>> StateChoices { get; } =
        [new(TicketStateFilter.Open, "Open"), new(TicketStateFilter.Closed, "Closed"), new(TicketStateFilter.All, "All")];

    public static IReadOnlyList<TicketChoice<TicketSort>> SortChoices { get; } =
        [new(TicketSort.Newest, "Newest"), new(TicketSort.Updated, "Recently updated"), new(TicketSort.Oldest, "Oldest")];

    private TicketStateFilter _stateFilter;

    /// <summary><b>Open</b>, <b>Closed</b> or <b>All</b>.</summary>
    public TicketStateFilter StateFilter
    {
        get => _stateFilter;
        set
        {
            if (SetProperty(ref _stateFilter, value))
            {
                OnPropertyChanged(nameof(SelectedState));
                FilterChanged();
            }
        }
    }

    public TicketChoice<TicketStateFilter> SelectedState
    {
        get => StateChoices.First(c => c.Value == StateFilter);
        set => StateFilter = value?.Value ?? TicketStateFilter.Open;
    }

    private TicketSort _sort;

    /// <summary><b>Sort</b>.</summary>
    public TicketSort Sort
    {
        get => _sort;
        set
        {
            if (SetProperty(ref _sort, value))
            {
                OnPropertyChanged(nameof(SelectedSort));
                FilterChanged();
            }
        }
    }

    public TicketChoice<TicketSort> SelectedSort
    {
        get => SortChoices.First(c => c.Value == Sort);
        set => Sort = value?.Value ?? TicketSort.Newest;
    }

    private bool _mine;

    /// <summary><b>Mine</b>: only those assigned to the account it's signed in as.</summary>
    public bool Mine
    {
        get => _mine;
        set
        {
            if (SetProperty(ref _mine, value))
            {
                FilterChanged();
            }
        }
    }

    /// <summary><b>Mine</b> is offered: every source shown is on a server signed in as someone.</summary>
    public bool CanFilterMine => ShownSources.Count > 0 && ShownSources.All(s => _services.Tickets.StateOf(s.Provider, s.Server).Account is not null);

    private string _filterText;

    /// <summary>The filter box: it filters half a second after typing stops.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? ""))
            {
                _filterWait.Restart(TimeSpan.FromSeconds(0.5), () => FilterChanged());
            }
        }
    }

    private void FilterChanged()
    {
        _filterWait.Cancel();
        OnFilterStateChanged();
        if (_started)
        {
            _ = RefreshAsync(readSources: false);
        }
    }

    // ---- The rows ------------------------------------------------------------------------------------------------------

    public ObservableCollection<TicketRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>A read is under way.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    partial void OnIsLoadingChanged(bool value) => OnRowStateChanged();

    /// <summary>What went wrong reading, with what to do; null when nothing did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListError))]
    public partial string? ListError { get; private set; }

    partial void OnListErrorChanged(string? value) => OnRowStateChanged();

    public bool HasListError => ListError is not null;

    /// <summary>The error can be fixed in Settings: a token refused, or a limit a sign-in would raise.</summary>
    [ObservableProperty]
    public partial bool ErrorOffersSettings { get; private set; }

    /// <summary>A source has more tickets to read: <b>Load more</b>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownText))]
    public partial bool CanLoadMore { get; private set; }

    /// <summary>Under the list: <i>50 shown</i>.</summary>
    public string ShownText => $"{Rows.Count} shown";

    /// <summary>What the list says with nothing in it, and why; null while it has rows, is loading or has an error.</summary>
    public string? EmptyText => IsLoading || ListError is not null || Rows.Count > 0 ? null
        : !HasSources ? "No ticket sources for this project."
        : FilterText.Trim().Length > 0 ? $"Nothing matches \"{FilterText.Trim()}\"."
        : $"{StateFilter switch
        {
            TicketStateFilter.Closed => "No closed tickets.",
            TicketStateFilter.All => "No tickets.",
            _ => Mine ? "No open tickets are yours." : "No open tickets.",
        }}{(GuestSource is { } guest ? $" {GuestNote(guest)}" : "")}";

    public bool HasEmptyText => EmptyText is not null;

    /// <summary>With nothing to show from a server read as its guest: its provider's settings, for a token.</summary>
    public bool OffersTokenSettings => HasEmptyText && HasSources && GuestSource is not null;

    /// <summary>A source shown that's read without a token by a provider that signs in with one, as YouTrack's guest is.</summary>
    private TicketSource? GuestSource => ShownSources.FirstOrDefault(s =>
        SignInOf(s) == TicketSignIn.Token && _services.Tickets.HasToken(s.Provider, s.Server) != true);

    /// <summary>What to do when a project may be private: <i>"Set a token for dotbunny.youtrack.cloud in Settings → Tickets → YouTrack."</i></summary>
    private string GuestNote(TicketSource source) =>
        $"Deventry reads {source.ServerName} as its guest: a private project needs a token for it, in Settings → Tickets → {_services.Tickets.FindType(source.Provider)?.Name ?? source.Provider}.";

    /// <summary>With no sources: <b>Add a ticket source…</b>.</summary>
    public bool OffersAddSource => !IsLoading && !HasSources;

    private void OnListShapeChanged()
    {
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasEmptyText));
        OnPropertyChanged(nameof(OffersTokenSettings));
        OnPropertyChanged(nameof(OffersAddSource));
        OnPropertyChanged(nameof(ShownText));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasSeveralSources));
        OnPropertyChanged(nameof(CanFilterMine));
        OnPropertyChanged(nameof(FilterTip));
        OnRowStateChanged();
    }

    /// <summary><b>Refresh</b> (⟳, <c>F5</c>): the sources from the project's files, and their tickets, again.</summary>
    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    /// <summary>
    /// Reads the list again: the project's sources first unless <paramref name="readSources"/> is false, then the first
    /// page of each shown, keeping the ticket chosen. A read a later one overtakes drops what it read.
    /// </summary>
    internal async Task RefreshAsync(bool readSources = true)
    {
        if (_disposed)
        {
            return;
        }
        _started = true;
        var version = ++_listVersion;
        IsLoading = true;
        OnListShapeChanged();
        if (readSources || _sources.Count == 0)
        {
            var found = await Task.Run(() => _services.Tickets.SourcesFor(Folder)).ConfigureAwait(true);
            if (version != _listVersion || _disposed)
            {
                return;
            }
            UseSources(found);
        }
        var query = new TicketQuery(StateFilter, Mine && CanFilterMine, FilterText.Trim(), Sort);
        var sources = ShownSources;
        var pages = await Task.WhenAll(sources.Select(s => ReadAsync(s, query, null))).ConfigureAwait(true);
        if (version != _listVersion || _disposed)
        {
            return;
        }
        _cursors.Clear();
        foreach (var (source, page, _) in pages)
        {
            _cursors[source.Id] = page?.Next;
        }
        var tickets = Merge(pages.Where(p => p.Page is not null).SelectMany(p => p.Page!.Tickets.Select(t => (p.Source, t))));
        var keep = SelectedRow is { } chosen ? (chosen.Source.Id, chosen.Ticket.Id) : (State.SelectedSourceId, State.SelectedId);
        Rows.Clear();
        foreach (var (source, ticket) in tickets)
        {
            Rows.Add(Row(source, ticket));
        }
        UseError(pages.FirstOrDefault(p => p.Error is not null));
        CanLoadMore = _cursors.Values.Any(c => c is not null);
        _lastRead = _services.Time.GetUtcNow();
        IsLoading = false;
        SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Source.Id, keep.Item1, StringComparison.OrdinalIgnoreCase) && r.Ticket.Id == keep.Item2)
            ?? (IsNarrow ? null : SelectedRow is null ? null : Rows.FirstOrDefault());
        if (SelectedRow is null && Detail is not null && !Rows.Any(r => r.Source.Id == Detail.Source.Id && r.Ticket.Id == Detail.Ticket.Id))
        {
            // The ticket shown stays while it's being read, then goes once the list no longer has it.
            Detail = null;
        }
        OnListShapeChanged();
        if (IsShown)
        {
            State.LastSeen ??= _lastRead;
        }
    }

    /// <summary><b>Load more</b>: the next page of each source that has one, after the rows there are.</summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var version = ++_listVersion;
        IsLoading = true;
        var query = new TicketQuery(StateFilter, Mine && CanFilterMine, FilterText.Trim(), Sort);
        var more = ShownSources.Where(s => _cursors.GetValueOrDefault(s.Id) is not null).ToArray();
        var pages = await Task.WhenAll(more.Select(s => ReadAsync(s, query, _cursors[s.Id]))).ConfigureAwait(true);
        if (version != _listVersion || _disposed)
        {
            return;
        }
        foreach (var (source, page, _) in pages)
        {
            _cursors[source.Id] = page?.Next;
        }
        var known = Rows.Select(r => (r.Source.Id, r.Ticket.Id)).ToHashSet();
        foreach (var (source, ticket) in Merge(pages.Where(p => p.Page is not null).SelectMany(p => p.Page!.Tickets.Select(t => (p.Source, t)))))
        {
            if (known.Add((source.Id, ticket.Id)))
            {
                Rows.Add(Row(source, ticket));
            }
        }
        UseError(pages.FirstOrDefault(p => p.Error is not null));
        CanLoadMore = _cursors.Values.Any(c => c is not null);
        IsLoading = false;
        OnListShapeChanged();
    }

    /// <summary>One source's page, or why it couldn't be read.</summary>
    private async Task<(TicketSource Source, TicketPage? Page, TicketException? Error)> ReadAsync(TicketSource source, TicketQuery query, string? cursor)
    {
        try
        {
            var tracker = await _services.Tickets.TrackerAsync(source.Provider, source.Server, _closing.Token).ConfigureAwait(false);
            return (source, await tracker.ListAsync(source, query, cursor, _closing.Token).ConfigureAwait(false), null);
        }
        catch (TicketException ex)
        {
            if (ex.Kind == TicketErrorKind.Refused)
            {
                _services.Tickets.TokenRefused(source.Provider, source.Server);
            }
            return (source, null, ex);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return (source, null, null);
        }
    }

    /// <summary>Several sources' tickets as one list, in the order the list asks for.</summary>
    private IEnumerable<(TicketSource Source, Ticket Ticket)> Merge(IEnumerable<(TicketSource Source, Ticket Ticket)> tickets) => Sort switch
    {
        TicketSort.Updated => tickets.OrderByDescending(t => t.Ticket.Updated),
        TicketSort.Oldest => tickets.OrderBy(t => t.Ticket.Created),
        _ => tickets.OrderByDescending(t => t.Ticket.Created),
    };

    private void UseSources(ProjectTicketSources found)
    {
        _sources = found.Sources;
        // Each source's server is in Settings from now on, for its token: a YouTrack server named by the project's file.
        foreach (var source in _sources)
        {
            _services.Tickets.Remember(source.Provider, source.Server);
        }
        SourceProblems = found.Problems;
        _choosingSources = true;
        try
        {
            SourceChoices.Clear();
            foreach (var source in _sources)
            {
                SourceChoices.Add(new TicketSourceChoice(source, _sources.Count(s => s.DisplayName == source.DisplayName) > 1 ? $"{source.DisplayName} ({source.ServerName})" : source.DisplayName));
            }
            if (_sources.Count > 1)
            {
                SourceChoices.Add(new TicketSourceChoice(null, "All sources"));
            }
            SelectedSource = State.AllSources && _sources.Count > 1 ? SourceChoices[^1]
                : SourceChoices.FirstOrDefault(c => c.Source?.Id is { } id && string.Equals(id, State.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? SourceChoices.FirstOrDefault();
        }
        finally
        {
            _choosingSources = false;
        }
    }

    /// <summary>The list's error in words, with what to do: the first source's that failed.</summary>
    private void UseError((TicketSource Source, TicketPage? Page, TicketException? Error) failed)
    {
        var error = failed.Error;
        ErrorOffersSettings = error?.Kind is TicketErrorKind.Refused or TicketErrorKind.RateLimited or TicketErrorKind.NotFound;
        var name = failed.Source is { } source ? _services.Tickets.FindType(source.Provider)?.Name ?? source.Provider : "The tracker";
        ListError = error switch
        {
            null => null,
            { Kind: TicketErrorKind.RateLimited, Until: { } until } => SignInOf(failed.Source) == TicketSignIn.None
                ? $"{name}'s limit for this computer is used up until {Clock(until)}. Sign in for a higher one."
                : $"{name}'s limit for your account is used up until {Clock(until)}.",
            { Kind: TicketErrorKind.Refused or TicketErrorKind.NotFound } when SignInOf(failed.Source) == TicketSignIn.Token
                && _services.Tickets.HasToken(failed.Source.Provider, failed.Source.Server) != true =>
                $"{error.Message} It may be private: set a token for {failed.Source.ServerName} in Settings → Tickets → {name}.",
            { Kind: TicketErrorKind.Refused } when SignInOf(failed.Source) != TicketSignIn.None =>
                $"{error.Message} Check how Deventry signs in to {failed.Source.ServerName} in Settings → Tickets → {name}.",
            _ => error.Message,
        };
    }

    private TicketSignIn SignInOf(TicketSource source) => _services.Tickets.SignInOf(source.Provider, source.Server);

    /// <summary>A time today as the clock shows it: <i>15:20</i>.</summary>
    private string Clock(DateTimeOffset time) =>
        TimeZoneInfo.ConvertTime(time, _services.Time.LocalTimeZone).ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    private TicketRowViewModel Row(TicketSource source, Ticket ticket) =>
        new(source, ticket, TabsOn(source, ticket.Id).Count, _services.Time) { ShowsSource = ShownSources.Count > 1 };

    /// <summary>The open tabs started on a ticket (DESIGN.md §18, "Tickets", "Tabs remember their ticket").</summary>
    internal IReadOnlyList<TabViewModel> TabsOn(TicketSource source, string id) =>
        [.. _shell.AllTabs.Where(t => t.State.Ticket is { } on && on.Is(source.Id, id))];

    /// <summary>Tabs opened or closed: the rows' counts and the ticket's <b>Tab on it</b> follow.</summary>
    internal void OnTabsChanged()
    {
        foreach (var row in Rows)
        {
            row.TabCount = TabsOn(row.Source, row.Ticket.Id).Count;
        }
        Detail?.UpdateTabs(TabsOn(Detail.Source, Detail.Ticket.Id));
    }

    /// <summary><b>Open in the browser</b>: the source's own list of tickets.</summary>
    [RelayCommand]
    private async Task OpenListInBrowserAsync()
    {
        if (ShownSources.FirstOrDefault() is { } source)
        {
            var tracker = await _services.Tickets.TrackerAsync(source.Provider, source.Server);
            await _services.Platform.OpenUrlAsync(tracker.ListUrl(source));
        }
    }

    /// <summary><b>Ticket sources…</b> and <b>Add a ticket source…</b>: the project's settings, on Tickets.</summary>
    [RelayCommand]
    private void EditSources() => _shell.OpenTicketSourcesCommand.Execute(Group);

    /// <summary>The provider's page in Settings → Tickets, for its servers and sign-in.</summary>
    [RelayCommand]
    private void OpenProviderSettings() =>
        _shell.OpenTicketSettings(ShownSources.FirstOrDefault()?.Provider ?? SelectedRow?.Source.Provider);

    public string ProviderSettingsText => ShownSources.FirstOrDefault() is { } source && _services.Tickets.FindType(source.Provider) is { } type
        ? $"{type.Name} settings…"
        : "Tickets settings…";
}
