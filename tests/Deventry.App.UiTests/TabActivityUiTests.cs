using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Controls;
using Deventry.App.Services;
using Deventry.App.Tests;
using Deventry.App.Tests.Support;
using Deventry.App.Views;
using Deventry.Core.Settings;

namespace Deventry.App.UiTests;

/// <summary>Tab activity's colored rows rendered (DESIGN.md §4, "Sidebar"): the washes behind a tab's row and its subagents'.</summary>
public class TabActivityUiTests
{
    [AvaloniaFact]
    public async Task A_working_rows_wash_fills_it_to_its_edges_and_corners_in_either_density_and_style()
    {
        await using var h = new TabTestHarness(settings => settings.Appearance.ShowSubagents = SubagentDisplay.InSidebar, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.ComposerText = "Fix it";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(Wire.Init());
        h.Transport.Emit(Wire.Agent("a1", "Fix the middleware", "Fix it"));
        h.Transport.Emit(Wire.TaskStarted("task-a", "a1"));
        await UiText.SettleUntilAsync(window, () => tab.ShowsWorkBar && tab.SubagentRows.Count == 1 && tab.SubagentRows[0].ShowsWorkBar, "the work");
        var row = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tabrow"));
        var agentRow = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("agentrow"));

        AssertFills(row);
        AssertFills(agentRow);

        h.Services.Settings.Appearance.Density = Density.Compact;
        h.Services.SaveSettings();
        UiText.Settle(window);
        AssertFills(row);
        AssertFills(agentRow);

        h.Services.Settings.Appearance.Style = AppStyle.Claude;
        h.Services.SaveSettings();
        UiText.Settle(window);
        AssertFills(row);
        AssertFills(agentRow);
        window.Close();
    }

    /// <summary>The row's wash covers it as its hover does: its whole width and height, with its corners.</summary>
    private static void AssertFills(Button row)
    {
        var wash = row.GetVisualDescendants().OfType<WorkBar>().Single();
        Assert.True(wash.IsEffectivelyVisible);
        var at = wash.TranslatePoint(default, row)!.Value;
        Assert.Equal(0, at.X, precision: 3);
        Assert.Equal(0, at.Y, precision: 3);
        Assert.Equal(row.Bounds.Width, wash.Bounds.Width, precision: 3);
        Assert.Equal(row.Bounds.Height, wash.Bounds.Height, precision: 3);
        Assert.Equal(row.CornerRadius, wash.CornerRadius);
    }
}
