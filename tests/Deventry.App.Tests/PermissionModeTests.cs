using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>The mode a tab starts in, and Auto in the mode picker (DESIGN.md §7, "Starting mode").</summary>
public class PermissionModeTests
{
    [Fact]
    public async Task A_new_tab_starts_in_auto_mode_as_a_terminal_session_would()
    {
        await using var h = new TabTestHarness();

        await h.OpenTabAsync();

        Assert.Equal("auto", h.Factory.Launches[0].PermissionMode);
    }

    [Fact]
    public async Task A_mode_chosen_in_Settings_or_Tab_settings_is_used_instead()
    {
        await using var h = new TabTestHarness(s => s.NewTabs.DefaultPermissionMode = "acceptEdits");

        await h.OpenTabAsync();

        Assert.Equal("acceptEdits", h.Factory.Launches[0].PermissionMode);
    }

    [Fact]
    public async Task A_default_mode_in_Claude_Codes_own_settings_is_left_to_Claude_Code()
    {
        await using var h = new TabTestHarness();
        UseClaudeSettings(h, """{ "permissions": { "defaultMode": "plan" } }""");

        var tab = await h.OpenTabAsync();

        Assert.Null(h.Factory.Launches[0].PermissionMode);
        Assert.Equal("Default (Plan)", TabSettings(h, tab).ModeChoices[0].Label);
    }

    [Fact]
    public async Task A_resumed_tab_is_switched_to_auto_mode_once_it_has_started()
    {
        await using var h = new TabTestHarness();

        var tab = await RestoreAsync(h);

        // Without the flag, so Claude Code can bring back plan mode; it came back in Manual, so Deventry switches.
        Assert.Equal("s1", h.Factory.Launches[0].Resume);
        Assert.Null(h.Factory.Launches[0].PermissionMode);
        Assert.Equal(["auto"], ModeRequests(h));
        Assert.Equal("Auto", tab.PermissionModeName);
    }

    [Fact]
    public async Task A_resumed_tab_that_comes_back_in_plan_mode_stays_there()
    {
        await using var h = new TabTestHarness();
        var initialize = h.Transport.Answers["initialize"];
        h.Transport.Answers["initialize"] = request =>
        {
            var response = initialize(request)!;
            response["current_permission_mode"] = "plan";
            return response;
        };

        var tab = await RestoreAsync(h);

        Assert.Empty(ModeRequests(h));
        Assert.Equal("Plan", tab.PermissionModeName);
    }

    [Fact]
    public async Task Auto_is_in_the_picker_and_switches_the_session()
    {
        await using var h = new TabTestHarness(s => s.NewTabs.DefaultPermissionMode = "default");
        var tab = await h.OpenTabAsync();
        Assert.Equal("Manual", tab.PermissionModeName);
        Assert.Equal(["Manual", "Accept edits", "Plan", "Auto", "Bypass permissions"], tab.PermissionModeChoices.Select(c => c.Label));

        await tab.ChooseModeCommand.ExecuteAsync(tab.PermissionModeChoices.Single(c => c.IsAuto));

        Assert.Equal(["auto"], ModeRequests(h));
        Assert.Equal("Auto", tab.PermissionModeName);
    }

    [Fact]
    public async Task Auto_isnt_offered_for_a_model_without_it()
    {
        // Haiku: Claude Code's initialize reply leaves supportsAutoMode out.
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Assert.Contains(tab.PermissionModeChoices, c => c.IsAuto);
        Assert.True(tab.IsAutoModeAvailable);
        var switched = new List<string?>();
        tab.PropertyChanged += (_, e) => switched.Add(e.PropertyName);

        tab.ChooseModelCommand.Execute(tab.Models.Single(m => m.Value == "haiku"));
        await tab.ModelSwitch.ConfirmCommand.ExecuteAsync(null);

        Assert.DoesNotContain(tab.PermissionModeChoices, c => c.IsAuto);
        Assert.Contains(nameof(TabViewModel.PermissionModeChoices), switched);
        // Nor on a plan's card.
        Assert.False(tab.IsAutoModeAvailable);
        Assert.Contains(nameof(TabViewModel.IsAutoModeAvailable), switched);
    }

    [Fact]
    public async Task Auto_isnt_offered_or_asked_for_when_Claude_Codes_settings_turn_it_off()
    {
        await using var h = new TabTestHarness();
        UseClaudeSettings(h, """{ "disableAutoMode": "disable" }""");

        var tab = await h.OpenTabAsync();

        Assert.Null(h.Factory.Launches[0].PermissionMode);
        Assert.DoesNotContain(tab.PermissionModeChoices, c => c.IsAuto);
        Assert.False(tab.IsAutoModeAvailable);
        Assert.Equal("Default (Manual)", TabSettings(h, tab).ModeChoices[0].Label);
    }

    [Fact]
    public async Task Tab_settings_and_Settings_name_Claude_Codes_default()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        using var settings = new SettingsViewModel(h.Services, null);

        Assert.Equal("Default (Auto)", TabSettings(h, tab).ModeChoices[0].Label);
        Assert.Equal("Claude Code's default (Auto)", settings.Tabs.ModeChoices[0].Label);

