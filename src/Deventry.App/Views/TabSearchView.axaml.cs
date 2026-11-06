using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>Find a tab (DESIGN.md §4): type to filter, Up and Down to choose, Enter to go to the tab, Escape to close.</summary>
public partial class TabSearchView : UserControl
{
    public TabSearchView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => QueryBox.Focus());
    }

    private TabSearchViewModel? ViewModel => DataContext as TabSearchViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } search)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Enter:
                search.PickCommand.Execute(null);
                break;
            case Key.Down:
                search.MoveDownCommand.Execute(null);
                ScrollToSelected(search);
                break;
            case Key.Up:
                search.MoveUpCommand.Execute(null);
                ScrollToSelected(search);
                break;
            case Key.Escape:
                search.CloseCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void ScrollToSelected(TabSearchViewModel search)
    {
        if (search.Selected is { } selected)
        {
            Results.ScrollIntoView(selected);
        }
    }

    /// <summary>A click on a tab goes to it at once, as a click on its row in the sidebar does; one on the scroll bar doesn't.</summary>
    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is TabSearchEntry entry)
        {
            ViewModel?.PickCommand.Execute(entry);
        }
    }
}
