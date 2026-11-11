using System.ComponentModel;

namespace Deventry.App.ViewModels.ToolWindows;

/// <summary>
/// What each tool window's icon says beside its title (DESIGN.md §3, "Tool windows"), as the side panel's page tabs did:
/// a busy dot while a subagent or a project's job runs, a warning dot when an MCP server needs looking at, and in its tip
/// what it holds now, such as Tasks' count, all of the session the windows show, kept up as it changes.
/// </summary>
public sealed partial class ToolDockViewModel
{
    private readonly List<INotifyPropertyChanged> _watched = [];

    /// <summary>Watches what the tabs say of <paramref name="session"/>, in place of the one before.</summary>
    private void WatchAdornments(TabViewModel? session)
    {
        foreach (var source in _watched)
        {
            source.PropertyChanged -= OnAdornmentSourceChanged;
        }
        _watched.Clear();
        if (session is not null)
        {
            _watched.AddRange([session, session.TodoList, session.ProjectTools.Runs, session.McpServers, session.Media]);
            foreach (var source in _watched)
            {
                source.PropertyChanged += OnAdornmentSourceChanged;
            }
        }
        UpdateAdornments(session);
    }

    private void OnAdornmentSourceChanged(object? sender, PropertyChangedEventArgs e) => UpdateAdornments(Session);

    private void UpdateAdornments(TabViewModel? session)
    {
        foreach (var window in _windows)
        {
            var (busy, warning, tip) = window.Kind.Id switch
            {
                "Agents" when session is not null => (session.HasActiveAgents, false, session.AgentsHeader),
                "Project" when session is not null => (session.ProjectTools.Runs.IsJobRunning, false, session.ProjectTools.HeaderTitle),
                "Tasks" when session is not null => (false, false, session.TodoList.PageTip),
                "Mcp" when session is not null => (false, session.McpServers.Attention is not null, session.McpServers.Attention ?? "MCP servers"),
                "Media" when session is not null => (false, false, session.Media.Tip),
                "ScratchPad" => (false, false, "Notes shared by the tabs in this project"),
                "Games" => (false, false, "Play a game against Joulene"),
                _ => (false, false, (string?)null),
            };
            window.IsBusy = busy;
            window.HasWarning = warning;
            window.Tip = tip;
        }
    }
}
