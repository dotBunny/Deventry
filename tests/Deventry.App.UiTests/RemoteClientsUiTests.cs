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
using Deventry.Core.Remote;
using Deventry.Core.Settings;

namespace Deventry.App.UiTests;

/// <summary>Settings → Remote clients rendered headlessly (DESIGN.md §18, "Remote clients").</summary>
public class RemoteClientsUiTests
{
    private static async Task<(TabTestHarness H, SettingsWindow Window, SettingsViewModel Settings)> ShowAsync(string category = "Telegram")
    {
        var h = new TabTestHarness(settings => settings.RemoteClients.SetEnabled("telegram", true), dispatcher: new AvaloniaUiDispatcher());
        h.Services.RemoteClients.Credentials = new FakeCredentialStore { Secrets = { ["telegram-bot-token"] = "123456789:token" } };
        h.Services.RemoteClients.ClientFactory = (_, _, _) => new FakeRemoteClient();
        h.Services.State.RemoteClients.Identities["telegram"] = "deventry_bot";
        var pairing = h.Services.RemoteClients.PairingOf("telegram");
        Assert.Equal(PairingOutcome.Paired, pairing.TryPair(pairing.Start().Code, 11, "Matt", "reapazor"));
        await h.OpenTabAsync();
        h.Services.RemoteClients.Start(h.Shell);
        await Waiting.UntilAsync(() => h.Services.RemoteClients.IsActive, "the bot to start", poll: () => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
        var settings = new SettingsViewModel(h.Services, null) { SelectedCategory = category };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 900 };
        window.Show();
        await UiText.SettleUntilAsync(window, () => settings.Client("telegram").HasSecret && settings.Client("telegram").Accounts.Count > 0,
            "the token read and the account listed");
        return (h, window, settings);
    }

    [AvaloniaFact]
    public async Task Telegrams_page_shows_the_bot_and_who_it_answers()
    {
        var (h, window, settings) = await ShowAsync();
        await using var _ = h;
        using var __ = settings;

        var page = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PageScroller");

        await Verify(UiText.Describe(page, (h.Root, "{root}"), (Environment.MachineName, "{machine}")));
    }

    [AvaloniaFact]
    public async Task All_clients_says_what_they_can_do_and_when_they_send()
    {
        var (h, window, settings) = await ShowAsync(SettingsCategory.AllClients);
        await using var _ = h;
        using var __ = settings;

        var page = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PageScroller");

        await Verify(UiText.Describe(page, (h.Root, "{root}"), (Environment.MachineName, "{machine}")));
    }

    [AvaloniaTheory]
    [InlineData(AppStyle.Standard, "Light")]
    [InlineData(AppStyle.Standard, "Dark")]
    [InlineData(AppStyle.Claude, "Light")]
    [InlineData(AppStyle.Claude, "Dark")]
    public async Task What_Telegram_can_read_is_said_in_the_caution_colors(AppStyle style, string variant)
    {
        var app = (App)Application.Current!;
        var before = app.RequestedThemeVariant;
        var (h, window, settings) = await ShowAsync();
        await using var _ = h;
        using var __ = settings;
        try
        {
            app.Colors.Apply(style);
            app.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            h.Services.Settings.Appearance.Style = style;
            h.Services.SaveSettings();
            UiText.Settle(window);

            var note = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("settingsnote") && b.Classes.Contains("caution"));
            Assert.True(note.IsEffectivelyVisible);
            Assert.Equal(Brush(window, "CautionBackgroundBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(note.Background).Color);
            Assert.Equal(Brush(window, "CautionBorderBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(note.BorderBrush).Color);
            Assert.Contains("aren't end-to-end encrypted", note.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        }
        finally
        {
            app.Colors.Apply(AppStyle.Standard);
            app.RequestedThemeVariant = before;
            window.Close();
        }
    }

    private static Color Brush(Window window, string key) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource(window.ActualThemeVariant, key)).Color;
}
