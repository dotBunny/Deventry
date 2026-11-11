using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>
/// A session's tool window's page (DESIGN.md §3, "Tool windows"): the one <see cref="Page"/> names, of the session tab it
/// has for its data context, and what its pages need beyond bindings, which is opening a changed file and folding
/// processes.
/// </summary>
public partial class SessionPagesView : UserControl
{
    /// <summary>The page shown: a tool window kind's id (<c>Files</c>, <c>Agents</c> and so on).</summary>
    public static readonly StyledProperty<string?> PageProperty = AvaloniaProperty.Register<SessionPagesView, string?>(nameof(Page));

    static SessionPagesView()
    {
        PageProperty.Changed.AddClassHandler<SessionPagesView>((view, _) => view.ShowPage());
    }

    public SessionPagesView()
    {
        InitializeComponent();
        // Tunnel: the list's items take Enter for themselves.
        ChangedFilesList.AddHandler(KeyDownEvent, OnChangedFileKeyDown, RoutingStrategies.Tunnel);
        // Tunnel, so an Alt+click on a process's arrow is taken before the arrow clicks.
        ProcessesScroller.AddHandler(PointerPressedEvent, OnProcessPressed, RoutingStrategies.Tunnel);
    }

    public string? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private TabViewModel? ViewModel => DataContext as TabViewModel;

    /// <summary>Only the page named shows; the others stay made, as the side panel's did, but out of sight.</summary>
    private void ShowPage()
    {
        FilesPage.IsVisible = Page == "Files";
        AgentsPage.IsVisible = Page == "Agents";
        ProjectPage.IsVisible = Page == "Project";
        TasksPage.IsVisible = Page == "Tasks";
        McpPage.IsVisible = Page == "Mcp";
        MediaPage.IsVisible = Page == "Media";
        ScratchPadPage.IsVisible = Page == "ScratchPad";
        ProcessesPage.IsVisible = Page == "Processes";
    }

    // ---- Changed files (DESIGN.md §8) -------------------------------------------------------------------------------

    /// <summary>
    /// A click opens the built-in diff view (DESIGN.md §8). Not on selection, so the arrow keys move through the list
    /// without opening a window for each file; Enter opens the selected one.
    /// </summary>
    private void OnChangedFileTapped(object? sender, TappedEventArgs e)
    {
        if (ChangedFileAt(e) is { } row && ViewModel?.ChangedFiles is { HasDiffTool: false } files)
        {
            files.OpenFileDiffCommand.Execute(row);
        }
    }

    /// <summary>
    /// A double click opens the diff tool when one is set. Without one, the second click of it opens the built-in view
    /// again, as a single click would (a double click raises no second tap).
    /// </summary>
    private void OnChangedFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ChangedFileAt(e) is not { } row || ViewModel?.ChangedFiles is not { } files)
        {
            return;
        }
        if (files.HasDiffTool)
        {
            files.OpenFileInDiffToolCommand.Execute(row);
        }
        else
        {
            files.OpenFileDiffCommand.Execute(row);
        }
    }

    /// <summary>Enter opens the selected file as a click (or, with a diff tool, a double click) would.</summary>
    private void OnChangedFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ChangedFilesList.SelectedItem is ChangedFileRow row && ViewModel is { } tab)
        {
            if (tab.ChangedFiles.HasDiffTool)
            {
                tab.ChangedFiles.OpenFileInDiffToolCommand.Execute(row);
            }
            else
            {
                tab.ChangedFiles.OpenFileDiffCommand.Execute(row);
            }
            e.Handled = true;
        }
    }

    /// <summary>The row under a tap, unless it was on one of the row's own controls (the Reviewed box, a button).</summary>
    private static ChangedFileRow? ChangedFileAt(TappedEventArgs e) =>
        e.Source is Control source && source.FindAncestorOfType<ToggleButton>(includeSelf: true) is null
            && source.FindAncestorOfType<Button>(includeSelf: true) is null
            ? source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as ChangedFileRow
            : null;

    // ---- Processes (DESIGN.md §4, "Process monitor") ---------------------------------------------------------------

    /// <summary>Alt+click on a process's arrow expands or collapses every level under it too.</summary>
    private void OnProcessPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && e.GetCurrentPoint(ProcessesScroller).Properties.IsLeftButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: ProcessRow row } arrow
            && arrow.Classes.Contains("processfold")
            && ViewModel is { } tab)
        {
            tab.ProcessMonitor.ToggleProcessAllLevels(row);
            e.Handled = true;
        }
    }
}