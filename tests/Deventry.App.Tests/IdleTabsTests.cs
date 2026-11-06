using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// A restored tab starting with its first message rather than when selected (DESIGN.md §9, "Starting fast"), and idle
/// tabs unloading (DESIGN.md §9, "Unloading idle tabs").
/// </summary>
public class IdleTabsTests
{
    [Fact]
    public async Task A_selected_restored_tab_starts_only_with_its_first_message()
    {
        await using var h = new TabTestHarness();
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true }];
        h.Shell.Restore(null);
        var tab = h.Shell.SelectedTab!;
        Assert.True(tab.IsShown);
        await Waiting.NeverAsync(() => h.Factory.Launches.Count > 0, "a start on selecting");
        Assert.Equal(TabStatus.NotStarted, tab.Status);

        tab.ComposerText = "hello";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains("hello"), "the message");
        Assert.Single(h.Factory.Launches);
    }

    [Fact]
    public async Task An_idle_tab_unloads_after_the_time_set_and_its_next_message_starts_it_again()
    {
        await using var h = new TabTestHarness(s => s.Sessions.UnloadIdleTabs = true);
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "first";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the turn to end");

        h.Time.Advance(TimeSpan.FromMinutes(30));
        await Waiting.NeverAsync(() => tab.Status == TabStatus.NotStarted, "an unload too soon");
        h.Time.Advance(TimeSpan.FromMinutes(31));
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.NotStarted, "the tab to unload");
        Assert.Contains(tab.Items, i => i is Conversation.UserMessageItem);

        tab.ComposerText = "second";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && h.Transport.SentUserTextsTo(1).Contains("second"), "the session to start again");
        Assert.Equal("s1", h.Factory.Launches[1].Resume);
    }

    [Fact]
    public async Task An_idle_tab_stays_loaded_with_unloading_off()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.Time.Advance(TimeSpan.FromHours(3));
        await Waiting.NeverAsync(() => tab.Status == TabStatus.NotStarted, "an unload");
    }
}
