using CommunityToolkit.Mvvm.Input;
using Deventry.Core.Settings;
using Deventry.App.ViewModels.Settings;

namespace Deventry.App.ViewModels;

/// <summary>
/// What the Settings window follows of the main window (DESIGN.md §14): its settings say which open tabs have their own
/// (Tab settings…), and it opens where it was left.
/// </summary>
public sealed partial class ShellViewModel : ISettingsHost
{
    public IReadOnlyList<string> TabsWithOwn(string overrideKey) =>
        [.. AllTabs.Where(t => TabOverrideKeys.Has(t.State.Overrides, overrideKey)).Select(t => t.DisplayName)];

    /// <summary>The page Settings showed when it last closed, this run; it opens there next.</summary>
    public string? LastSettingsPage { get; set; }

    // ---- A project's settings, in their own window (DESIGN.md §14, "The project's settings") ---------------------------

    /// <summary>Set by the view: opens a folder's project settings where the opening says, or brings its window forward.</summary>
    public Func<ProjectSettingsOpening, Task>? ShowProjectSettingsWindow { get; set; }

    /// <summary>
    /// The folder's project settings, in their window, at <paramref name="page"/>. There's one window for each folder:
    /// asked for again, it comes to the front at that page.
    /// </summary>
    internal Task OpenProjectSettingsAsync(string folder, string page) =>
        ShowProjectSettingsWindow is { } show && ProjectToolsFor(folder) is not null
            ? show(new ProjectSettingsOpening(folder, page))
            : Task.CompletedTask;

    /// <summary>The pages of <paramref name="folder"/>'s project settings, for its window; null for a folder with no tab or group.</summary>
    public ProjectSettingsViewModel? ProjectSettingsFor(string folder) =>
        ProjectToolsFor(folder) is { } tools ? new ProjectSettingsViewModel(_services, this, folder, tools) : null;

    /// <summary>
    /// The project tools of <paramref name="folder"/>, whose project the Tools page shows: its group's tab used last when
    /// that's in the folder, else any tab in it, else the group's own, as a group with no tab open has them.
    /// </summary>
    private ProjectToolsViewModel? ProjectToolsFor(string folder)
    {
        var group = Groups.FirstOrDefault(g => FolderHistory.SamePath(g.Folder, folder));
        if (group?.ProjectTab is { } used && FolderHistory.SamePath(used.Folder, folder))
        {
            return used.ProjectTools;
        }
        return AllTabs.FirstOrDefault(t => FolderHistory.SamePath(t.Folder, folder))?.ProjectTools ?? group?.OwnFolderTools;
    }

    /// <summary><b>Project settings…</b>, in a group's menu: its folder's, at Links.</summary>
    [RelayCommand]
    private Task OpenGroupProjectSettingsAsync(TabGroupViewModel? group) =>
        group is null ? Task.CompletedTask : OpenProjectSettingsAsync(group.Folder, ProjectSettingsWindowViewModel.LinksPage);

    /// <summary><b>Project settings…</b>, in a tab's menu: its folder's, at Links.</summary>
    [RelayCommand]
    private Task OpenTabProjectSettingsAsync(TabViewModel? tab) =>
        tab is null ? Task.CompletedTask : OpenProjectSettingsAsync(tab.Folder, ProjectSettingsWindowViewModel.LinksPage);
}

/// <summary>Where a project's settings window opens: the folder, the page, and whether to start a new link or action there.</summary>
public sealed record ProjectSettingsOpening(string Folder, string Page);
