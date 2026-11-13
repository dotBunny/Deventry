using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The shell's widths (DESIGN.md §3, §4, "Sidebar"): the sidebar's, collapsed to its rail by the user or by a narrow
/// window, and the room the tool windows' right edge takes (<see cref="ToolWindows.ToolDockViewModel"/>). What the user
/// chooses is kept in the app state. The sidebar has no most; beside it the conversation keeps
/// <see cref="MinConversationWidth"/> and the right edge its least, and when the window is too narrow for all three, a
/// docked right edge gives way first. And the share of the width each of the tabs side by side has.
/// </summary>
public sealed partial class ShellLayout : ViewModelBase
{
    private readonly AppState _state;
    private readonly Action _saveState;
    private bool _isNarrow;

    /// <summary>The shell's width, which the sidebar leaves room in; unbounded until the view reports it.</summary>
    private double _availableWidth = double.PositiveInfinity;

    /// <param name="saveState">Saves <paramref name="state"/> once a width or the sidebar's collapse changes.</param>
    internal ShellLayout(AppState state, Action saveState)
    {
        _state = state;
        _saveState = saveState;
        IsSidebarCollapsed = state.SidebarCollapsed;
        SidebarWidth = Math.Max(state.SidebarWidth ?? DefaultSidebarWidth, MinSidebarWidth);
        PaneShares = state.PaneShares is { Count: > 1 } shares && shares.All(s => s > 0) ? [.. shares] : [];
        ExplorerWidth = Math.Max(state.ExplorerWidth ?? DefaultExplorerWidth, MinExplorerWidth);
        IsExplorerHidden = state.ExplorerHidden;
        TicketListWidth = Math.Max(state.TicketListWidth ?? DefaultTicketListWidth, MinTicketListWidth);
    }

    // ---- The Tickets tabs' list (DESIGN.md §18, "Tickets", "Its layout"): one width for every tab's --------------

    public const double DefaultTicketListWidth = 320;
    public const double MinTicketListWidth = 220;

    /// <summary>The least the ticket beside the list keeps: under it and the list's least, the ticket takes the list's place.</summary>
    public const double MinTicketWidth = 320;

    /// <summary>The list's width, as the user dragged it. Each tab's view shows it as far as there's room, leaving the ticket <see cref="MinTicketWidth"/>.</summary>
    [ObservableProperty]
    public partial double TicketListWidth { get; private set; }

    /// <summary>Dragging the list's edge. <see cref="SaveTicketListWidth"/> keeps the result when the drag ends.</summary>
    public void ResizeTicketList(double width) => TicketListWidth = Math.Max(width, MinTicketListWidth);

    public void SaveTicketListWidth()
    {
        _state.TicketListWidth = TicketListWidth;
        _saveState();
    }

    // ---- The Project Files tabs' explorer (DESIGN.md §18, "Project files"): one width for every tab's ---------------

    public const double DefaultExplorerWidth = 260;
    public const double MinExplorerWidth = 180;

    /// <summary>The explorer's width, as the user dragged it. Each tab's view shows it as far as there's room, leaving its editor <see cref="MinConversationWidth"/>.</summary>
    [ObservableProperty]
    public partial double ExplorerWidth { get; private set; }

    /// <summary>The explorer is hidden, by its button, in every Project Files tab.</summary>
    [ObservableProperty]
    public partial bool IsExplorerHidden { get; set; }

    partial void OnIsExplorerHiddenChanged(bool value)
    {
        _state.ExplorerHidden = value;
        _saveState();
    }

    /// <summary>Dragging the explorer's edge. <see cref="SaveExplorerWidth"/> keeps the result when the drag ends.</summary>
    public void ResizeExplorer(double width) => ExplorerWidth = Math.Max(width, MinExplorerWidth);

    public void SaveExplorerWidth()
    {
        _state.ExplorerWidth = ExplorerWidth;
        _saveState();
    }

    /// <summary>The conversation keeps at least this much room between the sidebar and a docked right edge of tool windows (DESIGN.md §3).</summary>
    public const double MinConversationWidth = 360;

    // ---- Sidebar (DESIGN.md §4, "Sidebar") ------------------------------------------------------------------

    public const double DefaultSidebarWidth = 248;
    public const double MinSidebarWidth = 180;

    /// <summary>The collapsed sidebar: a rail of status icons.</summary>
    public const double RailWidth = 52;

    /// <summary>Below this width the sidebar collapses to its rail by itself, leaving the user's own choice alone.</summary>
    public const double NarrowWidth = 900;

    /// <summary>Whether the sidebar shows only its rail.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarExpanded), nameof(SidebarDisplayWidth), nameof(MaxPanes))]
    public partial bool IsSidebarCollapsed { get; set; }

    public bool IsSidebarExpanded => !IsSidebarCollapsed;

    /// <summary>The expanded sidebar's width, as the user dragged it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarDisplayWidth), nameof(MaxPanes))]
    public partial double SidebarWidth { get; set; }

