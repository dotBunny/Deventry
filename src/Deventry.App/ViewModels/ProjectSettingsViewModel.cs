using System.ComponentModel;
using Deventry.App.Services;
using Deventry.Core.ProjectTools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deventry.App.ViewModels;

/// <summary>
/// A folder's project settings, in a window of their own (DESIGN.md §14, "The project's settings"): its <b>Links</b>,
/// <b>Actions</b> and <b>Group</b>, from the folder's <c>deventry.json</c> and <c>deventry.local.json</c>, and its
/// <b>Tools</b>, this machine's choices for the project in <c>state.json</c>. Each change is saved as it's made: a file is
/// rewritten after each add, edit, removal or move, and every tab in the folder reads it again, so the project's menu
/// follows. Dispose it with the window.
/// </summary>
public sealed partial class ProjectSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly ProjectToolsViewModel _tools;

    /// <param name="folder">The folder whose project files the pages edit: a group's, or a tab's.</param>
    /// <param name="tools">The folder's project tools, a tab's or its group's, whose project the Tools page shows.</param>
    public ProjectSettingsViewModel(AppServices services, ShellViewModel shell, string folder, ProjectToolsViewModel tools)
    {
        _services = services;
        _shell = shell;
        _tools = tools;
        Folder = folder;
        LoadLinks();
        LoadTicketSources();
        LoadActionRows(ProjectFileScope.Shared);
        LoadActionRows(ProjectFileScope.Local);
        ProjectActionFiles =
        [
            new SettingChoice<ProjectFileScope>(ProjectFileScope.Shared, $"Shared with the project ({ProjectFile.SharedName})"),
            new SettingChoice<ProjectFileScope>(ProjectFileScope.Local, $"Just me ({ProjectFile.LocalName})"),
        ];
        // The shared file first when it has actions, since that's where a project's are.
        SelectedProjectActionFile = ProjectActionFiles[_sharedRows.Count > 0 || _localRows.Count == 0 && _actionFileErrors.GetValueOrDefault(ProjectFileScope.Shared) is null ? 0 : 1];
        LoadGroupFiles();
        SelectedGroupFile = ProjectActionFiles[GroupFileToShow()];
        LoadSolutionFiles();
        SelectedSolutionFile = ProjectActionFiles[SolutionFileToShow()];
        BuildToolChoices();
        _tools.PropertyChanged += OnProjectToolsPropertyChanged;
    }

    public void Dispose() => _tools.PropertyChanged -= OnProjectToolsPropertyChanged;

    /// <summary>The folder's group in the main window, whose color the window's heading has; null for a folder without one.</summary>
    public TabGroupViewModel? Group =>
        _shell.Groups.FirstOrDefault(g => SamePath(g.Folder, Folder)) ?? _shell.Groups.FirstOrDefault(g => g.Tabs.Any(t => SamePath(t.Folder, Folder)));

    /// <summary>The folder, where the project files are.</summary>
    public string Folder { get; }

    public string FolderName => Core.Formats.FolderName(Folder);

    /// <summary>The project's icon, which the window's heading shows in place of the group's dot, as the sidebar does.</summary>
    public string? GroupIcon => _tools.GroupIcon;

    public bool HasGroupIcon => GroupIcon is not null;

    /// <summary>The project's name when a provider found one, else the folder's, as the window's title and heading say.</summary>
    public string Heading => _tools.Project?.Name ?? FolderName;

    /// <summary>The window's title: "NightOwl · Project settings".</summary>
    public string WindowTitle => $"{Heading} · Project settings";

    /// <summary>"Shared (deventry.json)" or "Just me (deventry.local.json)": where a link or action is.</summary>
    public static string FileLabel(ProjectFileScope scope) =>
        scope == ProjectFileScope.Shared ? $"Shared ({ProjectFile.SharedName})" : $"Just me ({ProjectFile.LocalName})";

    /// <summary>The small dialog over the window: a link or an action being added or edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    public partial ViewModelBase? Editor { get; private set; }

    public bool HasEditor => Editor is not null;

    private void CloseEditor() => Editor = null;

    /// <summary>A file was saved: every tab in the folder, and its group, read its project files again.</summary>
    private void OnFileSaved() => _shell.OnProjectActionsChanged(Folder);

    /// <summary>A write that failed says why: the file's own reason when it isn't JSON, else what the OS said.</summary>
    private static string SaveFailure(ProjectFileScope scope, Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"Couldn't save {ProjectFile.FileName(scope)}: {ex.Message}";

    private static bool IsSaveFailure(Exception ex) => ex is InvalidOperationException or IOException or UnauthorizedAccessException;

    private void OnProjectToolsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectToolsViewModel.Project) or null or "")
        {
            OnPropertyChanged(nameof(Heading));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(SolutionNote));
            ToolsChanged();
        }
        if (e.PropertyName is nameof(ProjectToolsViewModel.GroupIcon) or null or "")
        {
            OnPropertyChanged(nameof(GroupIcon));
            OnPropertyChanged(nameof(HasGroupIcon));
        }
    }
}
