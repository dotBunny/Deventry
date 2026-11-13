using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.Views;
using Deventry.Core.ProjectTools;
using Deventry.Core.Tests.Support;

namespace Deventry.App.UiTests;

/// <summary>A project's Tickets tab, rendered (DESIGN.md §18, "Tickets"): the group's button, the list, and a ticket.</summary>
public class TicketsUiTests
{
    [AvaloniaFact]
    public async Task The_Tickets_tab_shows_the_list_and_the_ticket_with_its_comments_and_Work_on_it()
    {
        var github = new FakeGitHub();
        github.Add(142, "Grappling hook detaches on slopes", labels: ["bug", "gameplay"], daysAgo: 2, body: "The rope drops the player on slopes.", milestone: "0.4",
            assignees: ["kim"]);
        github.AddComments(142, 2);
        github.Add(139, "Save slots overwrite each other", labels: ["bug"], daysAgo: 5);
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher(), http: github);
        h.Time.Advance(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero) - h.Time.GetUtcNow());
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """{ "tickets": [ { "provider": "github", "repository": "org/game" } ] }""");
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1400, 800);
        await UiText.SettleUntilAsync(window, () => group.HasTicketSources, "the Tickets button");

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TicketsButton");
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Tickets", AutomationProperties.GetName(button));
        button.Command!.Execute(button.CommandParameter);
        var tickets = group.Tickets!;
        await UiText.SettleUntilAsync(window, () => tickets.HasRows && !tickets.IsLoading, "the tickets");
        tickets.SelectedRow = tickets.Rows[0];
        var view = window.GetVisualDescendants().OfType<TicketsView>().Single(v => v.IsEffectivelyVisible);
        // Its text and comments parse and lay out in the background.
        await UiText.SettleUntilAsync(window, () => UiText.Describe(view).Contains("Comment 2", StringComparison.Ordinal), "the ticket's comments");

        var shown = UiText.Describe(view);
        Assert.Contains("Work on it", shown, StringComparison.Ordinal);
        Assert.Contains("Open on GitHub", shown, StringComparison.Ordinal);
        Assert.Contains("The rope drops the player on slopes.", shown, StringComparison.Ordinal);
        Assert.Contains("Tickets", UiText.Describe(window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("filesrow") && b.DataContext == tickets)),
            StringComparison.Ordinal);
        window.Close();
        await Verify(shown);
    }
}
