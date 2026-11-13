using Deventry.Core.Settings;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → Tickets → All providers (DESIGN.md §18, "Tickets"): what every ticket provider shares, working on a
/// ticket. Each provider has a page of its own after it (<see cref="TicketProviderPage"/>). All of it this computer's.
/// </summary>
public sealed class AllTicketProvidersPage : SettingsPage
{
    public AllTicketProvidersPage(SettingsContext context) : base(context, SettingsCategory.AllTicketProviders) =>
        Section("working", "Working on a ticket",
        [
            Setting("tickets.workIn", "Work on a ticket in", s => s.Tickets.WorkIn, (s, v) => s.Tickets.WorkIn = v,
                "What Work on it opens; its arrow offers both.", keywords: ["work on it", "worktree", "new tab"]),
            Setting("tickets.nameTabs", "Name new tabs after their ticket", s => s.Tickets.NameTabs, (s, v) => s.Tickets.NameTabs = v,
                "#142 Grappling hook detaches on slopes. Reset name gives Claude Code's back.", keywords: ["tab name", "rename"]),
            Setting("tickets.includeComments", "Include the ticket's comments", s => s.Tickets.IncludeComments, (s, v) => s.Tickets.IncludeComments = v,
                "In the ticket Claude gets, after its text.", keywords: ["comments", "prompt", "tokens"]),
        ], description: "The ticket goes in the new tab's message, ready to send: nothing is sent until you send it.");

    public override string IconKey => "IconTicket";

    public override string Description => "What every ticket provider shares: starting a tab on a ticket.";

    public bool WorkInTab
    {
        get => Settings.Tickets.WorkIn == TicketWorkIn.Tab;
        set
        {
            if (value)
            {
                SetWorkIn(TicketWorkIn.Tab);
            }
        }
    }

    public bool WorkInWorktree
    {
        get => Settings.Tickets.WorkIn == TicketWorkIn.Worktree;
        set
        {
            if (value)
            {
                SetWorkIn(TicketWorkIn.Worktree);
            }
        }
    }

    private void SetWorkIn(TicketWorkIn where)
    {
        if (Settings.Tickets.WorkIn == where)
        {
            return;
        }
        Settings.Tickets.WorkIn = where;
        Save();
        OnPropertyChanged(nameof(WorkInTab));
        OnPropertyChanged(nameof(WorkInWorktree));
    }

    public bool NameTabs
    {
        get => Settings.Tickets.NameTabs;
        set => Set(value, v => Settings.Tickets.NameTabs = v);
    }

    public bool IncludeComments
    {
        get => Settings.Tickets.IncludeComments;
        set => Set(value, v => Settings.Tickets.IncludeComments = v);
    }
}