    /// <summary>
    /// The least the tool windows' right edge takes beside the panes (DESIGN.md §3, "Tool windows"): docked, its least
    /// width; auto-hidden, its tabs; nothing without windows. The shell keeps it from the tool windows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarDisplayWidth), nameof(MaxPanes))]
    public partial double RightEdgeRoom { get; set; }

    /// <summary>
    /// How many tabs fit side by side (DESIGN.md §18, "Split panes"): each pane keeps <see cref="MinConversationWidth"/>
    /// beside the sidebar as it shows and the tool windows' right edge's least. Panes past that fold away first, and
    /// come back as the window widens.
    /// </summary>
    public int MaxPanes =>
        double.IsInfinity(_availableWidth) ? ShellViewModel.MostPanes
        : Math.Clamp((int)((_availableWidth - SidebarDisplayWidth - RightEdgeRoom) / MinConversationWidth), 1, ShellViewModel.MostPanes);

    /// <summary>The sidebar as shown: the rail, or the width the user dragged as far as there's room for it.</summary>
    public double SidebarDisplayWidth => IsSidebarCollapsed ? RailWidth : Math.Min(SidebarWidth, SidebarRoom);

    /// <summary>
    /// The most the expanded sidebar shows: the shell's width less the conversation's least and the right edge's. A docked
    /// right edge gives way first, down to its least (the shell's view keeps it within what the sidebar leaves), and then
    /// the sidebar does, down to its own.
    /// </summary>
    private double SidebarRoom =>
        Math.Max(MinSidebarWidth, _availableWidth - MinConversationWidth - RightEdgeRoom);

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        // In a narrow window, expanding is only for now: once it's wide again, the user's choice comes back.
        if (!_isNarrow)
        {
            _state.SidebarCollapsed = IsSidebarCollapsed;
            _saveState();
        }
    }

    /// <summary>Called by the view as the window resizes: a narrow window shows the rail, and the sidebar fits.</summary>
    public void SetAvailableWidth(double width)
    {
        _availableWidth = width;
        OnPropertyChanged(nameof(SidebarDisplayWidth));
        OnPropertyChanged(nameof(MaxPanes));
        var narrow = width < NarrowWidth;
        if (narrow == _isNarrow)
        {
            return;
        }
        _isNarrow = narrow;
        IsSidebarCollapsed = narrow || _state.SidebarCollapsed;
    }

    /// <summary>
    /// Dragging the sidebar's edge, as far as there's room for it. <see cref="SaveSidebarWidth"/> keeps the result when
    /// the drag ends.
    /// </summary>
    public void ResizeSidebar(double width) => SidebarWidth = Math.Clamp(width, MinSidebarWidth, SidebarRoom);

    public void SaveSidebarWidth()
    {
        _state.SidebarWidth = SidebarWidth;
        _saveState();
    }

    // ---- Tabs side by side (DESIGN.md §18, "Split panes"): the share of the width each has -------------------------

    /// <summary>
    /// The share of the width each pane has, left to right, summing to 1, as the user dragged the lines between them;
    /// empty while they share it evenly. They hold for as many panes as they name.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<double> PaneShares { get; private set; } = [];

    /// <summary>The shares for <paramref name="count"/> panes: the dragged ones when they're for that many, else even.</summary>
    public IReadOnlyList<double> SharesFor(int count) =>
        PaneShares.Count == count && count > 0 ? PaneShares : [.. Enumerable.Repeat(count > 0 ? 1.0 / count : 0, count)];

    /// <summary>
    /// Dragging the line after pane <paramref name="line"/> to <paramref name="x"/>, across panes
    /// <paramref name="width"/> wide: the two panes beside it trade width, each keeping its least of
    /// <paramref name="leasts"/>, and the others stay as they are. <see cref="SavePaneShares"/> keeps the result.
    /// </summary>
    public void ResizePanes(int line, double x, double width, IReadOnlyList<double> leasts)
    {
        var count = leasts.Count;
        if (line < 0 || line >= count - 1 || width <= 0)
        {
            return;
        }
        var shares = SharesFor(count).ToArray();
        var left = shares.Take(line).Sum() * width;
        var right = left + (shares[line] + shares[line + 1]) * width;
        if (right - left < leasts[line] + leasts[line + 1])
        {
            return;
        }
        var at = Math.Clamp(x, left + leasts[line], right - leasts[line + 1]);
        shares[line] = (at - left) / width;
        shares[line + 1] = (right - at) / width;
        PaneShares = shares;
    }

    /// <summary>The panes share the width evenly again: a double-click on a line between them, or a pane coming or going.</summary>
    public void ResetPaneShares()
    {
        if (PaneShares.Count == 0)
        {
            return;
        }
        PaneShares = [];
        SavePaneShares();
    }

    public void SavePaneShares()
    {
        _state.PaneShares = PaneShares.Count > 0 ? [.. PaneShares] : null;
        _saveState();
    }
}
