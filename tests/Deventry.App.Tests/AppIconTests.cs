using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using Deventry.Platform.Notifications;

namespace Deventry.App.Tests;

/// <summary>
/// The Dock icon and taskbar button while tabs work, need input or wait for a usage limit to reset (DESIGN.md §10): the
/// animation and the flash.
/// </summary>
public class AppIconTests
{
    private const string BashRequest = """
        {"type":"control_request","request_id":"p1","request":{"subtype":"can_use_tool","tool_name":"Bash","tool_use_id":"t1","input":{"command":"npm test"}}}
        """;

    private static async Task<TabViewModel> StartWorkingAsync(TabTestHarness h)
    {
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "long job";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit("""{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-5-5"}""");
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Working, "working");
        return tab;
    }

    private static async Task NeedInputAsync(TabViewModel tab, TabTestHarness h)
    {
        h.Transport.Emit(BashRequest);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.NeedsInput, "needs input");
    }

    private static async Task AnswerAsync(TabViewModel tab)
    {
        tab.Items.OfType<PermissionItem>().Single().AllowCommand.Execute(null);
        await TabTestHarness.Eventually(() => tab.Status != TabStatus.NeedsInput, "the answer");
    }

    [Fact]
    public void Every_animation_has_its_frames()
    {
        foreach (var animation in new[] { AppIconAnimations.Chevrons, AppIconAnimations.Working, AppIconAnimations.Asking, AppIconAnimations.Hourglass, AppIconAnimations.Waiting })
        {
            Assert.True(animation.Frames.Count > 1, animation.Name);
            Assert.All(animation.Frames, frame => Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], frame.Png[..4]));
        }
    }

    [Fact]
    public void The_taskbar_overlays_frames_have_a_version_for_a_dark_taskbar()
    {
        foreach (var animation in new[] { AppIconAnimations.Chevrons, AppIconAnimations.Hourglass })
        {
            Assert.All(animation.Frames, frame => Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], frame.OnDark![..4]));
        }
        // The Dock's are on their tile, the same on a light or dark Dock.
        Assert.All(new[] { AppIconAnimations.Working, AppIconAnimations.Asking, AppIconAnimations.Waiting }.SelectMany(animation => animation.Frames),
            frame => Assert.Null(frame.OnDark));
    }

    [Fact]
    public async Task While_a_tab_works_the_taskbar_overlays_chevrons_light_in_turn()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var tab = await StartWorkingAsync(h);

        var chevrons = AppIconAnimations.Chevrons.Frames;
        Assert.Same(chevrons[0].Png, h.Notifier.Frame);
        Assert.Same(chevrons[0].OnDark, h.Notifier.FrameOnDark);
        Assert.Equal("Claude is working", h.Notifier.FrameDescription);
        h.Time.Advance(chevrons[0].Duration);
        Assert.Same(chevrons[1].Png, h.Notifier.Frame);
        foreach (var frame in chevrons.Skip(1))
        {
            h.Time.Advance(frame.Duration);
        }
        Assert.Same(chevrons[0].Png, h.Notifier.Frame);

        h.Transport.EmitTurn("done");
        await TabTestHarness.Eventually(() => tab.Status != TabStatus.Working, "the turn to end");
        Assert.Null(h.Notifier.Frame);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(h.Notifier.Frame);
    }

    [Fact]
    public async Task On_the_taskbar_the_count_of_tabs_needing_input_takes_the_chevrons_place()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var tab = await StartWorkingAsync(h);

        await NeedInputAsync(tab, h);
        Assert.Equal(1, h.Notifier.Badge);
        Assert.Null(h.Notifier.Frame);

        await AnswerAsync(tab);
        Assert.Equal(TabStatus.Working, tab.Status);
        Assert.Equal(0, h.Notifier.Badge);
        Assert.Same(AppIconAnimations.Chevrons.Frames[0].Png, h.Notifier.Frame);
    }

    [Fact]
    public async Task With_the_badge_off_the_chevrons_keep_lighting_while_another_tab_needs_input()
    {
        await using var h = new TabTestHarness(settings => settings.Notifications.Badge = false);
        h.Notifier.Surface = AppIconSurface.Overlay;

        h.Services.Notifications.SetTabActivity(needingInput: 1, limited: 0, working: 1);

        Assert.Equal(0, h.Notifier.Badge);
        Assert.Same(AppIconAnimations.Chevrons.Frames[0].Png, h.Notifier.Frame);
    }

    [Fact]
    public async Task The_Dock_icon_lights_its_chevrons_while_tabs_work_and_asks_while_one_needs_input()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Icon;
        var tab = await StartWorkingAsync(h);

        var working = AppIconAnimations.Working.Frames;
        Assert.Same(working[0].Png, h.Notifier.Frame);
        h.Time.Advance(working[0].Duration);
        Assert.Same(working[1].Png, h.Notifier.Frame);

        await NeedInputAsync(tab, h);
        Assert.Same(AppIconAnimations.Asking.Frames[0].Png, h.Notifier.Frame);
        Assert.Equal("A tab needs your input", h.Notifier.FrameDescription);
        Assert.Equal(1, h.Notifier.Badge);

        await AnswerAsync(tab);
        Assert.Same(working[0].Png, h.Notifier.Frame);

        h.Transport.EmitTurn("done");
        await TabTestHarness.Eventually(() => h.Notifier.Frame is null, "Deventry's own icon");
    }

    [Fact]
    public async Task While_a_usage_limit_stops_a_task_the_taskbar_overlay_runs_the_hourglass_until_it_continues()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var tab = await h.OpenTabAsync();
        var reset = h.Time.GetUtcNow() + TimeSpan.FromHours(2);

        await AutoContinueTests.HitLimitAsync(h, tab, reset);

        var hourglass = AppIconAnimations.Hourglass.Frames;
        Assert.Same(hourglass[0].Png, h.Notifier.Frame);
        Assert.Equal("Waiting for a usage limit to reset", h.Notifier.FrameDescription);
        foreach (var frame in hourglass)
        {
            h.Time.Advance(frame.Duration);
        }
        Assert.Same(hourglass[0].Png, h.Notifier.Frame);
        Assert.False(h.Notifier.Flashing);

        await AutoContinueTests.AdvanceToAsync(h, reset + AutoContinueMonitor.Grace);

        await TabTestHarness.Eventually(() => !tab.HasLimitWait && tab.Status == TabStatus.Working, "the continue");
        Assert.Same(AppIconAnimations.Chevrons.Frames[0].Png, h.Notifier.Frame);
    }

    [Fact]
    public async Task On_the_taskbar_the_hourglass_comes_after_the_count_and_before_the_chevrons()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var notifications = h.Services.Notifications;

        notifications.SetTabActivity(needingInput: 0, limited: 1, working: 1);
        Assert.Same(AppIconAnimations.Hourglass.Frames[0].Png, h.Notifier.Frame);

        notifications.SetTabActivity(needingInput: 1, limited: 1, working: 1);
        Assert.Equal(1, h.Notifier.Badge);
        Assert.Null(h.Notifier.Frame);

        notifications.SetTabActivity(needingInput: 0, limited: 0, working: 1);
        Assert.Same(AppIconAnimations.Chevrons.Frames[0].Png, h.Notifier.Frame);
    }

    [Fact]
    public async Task The_Dock_icon_waits_by_the_hourglass_unless_a_tab_needs_input()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Icon;
        var notifications = h.Services.Notifications;

        notifications.SetTabActivity(needingInput: 0, limited: 1, working: 1);
        Assert.Same(AppIconAnimations.Waiting.Frames[0].Png, h.Notifier.Frame);
        Assert.Equal("Waiting for a usage limit to reset", h.Notifier.FrameDescription);

        notifications.SetTabActivity(needingInput: 1, limited: 1, working: 0);
        Assert.Same(AppIconAnimations.Asking.Frames[0].Png, h.Notifier.Frame);
    }

    [Fact]
    public async Task Dont_continue_takes_the_hourglass_away()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var tab = await h.OpenTabAsync();
        await AutoContinueTests.HitLimitAsync(h, tab, h.Time.GetUtcNow() + TimeSpan.FromHours(2));

        tab.DontContinueCommand.Execute(null);

        await TabTestHarness.Eventually(() => tab.CanContinueAfterLimit, "the wait to stop");
        Assert.Null(h.Notifier.Frame);
    }

    [Fact]
    public async Task A_wait_that_wont_continue_by_itself_shows_the_hourglass_until_the_limit_resets()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var tab = await h.OpenTabAsync();
        tab.State.Overrides.ContinueAfterLimitReset = false;
        var reset = h.Time.GetUtcNow() + TimeSpan.FromHours(2);

        await AutoContinueTests.HitLimitAsync(h, tab, reset);
        Assert.Same(AppIconAnimations.Hourglass.Frames[0].Png, h.Notifier.Frame);

        await AutoContinueTests.AdvanceToAsync(h, reset);

        await TabTestHarness.Eventually(() => tab.LimitWait is { HasReset: true }, "the reset");
        Assert.Null(h.Notifier.Frame);
    }

    [Fact]
    public async Task A_restored_tab_still_waiting_for_its_limit_shows_the_hourglass()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Overlay;
        var reset = h.Time.GetUtcNow() + TimeSpan.FromHours(1);
        h.Services.State.Tabs =
        [
            new TabState
            {
                Folder = h.WorkFolder,
                IsPinned = true,
                LimitWait = new LimitWait(reset, "five_hour", reset + AutoContinueMonitor.Grace, LimitWaitHold.None),
            },
        ];

        h.Shell.Restore(null);
        h.Start();

        Assert.Same(AppIconAnimations.Hourglass.Frames[0].Png, h.Notifier.Frame);
    }

    [Fact]
    public async Task The_animation_can_be_turned_off()
    {
        await using var h = new TabTestHarness();
        h.Notifier.Surface = AppIconSurface.Icon;
        await StartWorkingAsync(h);
        Assert.NotNull(h.Notifier.Frame);

        h.Services.Settings.Notifications.AnimateIcon = false;
        h.Services.SaveSettings();

        Assert.Null(h.Notifier.Frame);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(h.Notifier.Frame);
    }

    [Fact]
    public async Task The_taskbar_button_flashes_while_a_tab_needs_input_and_Deventry_is_in_the_background()
    {
        await using var h = new TabTestHarness();
        var tab = await StartWorkingAsync(h);

        await NeedInputAsync(tab, h);
        Assert.True(h.Notifier.Flashing);

        await AnswerAsync(tab);
        Assert.False(h.Notifier.Flashing);
    }

    [Fact]
    public async Task Coming_to_the_front_stops_the_flash()
    {
        await using var h = new TabTestHarness();
        var tab = await StartWorkingAsync(h);
        await NeedInputAsync(tab, h);

        h.Services.Notifications.SetAppActive(true);

        Assert.False(h.Notifier.Flashing);
        Assert.Equal(TabStatus.NeedsInput, tab.Status);
    }

    [Fact]
    public async Task No_flash_while_Deventry_is_in_front()
    {
        await using var h = new TabTestHarness();
        var tab = await StartWorkingAsync(h);
        h.Services.Notifications.SetAppActive(true);

        await NeedInputAsync(tab, h);

        Assert.Equal(0, h.Notifier.Flashes);
    }

    [Fact]
    public async Task No_flash_with_the_needs_input_notification_off()
    {
        await using var h = new TabTestHarness(settings => settings.Notifications.NeedsInput = false);
        var tab = await StartWorkingAsync(h);

        await NeedInputAsync(tab, h);

        Assert.Equal(0, h.Notifier.Flashes);
    }
}
