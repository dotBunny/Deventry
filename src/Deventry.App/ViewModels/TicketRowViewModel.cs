using System.Collections.ObjectModel;
using Deventry.Core;
using Deventry.Core.Tickets;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveMarkdown.Avalonia;

namespace Deventry.App.ViewModels;

/// <summary>A row of the Tickets tab's list (DESIGN.md §18, "Tickets", "The list").</summary>
public sealed partial class TicketRowViewModel(TicketSource source, Ticket ticket, int tabCount, TimeProvider time) : ObservableObject
{
    public TicketSource Source { get; } = source;

    public Ticket Ticket { get; } = ticket;

    public string Key => Ticket.Key;

    public string Title => Ticket.Title;

    public bool IsOpen => Ticket.IsOpen;

    private int _tabCount = tabCount;

    /// <summary>How many open tabs work on it.</summary>
    public int TabCount
    {
        get => _tabCount;
        set
        {
            if (SetProperty(ref _tabCount, value))
            {
                OnPropertyChanged(nameof(Detail));
            }
        }
    }

    /// <summary>Its second line: its labels, how long ago it was opened, its comments and its tabs.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (Ticket.Labels.Count > 0)
            {
                parts.Add(string.Join(" · ", Ticket.Labels));
            }
            parts.Add(Formats.Ago(time.GetUtcNow() - Ticket.Created));
            if (Ticket.Comments > 0)
            {
                parts.Add($"{Ticket.Comments} comment{(Ticket.Comments == 1 ? "" : "s")}");
            }
            if (TabCount > 0)
            {
                parts.Add($"{TabCount} tab{(TabCount == 1 ? "" : "s")}");
            }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Its tip: the title in full, its state, who opened it and when.</summary>
    public string Tip => $"{Ticket.Key} {Ticket.Title}\n{Ticket.StateName}{(Ticket.Author is { } author ? $" · opened by {author}" : "")} {Formats.Ago(time.GetUtcNow() - Ticket.Created)}";

    public string AccessibleName => $"{Ticket.Key} {Ticket.Title}, {(Ticket.IsOpen ? "open" : Ticket.StateName)}";
}

/// <summary>A comment on the ticket shown, as Markdown.</summary>
public sealed class TicketCommentViewModel(TicketComment comment, TimeProvider time)
{
    public string Author => comment.Author;

    public string When => Formats.Ago(time.GetUtcNow() - comment.Created);

    public ObservableStringBuilder Markdown { get; } = new(comment.Body);
}

/// <summary>The ticket the Tickets tab shows (DESIGN.md §18, "Tickets", "A ticket"): its fields, text and comments.</summary>
public sealed partial class TicketDetailViewModel : ObservableObject
{
    public TicketDetailViewModel(TicketSource source, TicketDetail detail, ITicketProviderType? type, IReadOnlyList<TabViewModel> tabs, TimeProvider time)
    {
        Source = source;
        Detail = detail;
        Provider = type;
        Body = new ObservableStringBuilder(detail.Body.Trim().Length > 0 ? detail.Body : "*No description.*");
        foreach (var comment in detail.Comments)
        {
            Comments.Add(new TicketCommentViewModel(comment, time));
        }
        var ticket = detail.Ticket;
        var facts = new List<string> { ticket.IsOpen ? $"● {ticket.StateName}" : ticket.StateName };
        if (ticket.Labels.Count > 0)
        {
            facts.Add(string.Join(" · ", ticket.Labels));
        }
        facts.Add(ticket.Author is { } author ? $"{author} opened {Formats.Ago(time.GetUtcNow() - ticket.Created)}" : $"opened {Formats.Ago(time.GetUtcNow() - ticket.Created)}");
        StateLine = string.Join(" · ", facts);
        var fields = new List<string>();
        if (ticket.Assignees.Count > 0)
        {
            fields.Add($"Assigned to {string.Join(", ", ticket.Assignees)}");
        }
        fields.AddRange(detail.Fields.Select(f => $"{f.Name}: {f.Value}"));
        FieldsLine = string.Join(" · ", fields);
        UpdateTabs(tabs);
    }

    public TicketSource Source { get; }

    public TicketDetail Detail { get; }

    public Ticket Ticket => Detail.Ticket;

    public ITicketProviderType? Provider { get; }

    public string Key => Ticket.Key;

    public string Title => Ticket.Title;

    /// <summary><i>● Open · bug · gameplay · kim opened 2 days ago</i>.</summary>
    public string StateLine { get; }

    /// <summary>Its other fields: <i>Assigned to kim · Milestone: 0.4</i>; empty without any.</summary>
    public string FieldsLine { get; }

    public bool HasFields => FieldsLine.Length > 0;

    /// <summary><b>Open on GitHub</b>.</summary>
    public string OpenOnText => $"Open on {Provider?.Name ?? "the web"}";

    /// <summary>What it's called with its tracker's word: <i>GitHub issue #142</i>.</summary>
    public string KindName => Provider is { } type ? $"{type.Name} {type.TicketWord} {Ticket.Key}" : Ticket.Key;

    public ObservableStringBuilder Body { get; }

    public ObservableCollection<TicketCommentViewModel> Comments { get; } = [];

    public bool HasComments => Ticket.Comments > 0;

    /// <summary>Over the comments: <i>3 comments</i>.</summary>
    public string CommentsHeader => $"{Ticket.Comments} comment{(Ticket.Comments == 1 ? "" : "s")}";

    /// <summary>Comments that weren't read: <i>The last 100 of 140 comments.</i></summary>
    public string? LeftOutText => Detail.CommentsLeftOut > 0 ? $"The last {Detail.Comments.Count} of {Ticket.Comments} comments." : null;

    public bool HasLeftOut => LeftOutText is not null;

    /// <summary>The open tabs started on it: <i>Tab on it: #142 Grappling hook · Go to tab</i>.</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public bool HasTabs => Tabs.Count > 0;

    internal void UpdateTabs(IReadOnlyList<TabViewModel> tabs)
    {
        if (tabs.SequenceEqual(Tabs))
        {
            return;
        }
        Tabs.Clear();
        foreach (var tab in tabs)
        {
            Tabs.Add(tab);
        }
        OnPropertyChanged(nameof(HasTabs));
    }
}
