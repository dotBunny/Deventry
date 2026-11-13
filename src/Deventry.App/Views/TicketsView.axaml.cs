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

    public TicketsView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, (_, _) => _tickets?.FocusPane(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _tickets?.FocusPane(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _tickets = DataContext as TicketsViewModel;
        UpdateNarrow();
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
        // In a narrow pane, the list has the whole width.
        ListColumn.Width = tickets.IsNarrow ? double.NaN : 320;
        Grid.SetColumnSpan(ListColumn, tickets.IsNarrow ? 2 : 1);
        Grid.SetColumn(TicketColumn, tickets.IsNarrow ? 0 : 1);
        Grid.SetColumnSpan(TicketColumn, tickets.IsNarrow ? 2 : 1);
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
