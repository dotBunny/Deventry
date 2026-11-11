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
    /// without opening a window for each file; Enter opens the selected one. A click on a folder's row folds it, and with
    /// Alt every folder under it too (DESIGN.md §8, "The tree").
    /// </summary>
    private void OnChangedFileTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel?.ChangedFiles is not { } files)
        {
            return;
        }
        switch (ChangedRowAt(e))
        {
            case ChangedFolderRow folder when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                files.ToggleFolderAllLevels(folder);
                break;
            case ChangedFolderRow folder:
                files.ToggleFolderCommand.Execute(folder);
                break;
            case ChangedFileRow row when !files.HasDiffTool:
                files.OpenFileDiffCommand.Execute(row);
                break;
        }
    }

    /// <summary>
    /// A double click opens the diff tool when one is set. Without one, the second click of it opens the built-in view
    /// again, as a single click would (a double click raises no second tap). On a folder, its first click folded it.
    /// </summary>
    private void OnChangedFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ChangedRowAt(e) is not ChangedFileRow row || ViewModel?.ChangedFiles is not { } files)
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

    /// <summary>
    /// Enter opens the selected file as a click (or, with a diff tool, a double click) would, and folds a folder. In the
    /// tree, Right expands a folder and Left collapses it, or goes up to the folder a row is in, as the Files tab's
    /// explorer does.
    /// </summary>
    private void OnChangedFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } tab || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }
        var files = tab.ChangedFiles;
        switch (e.Key, ChangedFilesList.SelectedItem)
        {
            case (Key.Enter, ChangedFileRow row) when files.HasDiffTool:
                files.OpenFileInDiffToolCommand.Execute(row);
                break;
            case (Key.Enter, ChangedFileRow row):
                files.OpenFileDiffCommand.Execute(row);
                break;
            case (_, ChangedFolderRow folder) when e.Key == Key.Enter || e.Key == Key.Right && folder.IsCollapsed || e.Key == Key.Left && folder.IsExpanded:
                files.ToggleFolderCommand.Execute(folder);
                break;
            case (Key.Left, ChangedTreeRow { Depth: > 0 } row):
                var index = files.Rows.IndexOf(row);
                if (files.Rows.Take(index).LastOrDefault(r => r is ChangedFolderRow && r.Depth < row.Depth) is { } up)
                {
                    ChangedFilesList.SelectedItem = up;
                    ChangedFilesList.ContainerFromItem(up)?.Focus(NavigationMethod.Directional);
                }
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>The row under a tap, unless it was on one of the row's own controls (the Reviewed box, a button).</summary>
    private static ChangedTreeRow? ChangedRowAt(TappedEventArgs e) =>
        e.Source is Control source && source.FindAncestorOfType<ToggleButton>(includeSelf: true) is null
            && source.FindAncestorOfType<Button>(includeSelf: true) is null
            ? source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as ChangedTreeRow
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