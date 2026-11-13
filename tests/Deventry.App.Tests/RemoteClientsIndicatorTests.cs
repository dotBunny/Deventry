using System.Collections.Concurrent;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Remote;
using Deventry.Core.Remote.Telegram;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// The header's remote clients icon (DESIGN.md §18, "Remote clients", "The header's indicator"): shown while one is on,
/// the state that most needs the user winning across clients, each client's line, the quiet, and the arcs lighting up.
/// </summary>
public class RemoteClientsIndicatorTests
{
    private sealed class Indicator : IAsyncDisposable
    {
        public required TabTestHarness H { get; init; }

        public required ConcurrentDictionary<string, FakeRemoteClient> Clients { get; init; }

        public required RemoteClientsIndicatorViewModel View { get; init; }

        public FakeRemoteClient Telegram => Clients["telegram"];

        public FakeRemoteClient Discord => Clients["discord"];

        public T Read<T>(Func<RemoteClientsIndicatorViewModel, T> read) => InlineDispatcher.Read(() => read(View));

        /// <summary>Pairs an account with the client of <paramref name="kind"/>, so it has someone to send to.</summary>
        public Task PairAsync(string kind)
        {
            var pairing = H.Services.RemoteClients.PairingOf(kind);
            Assert.Equal(PairingOutcome.Paired, pairing.TryPair(pairing.Start().Code, 11, "Matt", "reapazor"));
            return TabTestHarness.Eventually(() => H.Services.State.RemoteClients.Accounts.Any(a => a.Client == kind), "the account kept");
        }

        public void SetAppActive(bool active) => H.OnUiThread(() => H.Services.Notifications.SetAppActive(active));

        public ValueTask DisposeAsync()
        {
            View.Dispose();
            return H.DisposeAsync();
        }
    }

    /// <summary>Deventry with Telegram and a second kind, Discord, offered, and the kinds in <paramref name="on"/> turned on and started.</summary>
    private static async Task<Indicator> StartAsync(params string[] on)
    {
        var h = new TabTestHarness(settings =>
        {
            foreach (var kind in on)
            {
                settings.RemoteClients.SetEnabled(kind, true);
            }
        });
        IRemoteClientType[] types = [new TelegramClientType(), new FakeRemoteClientType("discord", "Discord")];
        h.Services.RemoteClients.UseTypes(types);
        var keys = new FakeCredentialStore();
        foreach (var type in types)
        {
            keys.Secrets[h.Services.RemoteClients.CredentialKeyOf(type.Kind)] = "a secret";
        }
        h.Services.RemoteClients.Credentials = keys;
        var clients = new ConcurrentDictionary<string, FakeRemoteClient>(StringComparer.Ordinal);
        h.Services.RemoteClients.ClientFactory = (type, _, _) => clients[type.Kind] = new FakeRemoteClient();
        var view = new RemoteClientsIndicatorViewModel(h.Services);
        await h.OpenTabAsync();
        h.OnUiThread(() => h.Services.RemoteClients.Start(h.Shell));
        await TabTestHarness.Eventually(() => on.All(clients.ContainsKey) && (on.Length == 0 || h.Services.RemoteClients.IsActive), "the clients to start");
        return new Indicator { H = h, Clients = clients, View = view };
    }

    [Fact]
    public async Task It_shows_only_while_a_client_is_turned_on()
    {
        await using var indicator = await StartAsync();
        Assert.False(indicator.Read(v => v.IsVisible));

        indicator.H.OnUiThread(() =>
        {
            indicator.H.Services.Settings.RemoteClients.SetEnabled("telegram", true);
            indicator.H.Services.SaveSettings();
        });

        await TabTestHarness.Eventually(() => indicator.View.IsVisible, "the icon");
    }