        h.Services.Settings.NewTabs.DefaultPermissionMode = "acceptEdits";
        Assert.Equal("Default (Accept edits)", TabSettings(h, tab).ModeChoices[0].Label);
    }

    [Fact]
    public async Task Going_back_to_the_default_in_Tab_settings_switches_back_to_auto_mode()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var previous = tab.State.Overrides;
        tab.State.Overrides = new TabOverrides { PermissionMode = "plan" };
        await tab.ApplyOverridesAsync(previous);

        previous = tab.State.Overrides;
        tab.State.Overrides = new TabOverrides();
        await tab.ApplyOverridesAsync(previous);

        Assert.Equal(["plan", "auto"], ModeRequests(h));
        Assert.Equal("Auto", tab.PermissionModeName);
    }

    [Fact]
    public async Task A_restored_tab_shows_the_mode_it_starts_in_before_it_starts()
    {
        await using var h = new TabTestHarness();

        var tab = RestoreUnstarted(h);

        // Claude Code's settings files are read in the background, as the tab is made.
        await TabTestHarness.Eventually(() => tab.PermissionModeName == "Auto", "the mode it starts in");
        Assert.Equal(TabStatus.NotStarted, tab.Status);
        Assert.Empty(h.Factory.Launches);
        Assert.Contains(tab.InfoRows, r => r.Label == "Mode" && r.Value == "Auto");
        Assert.Equal("Default (Auto)", TabSettings(h, tab).ModeChoices[0].Label);
        // Picking the mode it shows doesn't start it.
        await InlineDispatcher.RunAsync(() => tab.ChooseModeCommand.ExecuteAsync(tab.PermissionModeChoices.Single(c => c.IsAuto)));
        Assert.Empty(h.Factory.Launches);

        // What it showed is what it asks Claude Code for.
        h.Start(tab);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the tab to start");
        Assert.Equal("auto", Assert.Single(h.Factory.Launches).PermissionMode);
    }

    [Fact]
    public async Task A_mode_chosen_in_Tab_settings_shows_before_the_tab_starts()
    {
        await using var h = new TabTestHarness();

        var tab = RestoreUnstarted(h, new TabOverrides { PermissionMode = "plan" });

        Assert.Equal("Plan", tab.PermissionModeName);
        Assert.False(tab.IsBypassMode);

        var previous = tab.State.Overrides;
        tab.State.Overrides = new TabOverrides { PermissionMode = "acceptEdits" };
        await tab.ApplyOverridesAsync(previous);
        Assert.Equal("Accept edits", tab.PermissionModeName);
        Assert.Empty(h.Factory.Launches);
    }

    [Fact]
    public async Task A_tab_that_starts_in_Bypass_has_its_warning_before_it_starts()
    {
        await using var h = new TabTestHarness();

        var tab = RestoreUnstarted(h, new TabOverrides { PermissionMode = PermissionModeInfo.Bypass });

        Assert.Equal("Bypass permissions", tab.PermissionModeName);
        Assert.True(tab.IsBypassMode);
        Assert.Empty(h.Factory.Launches);
    }

    [Fact]
    public async Task An_unloaded_tab_shows_the_mode_its_next_start_brings_not_the_one_it_was_in()
    {
        await using var h = new TabTestHarness(s => s.Sessions.UnloadIdleTabs = true);
        var tab = await h.OpenTabAsync();
        await InlineDispatcher.RunAsync(() => tab.ChooseModeCommand.ExecuteAsync(tab.PermissionModeChoices.Single(c => c.Value == "acceptEdits")));
        Assert.Equal("Accept edits", tab.PermissionModeName);
        tab.ComposerText = "first";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the turn to end");

        h.Time.Advance(TimeSpan.FromMinutes(61));
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.NotStarted, "the tab to unload");

        // A resumed session doesn't keep a mode picked in the run before: it starts again in Auto.
        Assert.Equal("Auto", tab.PermissionModeName);
    }

    private static void UseClaudeSettings(TabTestHarness h, string json)
    {
        var config = Path.Combine(h.Root, "claude-config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "settings.json"), json);
        h.Services.ClaudeConfigDirectory = config;
    }

    private static async Task<TabViewModel> RestoreAsync(TabTestHarness h)
    {
        h.WriteTranscript("s1", Wire.Entry("user", "2026-09-27T09:30:00Z", new JsonObject { ["role"] = "user", ["content"] = "Plan the parser fix" }));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];
        h.Shell.Restore(null);
        h.Start();
        var tab = h.Shell.AllTabs.Single();
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the restored tab to start");
        return tab;
    }

    /// <summary>A restored tab, selected, which starts only with its first message (DESIGN.md §9, "Starting fast").</summary>
    private static TabViewModel RestoreUnstarted(TabTestHarness h, TabOverrides? overrides = null)
    {
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, Overrides = overrides ?? new TabOverrides() }];
        h.Shell.Restore(null);
        return h.Shell.SelectedTab!;
    }

    private static TabSettingsViewModel TabSettings(TabTestHarness h, TabViewModel tab) => new(h.Services, tab, () => { });

    private static string[] ModeRequests(TabTestHarness h) =>
        h.Transport.Sent
            .Where(m => m["type"]?.GetValue<string>() == "control_request" && m["request"]!["subtype"]!.GetValue<string>() == "set_permission_mode")
            .Select(m => m["request"]!["mode"]!.GetValue<string>())
            .ToArray();
}
