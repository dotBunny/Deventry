using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// An open tab as <b>Find a tab</b> lists and searches it (DESIGN.md §4, "Find a tab"): its name, its group's name and
/// color, its folder and its git branch, as the sidebar shows them now.
/// </summary>
/// <param name="Branch">The branch on its row, or null outside a git repository or before it's read.</param>
public sealed record TabSearchEntry(TabViewModel Tab, string Name, string Project, IBrush ProjectBrush, string Folder, string? Branch)
{
    /// <summary>What it's doing, as its row's status icon says; empty while it's idle.</summary>
    public string StatusGlyph => Tab.StatusGlyph;

    public string StatusTip => Tab.StatusTip;

    public bool HasBranch => !string.IsNullOrEmpty(Branch);

    /// <summary>What a screen reader reads for its row: the tab, then its project.</summary>
    public override string ToString() => $"{Name}, {Project}";
}

/// <summary>
/// <b>Find a tab</b> (DESIGN.md §4, "Find a tab"): the open tabs, filtered as the user types. Each word typed has to
/// be somewhere in a tab's name, its group's name, its folder or its branch, ignoring case; tabs whose name has every
/// word come first, then those whose name or project has them, then the rest, each in sidebar order. Up, Down and Enter
/// choose; Escape closes.
/// </summary>
public sealed partial class TabSearchViewModel : ViewModelBase
{
    private readonly IReadOnlyList<TabSearchEntry> _entries;
    private readonly Action<TabViewModel> _pick;
    private readonly Action _close;

    /// <param name="entries">The open tabs, in sidebar order.</param>
    /// <param name="pick">Goes to the tab picked, once the popup has closed.</param>
    /// <param name="close">Closes the popup, after picking or on Escape.</param>
    public TabSearchViewModel(IReadOnlyList<TabSearchEntry> entries, Action<TabViewModel> pick, Action close)
    {
        _entries = entries;
        _pick = pick;
        _close = close;
        Filter();
    }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    partial void OnQueryChanged(string value) => Filter();

    public ObservableCollection<TabSearchEntry> Results { get; } = [];

    [ObservableProperty]
    public partial TabSearchEntry? Selected { get; set; }

    public bool HasResults => Results.Count > 0;

    /// <summary>Nothing to search: no tab is open.</summary>
    public bool HasNoTabs => _entries.Count == 0;

    /// <summary>Tabs are open, but none has what's typed.</summary>
    public bool HasNoMatches => !HasResults && !HasNoTabs;

    [RelayCommand]
    private void MoveDown() => Move(1);

    [RelayCommand]
    private void MoveUp() => Move(-1);

    private void Move(int by)
    {
        if (Results.Count == 0)
        {
            return;
        }
        var at = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[(at + by + Results.Count) % Results.Count];
    }

    /// <summary>Goes to the chosen tab (or the one clicked), after closing the popup so the sidebar is in view.</summary>
    [RelayCommand]
    private void Pick(TabSearchEntry? entry)
    {
        if ((entry ?? Selected) is not { } chosen)
        {
            return;
        }
        _close();
        _pick(chosen.Tab);
    }

    [RelayCommand]
    private void Close() => _close();

    private void Filter()
    {
        var words = Words(Query);
        var matches = words.Length == 0
            ? _entries
            : _entries
                .Select((entry, order) => (Entry: entry, Order: order, Rank: Rank(entry, words)))
                .Where(m => m.Rank is not null)
                .OrderBy(m => m.Rank)
                .ThenBy(m => m.Order)
                .Select(m => m.Entry)
                .ToArray();
        Results.Clear();
        foreach (var entry in matches)
        {
            Results.Add(entry);
        }
        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>The words typed, each to be found on its own.</summary>
    internal static string[] Words(string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Where <paramref name="words"/> are in a tab, lower first: 0 when its name has them all, 1 when its name and its
    /// project's have them between them, 2 when the folder or branch is needed for one. Null when a word is nowhere.
    /// </summary>
    internal static int? Rank(TabSearchEntry entry, IReadOnlyList<string> words)
    {
        var rank = 0;
        foreach (var word in words)
        {
            int? best = Has(entry.Name, word) ? 0
                : Has(entry.Project, word) ? 1
                : Has(entry.Folder, word) || Has(entry.Branch, word) ? 2
                : null;
            if (best is not { } found)
            {
                return null;
            }
            rank = Math.Max(rank, found);
        }
        return rank;
    }

    private static bool Has(string? text, string word) => text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;
}