    [Fact]
    public async Task At_the_desk_messages_wait_and_away_they_go_to_the_phone()
    {
        await using var indicator = await StartAsync("telegram");
        await indicator.PairAsync("telegram");
        indicator.SetAppActive(true);

        Assert.Equal(RemoteIndicatorLevel.Held, indicator.Read(v => v.Level));
        Assert.Equal("At the desk: messages wait until you're away.", indicator.Read(v => v.SharedLine));
        Assert.Equal("Remote clients: messages wait until you're away", indicator.Read(v => v.AccessibleName));

        indicator.SetAppActive(false);

        Assert.Equal(RemoteIndicatorLevel.Away, indicator.Read(v => v.Level));
        Assert.Equal("Telegram: connected\nYou're away, so messages go to your phone.\nClick for its menu.", indicator.Read(v => v.Tooltip));
    }

    [Fact]
    public async Task With_Always_the_desk_says_messages_come_silently()
    {
        await using var indicator = await StartAsync("telegram");
        indicator.H.OnUiThread(() =>
        {
            indicator.H.Services.Settings.RemoteClients.When = RemoteSendWhen.Always;
            indicator.H.Services.SaveSettings();
        });
        indicator.SetAppActive(true);

        Assert.Equal("At the desk: messages come silently.", indicator.Read(v => v.SharedLine));
    }

    [Fact]
    public async Task Connected_with_no_phone_paired_needs_the_user()
    {
        await using var indicator = await StartAsync("telegram");

        Assert.Equal(RemoteIndicatorLevel.Trouble, indicator.Read(v => v.Level));
        Assert.Equal("Telegram: connected, but no phone is paired", indicator.Read(v => v.Clients.Single().Line));
    }

    [Fact]
    public async Task A_client_that_stopped_needs_the_user_and_one_connecting_dims_it()
    {
        await using var indicator = await StartAsync("telegram");
        await indicator.PairAsync("telegram");

        indicator.Telegram.Become(new RemoteClientState(RemoteClientStatus.Connecting, "Couldn't reach Telegram."));
        await TabTestHarness.Eventually(() => indicator.View.Level == RemoteIndicatorLevel.Connecting, "connecting");
        Assert.Equal("Telegram: connecting. Couldn't reach Telegram.", indicator.Read(v => v.Clients.Single().Line));

        indicator.Telegram.Become(new RemoteClientState(RemoteClientStatus.Failed, TelegramClient.TokenRefused));
        await TabTestHarness.Eventually(() => indicator.View.Level == RemoteIndicatorLevel.Trouble, "the failure");
        Assert.Equal($"Telegram: stopped. {TelegramClient.TokenRefused}", indicator.Read(v => v.Clients.Single().Line));
        Assert.Equal("Remote clients: one needs you", indicator.Read(v => v.AccessibleName));
    }

    [Fact]
    public async Task With_two_clients_the_one_that_needs_the_user_wins_and_each_has_its_line()
    {
        await using var indicator = await StartAsync("telegram", "discord");
        await indicator.PairAsync("telegram");
        await indicator.PairAsync("discord");
        indicator.H.OnUiThread(() => indicator.H.Services.State.RemoteClients.Identities["telegram"] = "deventry_bot");

        indicator.Discord.Become(new RemoteClientState(RemoteClientStatus.Failed, "Discord didn't accept the bot's token."));

        await TabTestHarness.Eventually(() => indicator.View.Level == RemoteIndicatorLevel.Trouble, "Discord's failure");
        var clients = indicator.Read(v => v.Clients);
        Assert.Equal(["Telegram (@deventry_bot): connected", "Discord: stopped. Discord didn't accept the bot's token."], clients.Select(c => c.Line));
        Assert.Equal(["Telegram#telegram", "Discord#discord"], clients.Select(c => c.SettingsTarget));
        Assert.Equal(["IconSettingsTelegram", "IconSettingsDiscord"], clients.Select(c => c.IconKey));
        Assert.Equal("Telegram settings…", clients[0].SettingsText);
    }

