using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// <b>Find a tab</b> (DESIGN.md §4, "Find a tab"): a popup over the window that finds an open tab by pieces of its
/// name, its project's, its folder or its branch, and goes to it, its row in the sidebar picked out for a moment.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>How long a row found is picked out for: a few soft pulses, or a still highlight with motion reduced.</summary>
    internal static readonly TimeSpan AttentionFor = TimeSpan.FromSeconds(2);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTabSearchOpen))]
    public partial TabSearchViewModel? TabSearch { get; set; }

    public bool IsTabSearchOpen => TabSearch is not null;

    /// <summary>
    /// Raised once a tab found is selected and its group and thread opened, for the view to scroll the sidebar to its
    /// row.
    /// </summary>
    public event Action<TabViewModel>? TabRevealed;

    /// <summary>The row picked out now, which another tab found takes the highlight from.</summary>
    private TabViewModel? _attentionTab;

    private UiTimeout? _attentionWait;

    private UiTimeout AttentionWait => _attentionWait ??= new(_services.Time, _services.Dispatcher);

    /// <summary>Ctrl/Cmd+K, the sidebar's magnifier and the palette's <b>Find a tab…</b>: the open tabs, in sidebar order.</summary>
    [RelayCommand]
    private void OpenTabSearch()
    {
        Picker = null;
        History = null;
        Palette = null;
        TabSearch = new TabSearchViewModel(TabSearchEntries(), GoToFoundTab, () => TabSearch = null);
    }

    public void CloseTabSearch() => TabSearch = null;

    internal List<TabSearchEntry> TabSearchEntries() =>
    [
        .. Groups.SelectMany(group => group.Tabs.Select(tab =>
            new TabSearchEntry(tab, tab.DisplayName, group.Label, group.Brush, tab.Folder, tab.BranchBadge))),
    ];

    /// <summary>
    /// Goes to a tab found: selects it as a click on its row does, opening its group and its thread when they're
    /// collapsed, and picks its row out for <see cref="AttentionFor"/>. Nothing happens when it has closed meanwhile.
    /// </summary>
    internal void GoToFoundTab(TabViewModel tab)
    {
        if (!SelectTab(tab.Id))
        {
            return;
        }
        tab.ShowOwnConversation();
        if (_attentionTab is { } previous && !ReferenceEquals(previous, tab))
        {
            previous.IsDrawingAttention = false;
        }
        // Off and on again, so a row found twice in a row pulses again.
        tab.IsDrawingAttention = false;
        tab.IsDrawingAttention = true;
        _attentionTab = tab;
        AttentionWait.Restart(AttentionFor, () =>
        {
            tab.IsDrawingAttention = false;
            if (ReferenceEquals(_attentionTab, tab))
            {
                _attentionTab = null;
            }
        });
        TabRevealed?.Invoke(tab);
    }
}
