using Deventry.App.Conversation;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Platform.Processes;

namespace Deventry.App.ViewModels;

/// <summary>
/// What the tab gives its child view models: the process monitor, changed files, project tools, Remote Control,
/// Perforce and the context indicator. Each sees only its own host interface, over the <see cref="ITabAreaHost"/> they share; where one area needs
/// another, it goes through the tab. Its replies, plans and agents' Markdown get their images and Copy from it too.
/// </summary>
public sealed partial class TabViewModel : IProcessMonitorHost, IChangedFilesHost, IProjectToolsHost, IRemoteControlHost, IPerforceHost, IContextHost, IMarkdownHost
{
    // ---- Every area ------------------------------------------------------------------------------------------------

    ClaudeSession? ITabAreaHost.Session => _session;

    void ITabAreaHost.AddNote(string text, NoteKind kind, string? link) => _conversation.AddNote(text, kind, link);

    void ITabAreaHost.Confirm(string title, string message, string confirmText, Func<Task> onConfirm) => _shell.Confirm(title, message, confirmText, onConfirm);

    void ITabAreaHost.InfoRowsChanged() => OnPropertyChanged(nameof(InfoRows));

    // ---- The process monitor -----------------------------------------------------------------------------------------

    IEnumerable<ToolUseItem> IProcessMonitorHost.ToolItems => Items.ToolCalls();

    string? IProcessMonitorHost.TaskIdFor(string toolUseId) => Tasks.TaskIdFor(toolUseId);

    IReadOnlyList<ProcessSnapshot> IProcessMonitorHost.ProjectJobProcesses(bool includeCommandLines) => ProjectTools.Runs.JobProcesses(includeCommandLines);

    ProcessTree? IProcessMonitorHost.ProjectJobTreeHolding(int pid) => ProjectTools.Runs.JobTreeHolding(pid);

    void IProcessMonitorHost.ScrollTo(ConversationItem item) => ScrollTo(item);

    void IProcessMonitorHost.ProcessesSampled() => _shell.OnTabProcessesSampled();

    // ---- Changed files ---------------------------------------------------------------------------------------------

    void IChangedFilesHost.CopyToLibrary() => CopyToLibrary();

    Task<OpenedPath> IChangedFilesHost.OpenPathAsync(string path) => OpenPathAsync(path);

    bool IChangedFilesHost.IsSelected => IsShown;

    string? IChangedFilesHost.ChangedSinceBy(string path, DateTimeOffset since) => _shell.ChangedSinceBy(this, path, since);

    void IChangedFilesHost.ClaudeChangedFile(string path) => _shell.OnClaudeChangedFile(this, path);

    void IChangedFilesHost.ClaudeCreatedFile(string path) => Perforce.OnClaudeCreatedFile(path);

    void IChangedFilesHost.AllReviewedChanged()
    {
        MarkShownChanged();
        // Joulene on the composer stamps it (DESIGN.md §5). Not while the tab is still being made: a restored tab's saved
        // state sets it before ChangedFiles is there.
        if (IsSelected && ChangedFiles is { AllReviewed: true })
        {
            TellMascot(mascot => mascot.AllReviewed());
        }
    }

    // ---- Project tools ---------------------------------------------------------------------------------------------

    long? IProjectToolsHost.CurrentChangelist => Perforce.CurrentChangelist;

    bool IProjectToolsHost.CanRunJobs => true;

    void IProjectToolsHost.UseGroupColor(string color) => _shell.UseProjectColor(this, color);

    void IProjectToolsHost.RunAdded(ProjectRunViewModel run) => _shell.AddRun(run);

    void IProjectToolsHost.RunRemoved(ProjectRunViewModel run) => _shell.RemoveRun(run, this);

    void IProjectToolsHost.ShowRun(ProjectRunViewModel run) => _shell.ShowRun(run);

    void IProjectToolsHost.RunSelected(ProjectRunViewModel run)
    {
        // Its entry comes out from under a collapsed tab, and a collapsed thread (DESIGN.md §4, "Sidebar").
        RevealRowsUnder();
    }

    void IProjectToolsHost.CloseRunPane(ProjectRunViewModel run) => _shell.ClosePaneCommand.Execute(run);

    Task<OpenedPath> IProjectToolsHost.OpenPathAsync(string path, int? line, int? column) => OpenPathAsync(path, line, column);

    void IProjectToolsHost.ProjectChanged() => _shell.FollowEditorLogs();

    void IProjectToolsHost.ProjectLaunched() => _shell.OnProjectLaunched(this);

    /// <summary>
    /// Whether this tab started <paramref name="pid"/>, through its <c>claude</c> (Claude running a commandlet) or its
    /// project job, for which tab an editor's log goes under (DESIGN.md §18, "Editor logs"). Safe off the UI thread.
    /// </summary>
    internal bool StartedProcess(int pid) => ProcessMonitor.Started(pid) || ProjectTools.Runs.JobTreeHolding(pid) is not null;

    // ---- Remote Control --------------------------------------------------------------------------------------------

    void IRemoteControlHost.CommandTurnEnded() => _checkIns.TurnEnded();

    string? IRemoteControlHost.AccountRemoteControlReason => AccountRemoteControlReason;

    async Task IRemoteControlHost.RestartSessionAsync()
    {
        await StopSessionAsync();
        await EnsureStartedAsync();
    }

    // ---- Perforce --------------------------------------------------------------------------------------------------

    Task IPerforceHost.SendRawAsync(string text) => SendRawAsync(text);

    void IPerforceHost.WaitOnUser(object key)
    {
        _waitingOnUser.Add(key);
        _checkIns.SetWaitingOnUser(true);
        UpdateStatus();
    }

    void IPerforceHost.Resolved(object key) => PermissionResolved(key);

    void IPerforceHost.LinkValuesChanged() => ProjectTools.OnLinkValuesChanged();

    // ---- Context and tokens ----------------------------------------------------------------------------------------

    string? IContextHost.ModelDisplayName(string? id) => ModelDisplayName(id);

    void IContextHost.TurnTokensChanged() => Working.Refresh();

    // ---- Markdown --------------------------------------------------------------------------------------------------

    void IMarkdownHost.ShowImage(ToolImage image) => OpenImage(image);
}
