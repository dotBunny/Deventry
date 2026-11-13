using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Deventry.App.Services;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>
/// A group's Tickets tab (DESIGN.md §18, "Tickets"): a click or the focus in it focuses its pane; a pane too narrow for
/// the list and the ticket side by side shows one at a time; <c>F5</c> refreshes and <c>Ctrl/Cmd+F</c> goes to the filter.
/// </summary>
public partial class TicketsView : UserControl
{
    /// <summary>Under this width, the ticket takes the list's place.</summary>
    public const double NarrowWidth = 640;

    private TicketsViewModel? _tickets;
    private double? _resizeFrom;
    private double _resizeStartWidth;

    public TicketsView()
    {
        InitializeComponent();
        ListEdge.PointerPressed += OnEdgePressed;
        ListEdge.PointerMoved += OnEdgeMoved;
        ListEdge.PointerReleased += (_, e) => EndResize(e.Pointer);
        ListEdge.PointerCaptureLost += (_, _) => EndResize(null);
        ListEdge.DoubleTapped += (_, _) => _tickets?.ResetListWidth();
        AddHandler(PointerPressedEvent, (_, _) => _tickets?.FocusPane(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _tickets?.FocusPane(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_tickets is not null)
        {
            _tickets.PropertyChanged -= OnTicketsChanged;
        }
        base.OnDataContextChanged(e);
        _tickets = DataContext as TicketsViewModel;
        if (_tickets is not null)
        {
            _tickets.PropertyChanged += OnTicketsChanged;
        }
        UpdateNarrow();
    }

    private void OnTicketsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TicketsViewModel.ListWidth))
        {
            UpdateNarrow();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateNarrow();
        }
    }

    private void UpdateNarrow()
    {
        if (_tickets is not { } tickets || Bounds.Width <= 0)
        {
            return;
        }
        tickets.IsNarrow = Bounds.Width < NarrowWidth;
        // In a narrow pane, the list has the whole width; otherwise its own, as far as the ticket keeps its least.
        ListColumn.Width = tickets.IsNarrow ? double.NaN
            : Math.Min(tickets.ListWidth, Math.Max(ShellLayout.MinTicketListWidth, Bounds.Width - ShellLayout.MinTicketWidth));
        Grid.SetColumnSpan(ListColumn, tickets.IsNarrow ? 2 : 1);
        Grid.SetColumn(TicketColumn, tickets.IsNarrow ? 0 : 1);
        Grid.SetColumnSpan(TicketColumn, tickets.IsNarrow ? 2 : 1);
    }

    // ---- Resizing the list ------------------------------------------------------------------------------------------

    private void OnEdgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_tickets is null || !e.GetCurrentPoint(ListEdge).Properties.IsLeftButtonPressed || e.ClickCount > 1)
        {
            return;
        }
        _resizeFrom = e.GetPosition(this).X;
        _resizeStartWidth = ListColumn.Bounds.Width;
        e.Pointer.Capture(ListEdge);
        e.Handled = true;
    }

    /// <summary>The list wider or narrower, leaving the ticket its least (DESIGN.md §18, "Tickets", "Its layout").</summary>
    private void OnEdgeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeFrom is not { } from || _tickets is null)
        {
            return;
        }
        var width = _resizeStartWidth + e.GetPosition(this).X - from;
        _tickets.ResizeList(Math.Min(width, Math.Max(ShellLayout.MinTicketListWidth, Bounds.Width - ShellLayout.MinTicketWidth)));
    }

    private void EndResize(IPointer? pointer)
    {
        if (_resizeFrom is null)
        {
            return;
        }
        _resizeFrom = null;
        pointer?.Capture(null);
        _tickets?.SaveListWidth();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tickets is not { } tickets)
        {
            return;
        }
        var primary = Shortcuts.IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            tickets.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == primary)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>A state picked in <b>Change state</b>'s list: the list closes, as a menu does.</summary>
    private void OnStatePicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.FindLogicalAncestorOfType<Avalonia.Controls.Primitives.Popup>() is { } popup)
        {
            popup.IsOpen = false;
        }
    }
}
