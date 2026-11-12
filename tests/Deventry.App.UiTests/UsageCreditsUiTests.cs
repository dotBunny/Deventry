using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;
using Deventry.Core.Auth;
using Deventry.Core.Protocol;
using Deventry.Core.Settings;
using Deventry.Usage;

namespace Deventry.App.UiTests;

/// <summary>
/// The notice while usage credits pay for Claude (DESIGN.md §6, "Usage credits"): right under the header, in each
/// style's caution colors, until they stop paying, or dismissed until they start again.
/// </summary>
public class UsageCreditsUiTests
{
    [AvaloniaFact]
    public async Task The_notice_sits_under_the_header_until_credits_stop_paying_or_it_is_dismissed()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        using var usage = new UsageViewModel(h.Services, tracker);
        var resets = h.Time.GetUtcNow().AddHours(1).AddMinutes(20);
        tracker.OnRateLimitEvent(RateLimitEvent(1.0, resets, usingCredits: true));
        var window = Show(h, usage);
        await UiText.SettleUntilAsync(window, () => Notice(window).IsEffectivelyVisible, "the notice");
        var notice = Notice(window);

        // Straight under the header, before the other banners, with the islands' gap between it and the page.
        var banners = Assert.IsType<StackPanel>(notice.Parent);
        Assert.Equal(0, banners.Children.IndexOf(notice));
        var dock = Assert.IsType<DockPanel>(banners.Parent);
        Assert.Equal(1, dock.Children.IndexOf(banners));
        var page = dock.Children[^1];
        Assert.Equal(notice.TranslatePoint(new Point(0, notice.Bounds.Height), window)!.Value.Y + 6, page.TranslatePoint(default, window)!.Value.Y, 3);
        var buttons = notice.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["Manage on claude.ai", "Dismiss"], buttons.Select(b => b.Content));
        var described = UiText.Describe(notice);

        buttons[0].Command!.Execute(null);
        UiText.Settle(window);
        Assert.Equal([AccountViewModel.ClaudeUsageUrl], h.Platform.OpenedUrls);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        tracker.OnRateLimitEvent(RateLimitEvent(0.02, resets.AddHours(5), usingCredits: false));
        await UiText.SettleUntilAsync(window, () => !Notice(window).IsEffectivelyVisible, "the notice gone");

        // Paying again, in the next window, and dismissed.
        h.Time.Advance(TimeSpan.FromMinutes(1));
        tracker.OnRateLimitEvent(RateLimitEvent(1.0, resets.AddHours(5), usingCredits: true));
        await UiText.SettleUntilAsync(window, () => Notice(window).IsEffectivelyVisible, "the notice again");
        Notice(window).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Dismiss")).Command!.Execute(null);
        await UiText.SettleUntilAsync(window, () => !Notice(window).IsEffectivelyVisible, "the notice dismissed");

        // With no banner left, the gap goes too.
        usage.DismissAlertCommand.Execute(null);
        await UiText.SettleUntilAsync(window, () => !banners.IsVisible, "the banners gone");
        window.Close();

        await Verify(described);
    }

    [AvaloniaFact]
    public async Task The_notice_uses_each_styles_caution_colors_in_light_and_dark()
    {
        var app = (App)Application.Current!;
        var before = app.RequestedThemeVariant;
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        using var usage = new UsageViewModel(h.Services, tracker);
        tracker.OnRateLimitEvent(RateLimitEvent(1.0, h.Time.GetUtcNow().AddHours(1), usingCredits: true));
        var window = Show(h, usage);
        try
        {
            await UiText.SettleUntilAsync(window, () => Notice(window).IsEffectivelyVisible, "the notice");
            foreach (var style in new[] { AppStyle.Standard, AppStyle.Claude })
            {
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    app.Colors.Apply(style);
                    app.RequestedThemeVariant = variant;
                    UiText.Settle(window);
                    var what = $"{style} {variant}";
                    Assert.True(Token(window, "CautionBackgroundBrush") == ColorOf(Notice(window).Background), what);
                    Assert.True(Token(window, "CautionBorderBrush") == ColorOf(Notice(window).BorderBrush), what);
                }
            }
        }
        finally
        {
            app.Colors.Apply(AppStyle.Standard);
            app.RequestedThemeVariant = before;
            window.Close();
        }
    }

    private static MainWindow Show(TabTestHarness h, UsageViewModel usage)
    {
        var main = new MainWindowViewModel(h.Services) { CurrentPage = h.Shell, Usage = usage };
        main.Account.Status = new AuthStatus(true, "claude.ai", null, "me@example.com", null, "max", null, null);
        var window = new MainWindow { DataContext = main, Width = 1200, Height = 800 };
        window.Show();
        return window;
    }

    private static Border Notice(Window window) => window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CreditsNotice");

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Token(Window window, string key) => ColorOf(window.FindResource(window.ActualThemeVariant, key) as IBrush);

    /// <summary>A <c>rate_limit_event</c> for the session, saying whether usage credits are paying.</summary>
    private static RateLimitEventMessage RateLimitEvent(double session, DateTimeOffset resetsAt, bool usingCredits)
    {
        var info = new JsonObject
        {
            ["status"] = usingCredits ? "rejected" : "allowed",
            ["rateLimitType"] = "five_hour",
            ["resetsAt"] = resetsAt.ToUnixTimeSeconds(),
            ["isUsingOverage"] = usingCredits,
            ["unifiedWindows"] = new JsonObject
            {
                ["five_hour"] = new JsonObject { ["utilization"] = session, ["resetsAt"] = resetsAt.ToUnixTimeSeconds() },
                ["seven_day"] = new JsonObject { ["utilization"] = 0.6, ["resetsAt"] = resetsAt.AddDays(3).ToUnixTimeSeconds() },
            },
        };
        return new RateLimitEventMessage(info, new JsonObject { ["type"] = "rate_limit_event", ["rate_limit_info"] = info.DeepClone() });
    }
}
