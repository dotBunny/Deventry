using Deventry.Core.Tickets;

namespace Deventry.App.ViewModels;

/// <summary>
/// The ticket a tab was started on (DESIGN.md §18, "Tickets", "Tabs remember their ticket"): kept with the tab, named on
/// its info card, and gone back to from its menu's <b>Open ticket</b>.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The tab works on a ticket: its menu has <b>Open ticket</b>.</summary>
    public bool HasTicket => State.Ticket is not null;

    /// <summary>The info card's <b>Ticket</b> row: <i>GitHub issue #142: Grappling hook detaches on slopes</i>.</summary>
    private InfoRow? TicketRow => State.Ticket is { } ticket
        ? new InfoRow("Ticket", $"{(ticket.Kind.Length > 0 ? ticket.Kind : ticket.Key)}: {ticket.Title}")
        : null;

    /// <summary>
    /// <b>Work on it</b> opened this tab on a ticket, before it's selected and started: it remembers the ticket, has it as
    /// an attachment in its composer with <i>Work on this ticket.</i> typed, and is named after it when
    /// <paramref name="nameTab"/>, as a rename names it. Nothing is sent.
    /// </summary>
    internal void StartOnTicket(TicketRef ticket, string text, bool nameTab)
    {
        State.Ticket = ticket;
        if (nameTab)
        {
            State.UserName = $"{ticket.Key} {ticket.Title}";
            NameChanged();
        }
        AddPastedText(text);
        ComposerText = TicketPrompt.Typed;
        OnPropertyChanged(nameof(HasTicket));
        OnPropertyChanged(nameof(InfoRows));
    }
}
