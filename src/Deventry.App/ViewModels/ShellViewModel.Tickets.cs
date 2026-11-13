using Deventry.Core.Settings;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// The folder groups' Tickets tabs (DESIGN.md §18, "Tickets"): opening one from its group, closing it, bringing them
/// back at launch, whether a group has a ticket source for its button, and <b>Work on it</b>, which starts a tab on a
/// ticket.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>The focused tab when it's a Tickets tab.</summary>
    public TicketsViewModel? SelectedTickets => Selected as TicketsViewModel;

    /// <summary>The group's <b>Tickets</b> button, its menu's <b>Tickets</b> and the palette's <b>Open tickets</b>.</summary>
    [RelayCommand]
    private void OpenTickets(TabGroupViewModel? group)
    {
        if (group is not null)
        {
            Selected = TicketsFor(group);
        }
    }

    /// <summary>A click on a Tickets tab's row.</summary>
    [RelayCommand]
    private void SelectTickets(TicketsViewModel? tickets)
    {
        if (tickets is not null)
        {
            Selected = tickets;
        }
    }

    /// <summary>The group's Tickets tab, opened if it isn't.</summary>
    internal TicketsViewModel TicketsFor(TabGroupViewModel group)
    {
        if (group.Tickets is { } open)
        {
            return open;
        }
        var tickets = new TicketsViewModel(_services, this, group, new TicketsTabState { Folder = group.Folder });
        AddTickets(group, tickets);
        SaveTabs();
        return tickets;
    }

    private void AddTickets(TabGroupViewModel group, TicketsViewModel tickets)
    {
        group.Tickets = tickets;
        OpenTabs.Add(tickets);
    }

    /// <summary>Closes a Tickets tab. Its group leaves the sidebar with it when nothing else is open in it.</summary>
    internal void CloseTickets(TicketsViewModel tickets)
    {
        var group = tickets.Group;
        var pane = _panes.IndexOf(tickets);
        if (pane >= 0)
        {
            _panes.RemoveAt(pane);
        }
        OpenTabs.Remove(tickets);
        group.Tickets = null;
        if (ReferenceEquals(Selected, tickets))
        {
            Selected = _panes.Count > 0 ? _panes[Math.Min(pane, _panes.Count - 1)]
                : group.Tabs.FirstOrDefault() ?? (IShellTab?)group.Files ?? AllTabs.FirstOrDefault();
        }
        else if (pane >= 0)
        {
            UpdatePanes();
        }
        RemoveGroupIfEmpty(group);
        SaveTabs();
        _ = tickets.DisposeAsync();
    }

    /// <summary>The Tickets tabs saved, each in its group, which is made for it when nothing of its folder came back.</summary>
    private void RestoreTickets()
    {
        foreach (var saved in _services.State.Tickets.Where(t => t.Folder.Length > 0 && Directory.Exists(t.Folder)).ToArray())
        {
            var group = GroupFor(saved.Folder);
            if (group.Tickets is null)
            {
                AddTickets(group, new TicketsViewModel(_services, this, group, saved));
            }
        }
    }

    /// <summary>Tabs opened or closed: each Tickets tab's rows count their tabs again.</summary>
    private void OnTicketTabsChanged()
    {
        foreach (var tickets in Groups.Select(g => g.Tickets).OfType<TicketsViewModel>())
        {
            tickets.OnTabsChanged();
        }
    }

    /// <summary>
    /// Whether the group's folder has a ticket source, for its row's <b>Tickets</b> button: read from its files and git's,
    /// off the UI thread, when the group is made, when Deventry comes to the front, and when Settings → Tickets changes.
    /// </summary>
    private void UpdateTicketSources(TabGroupViewModel group)
    {
        var folder = group.Folder;
        _ = Task.Run(() => _services.Tickets.SourcesFor(folder).Sources.Count > 0).ContinueWith(
            read => _services.Dispatcher.Post(() => group.HasTicketSources = read.Result),
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>The project settings saved a folder's ticket sources: its group's button and Tickets tab follow.</summary>
    internal void OnTicketSourcesChanged(string folder)
    {
        if (Groups.FirstOrDefault(g => FolderHistory.SamePath(g.Folder, folder)) is { } group)
        {
            UpdateTicketSources(group);
            if (group.Tickets is { } tickets)
            {
                _ = tickets.RefreshAsync();
            }
        }
    }

    private void UpdateAllTicketSources()
    {
        foreach (var group in Groups.ToArray())
        {
            UpdateTicketSources(group);
        }
    }

    /// <summary><b>Ticket sources…</b> and <b>Add a ticket source…</b>: the folder's project settings, on Tickets.</summary>
    [RelayCommand]
    private Task OpenTicketSourcesAsync(TabGroupViewModel? group) =>
        group is null ? Task.CompletedTask : OpenProjectSettingsAsync(group.Folder, ProjectSettingsWindowViewModel.TicketsPage);

    /// <summary>A provider's page in Settings → Tickets, or All providers without one.</summary>
    internal void OpenTicketSettings(string? kind) =>
        _ = OpenSettingsAtAsync(kind is not null && _services.Tickets.FindType(kind) is { } type ? type.Name : SettingsCategory.AllTicketProviders);

    // ---- Work on it (DESIGN.md §18, "Tickets", "Work on it") ----------------------------------------------------------

    /// <summary>
    /// A new tab on <paramref name="detail"/>'s ticket in the group's folder, or in a worktree named for it: the ticket as
    /// an attachment in its composer with <i>Work on this ticket.</i> typed, nothing sent; the tab named after the ticket
    /// as Settings says, and remembering it.
    /// </summary>
    internal Task StartWorkOnTicketAsync(TabGroupViewModel group, TicketDetailViewModel detail, bool inWorktree)
    {
        var settings = _services.Settings.Tickets;
        var type = _services.Tickets.FindType(detail.Source.Provider);
        var text = TicketPrompt.For(detail.Detail, type?.Name ?? detail.Source.Provider, type?.TicketWord ?? "ticket", settings.IncludeComments);
        var ticket = new TicketRef
        {
            Provider = detail.Source.Provider,
            SourceId = detail.Source.Id,
            Key = detail.Ticket.Key,
            Id = detail.Ticket.Id,
            Title = detail.Ticket.Title,
            Url = detail.Ticket.Url,
            Kind = detail.KindName,
        };
        void Opened(TabViewModel tab)
        {
            tab.StartOnTicket(ticket, text, settings.NameTabs);
            // Added before it knew its ticket: the ticket's row counts it now.
            OnTicketTabsChanged();
        }
        return inWorktree
            ? OpenWorktreeTabAsync(group.Folder, Opened, WorktreeNameFor(detail.Ticket))
            : OpenFolderAsync(group.Folder, Opened);
    }

    /// <summary>A ticket's worktree's name: <c>issue-142</c> for GitHub's <c>#142</c>, <c>no-42</c> for YouTrack's <c>NO-42</c>.</summary>
    internal static string WorktreeNameFor(Ticket ticket)
    {
        var key = ticket.Key.TrimStart('#');
        var name = new string([.. key.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        return ticket.Key.StartsWith('#') ? $"issue-{name}" : name.Length > 0 ? name : "ticket";
    }

    /// <summary><b>Open ticket</b> in a tab's menu: its project's Tickets tab, on the ticket the tab works on.</summary>
    [RelayCommand]
    private void OpenTabTicket(TabViewModel? tab)
    {
        if (tab?.State.Ticket is not { } ticket || Groups.FirstOrDefault(g => g.Tabs.Contains(tab)) is not { } group)
        {
            return;
        }
        var tickets = TicketsFor(group);
        Selected = tickets;
        tickets.Show(ticket);
    }
}