    [Fact]
    public async Task Quiet_for_an_hour_holds_every_client_until_it_ends_or_messages_resume()
    {
        await using var indicator = await StartAsync("telegram");
        await indicator.PairAsync("telegram");
        indicator.SetAppActive(false);
        Assert.Equal(RemoteIndicatorLevel.Away, indicator.Read(v => v.Level));

        indicator.H.OnUiThread(() => indicator.View.QuietForAnHourCommand.Execute(null));

        Assert.True(indicator.Read(v => v.IsQuiet));
        Assert.Equal(RemoteIndicatorLevel.Held, indicator.Read(v => v.Level));
        Assert.Equal("Messages are quiet until 13:00", indicator.Read(v => v.QuietText));
        Assert.StartsWith("Quiet until 13:00: messages wait", indicator.Read(v => v.SharedLine));
        // The desk every client reads: Telegram's /quiet is the same switch.
        Assert.Equal(indicator.H.Time.GetUtcNow() + RemoteClientsIndicatorViewModel.QuietFor, ((IRemoteDesk)indicator.H.Services.RemoteClients).QuietUntil);

        indicator.H.Time.Advance(RemoteClientsIndicatorViewModel.QuietFor);

        await TabTestHarness.Eventually(() => !indicator.View.IsQuiet && indicator.View.Level == RemoteIndicatorLevel.Away, "the quiet to end");

        indicator.H.OnUiThread(() => indicator.View.QuietForAnHourCommand.Execute(null));
        indicator.H.OnUiThread(() => indicator.View.ResumeCommand.Execute(null));
        Assert.False(indicator.Read(v => v.IsQuiet));
        Assert.Null(indicator.H.Services.RemoteClients.QuietUntil);
    }

    [Fact]
    public async Task A_message_lights_the_arcs_outward_once_and_says_when_it_went()
    {
        await using var indicator = await StartAsync("telegram");
        await indicator.PairAsync("telegram");

        indicator.Telegram.Act(RemoteActivity.Sent);

        Assert.Equal((true, true, false), indicator.Read(v => (v.IsFlicking, v.InnerLit, v.OuterLit)));
        Assert.Contains("Telegram: connected, last sent 12:00", indicator.Read(v => v.Tooltip));
        indicator.H.Time.Advance(RemoteClientsIndicatorViewModel.FlickStep);
        Assert.Equal((true, true), indicator.Read(v => (v.InnerLit, v.OuterLit)));
        indicator.H.Time.Advance(RemoteClientsIndicatorViewModel.FlickStep);
        Assert.Equal((false, true), indicator.Read(v => (v.InnerLit, v.OuterLit)));
        indicator.H.Time.Advance(RemoteClientsIndicatorViewModel.FlickStep);
        Assert.Equal((false, false, false), indicator.Read(v => (v.IsFlicking, v.InnerLit, v.OuterLit)));
    }

    [Fact]
    public async Task A_message_received_lights_them_too_but_not_its_time()
    {
        await using var indicator = await StartAsync("telegram");
        await indicator.PairAsync("telegram");

        indicator.Telegram.Act(RemoteActivity.Received);

        Assert.True(indicator.Read(v => v.IsFlicking));
        Assert.DoesNotContain("last sent", indicator.Read(v => v.Tooltip));
    }

    [Fact]
    public async Task With_motion_reduced_the_arcs_only_take_the_accent_for_a_moment()
    {
        await using var indicator = await StartAsync("telegram");
        await indicator.PairAsync("telegram");
        indicator.H.OnUiThread(() =>
        {
            indicator.H.Services.Settings.Appearance.Motion = MotionSetting.Reduce;
            indicator.H.Services.SaveSettings();
        });
        Assert.True(indicator.H.Services.ReduceMotion);

        indicator.Telegram.Act(RemoteActivity.Sent);

        Assert.Equal((true, true, true), indicator.Read(v => (v.IsFlicking, v.InnerLit, v.OuterLit)));
        indicator.H.Time.Advance(RemoteClientsIndicatorViewModel.FlickStep);
        Assert.Equal((true, true), indicator.Read(v => (v.InnerLit, v.OuterLit)));
        indicator.H.Time.Advance(RemoteClientsIndicatorViewModel.StillFlick - RemoteClientsIndicatorViewModel.FlickStep);
        Assert.Equal((false, false, false), indicator.Read(v => (v.IsFlicking, v.InnerLit, v.OuterLit)));
    }
}
