using Deventry.App.Conversation;
using Deventry.Core;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.App.ViewModels;

/// <summary>
/// What a group's project tools need while it has no session tab open (DESIGN.md §18, "Project tools"): one of Claude
/// Code's projects listing only its past sessions, or a group with only its Project Files tab. Its project and its
/// <c>deventry.json</c> are found as a tab's are, and its menu is a tab's, but a job has no tab to run under, so jobs wait
/// for one (<see cref="CanRunJobs"/>); what the tools would say in a conversation is a notice over the window instead.
/// </summary>
internal sealed class FolderProjectToolsHost(ShellViewModel shell, TabGroupViewModel group) : IProjectToolsHost
{
    private readonly TabState _state = new() { Folder = group.Folder };

    public string Id => $"folder:{group.Folder}";

    public string DisplayName => group.Label;

    public string Folder => group.Folder;

    public TabState State => _state;

    public ClaudeSession? Session => null;

    public string FolderName => Formats.FolderName(group.Folder);

    public string GroupFolder => group.Folder;

    public bool CanRunJobs => false;

    public long? CurrentChangelist => null;

    public void AddNote(string text, NoteKind kind = NoteKind.Info, string? link = null) => shell.Notify(group.Label, text);

    public void Confirm(string title, string message, string confirmText, Func<Task> onConfirm) => shell.Confirm(title, message, confirmText, onConfirm);

    public void InfoRowsChanged()
    {
    }

    public void UseGroupColor(string color) => shell.UseProjectColor(group, color);

    // No job runs without a tab, so there are no runs to show.

    public void RunAdded(ProjectRunViewModel run)
    {
    }

    public void RunRemoved(ProjectRunViewModel run)
    {
    }

    public void ShowRun(ProjectRunViewModel run)
    {
    }

    public void RunSelected(ProjectRunViewModel run)
    {
    }

    public void CloseRunPane(ProjectRunViewModel run)
    {
    }

    public Task<OpenedPath> OpenPathAsync(string path, int? line, int? column) => shell.OpenPathAsync(path, group.Folder, null, line, column);

    // An editor's log goes under one of its project's tabs (DESIGN.md §18, "Editor logs"): with none open, nothing follows it.

    public void ProjectChanged()
    {
    }

    public void ProjectLaunched()
    {
    }
}
