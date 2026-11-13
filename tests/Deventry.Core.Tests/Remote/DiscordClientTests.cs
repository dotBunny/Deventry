using System.Net;
using Deventry.Core.Remote;
using Deventry.Core.Remote.Chat;
using Deventry.Core.Remote.Discord;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Remote;

/// <summary>
/// The Discord bot (DESIGN.md §18, "Remote clients", "Discord"), against a fake Discord and a fake desk: starting,
/// pairing from the bot's DM and from any chat, who's answered, the 3-second answers, commands, talking to tabs,
/// buttons, prompts, and what makes it fail.
/// </summary>
public sealed class DiscordClientTests : IAsyncLifetime
{
    private const long Me = 11;
    private const long Stranger = 99;
    private const string Code = "7KQ2M9XD";

    private readonly FakeDiscord _discord = new();
    private readonly FakeDiscordGateway _gateway = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero));
    private readonly FakeRemoteDesk _desk = new();
    private readonly HttpClient _http;
    private readonly RemotePairing _pairing;
    private DiscordClient _client;

    public DiscordClientTests()
    {
        _http = new HttpClient(_discord);
        _pairing = new RemotePairing(DiscordClient.ClientKind, _clock, [new RemoteAccount { Client = DiscordClient.ClientKind, UserId = Me, Name = "Me" }], () => Code);
        _client = NewClient();
        _desk.Tabs = [FakeRemoteDesk.Tab("t1", "Grappling hook"), FakeRemoteDesk.Tab("t2", "Inventory")];
    }

    private DiscordClient NewClient(bool announceBack = false) =>
        new(new DiscordApi(_http, _clock, FakeDiscord.Token, "Deventry/test", "https://discord.test/api/v10"), _gateway.Create, FakeDiscord.Token, _pairing, _clock,
            NullLogger.Instance, new ChatBotOptions { MessageSpacing = TimeSpan.Zero })
        { AnnounceBack = announceBack };

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _http.Dispose();
        _discord.Dispose();
    }

    private static long MyDm => FakeDiscord.DmOf(Me);

    private async Task StartAsync()
    {
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => _client.State == RemoteClientState.Connected, "connected");
    }

    /// <summary>Waits until the bot has taken everything Discord sent so far, and done its work.</summary>
    private async Task SettleAsync()
    {
        _gateway.Message(Stranger, "(a stranger's message, to mark the place)");
        await Waiting.UntilAsync(() => _gateway.Current.Pending == 0, "the Gateway read");
        await _client.WhenIdleAsync().WaitAsync(Waiting.Timeout, TestContext.Current.CancellationToken);
    }

    private async Task<FakeDiscord.Call> NextSentAsync(int count)
    {
        await Waiting.UntilAsync(() => _discord.Sent.Count >= count, $"{count} messages sent");
        return _discord.Sent[count - 1];
    }

    private static string Text(FakeDiscord.Call sent) => FakeDiscord.Content(sent);

    private static bool Silent(FakeDiscord.Call sent) => ((int)sent.Body["flags"]! & (1 << 12)) != 0;

    // ---- Starting ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Starting_names_the_bot_with_its_apps_id_sets_its_commands_and_connects()
    {
        string? identified = null;
        _client.Identified += name => identified = name;

        await StartAsync();

        Assert.Equal($"{FakeDiscord.ApplicationId}:deventry", identified);
        Assert.Equal((FakeDiscord.ApplicationId, "deventry"), DiscordClient.ParseIdentity(identified!));
        Assert.Single(_discord.Of("PUT", "/applications/"));
        Assert.Single(_gateway.Op(2));
    }

    [Fact]
    public async Task A_refused_token_fails_with_why()
    {
        _discord.GetMeAnswer = () => FakeDiscord.Error(HttpStatusCode.Unauthorized, 0, "401: Unauthorized");

        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Failed, "the failure");
        Assert.Equal(DiscordClient.TokenRefused, _client.State.Reason);
        Assert.Empty(_gateway.Sockets);
    }

    [Fact]
    public async Task An_app_that_cannot_be_added_to_an_account_fails_with_what_to_do()
    {
        _discord.InstallTypes = ["0"];

        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Failed, "the failure");
        Assert.Equal(DiscordClient.NoUserInstall, _client.State.Reason);
    }

    [Fact]
    public async Task An_app_whose_interactions_go_to_a_web_address_fails_with_what_to_do()
    {
        _discord.InteractionsEndpoint = "https://example.com/interactions";

        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Failed, "the failure");
        Assert.Equal(DiscordClient.HasEndpoint, _client.State.Reason);
    }

    [Fact]
    public async Task Discord_closing_the_Gateway_over_the_token_fails_with_why()
    {
        await StartAsync();

        _gateway.Current.CloseFromDiscord(4004, "Authentication failed.");

        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Failed, "the failure");
        Assert.Equal(DiscordClient.TokenRefused, _client.State.Reason);
    }

    // ---- Pairing and who's answered ---------------------------------------------------------------------------------

    [Fact]
    public async Task Pair_in_the_bots_DM_with_the_right_code_pairs_and_welcomes()
    {
        _pairing.Start();
        await StartAsync();

        _gateway.Command(12, "pair", Code);

        await Waiting.UntilAsync(() => _discord.Of("PATCH", "/webhooks/").Count == 1, "the welcome");
        var answer = Assert.Single(_discord.Callbacks);
        Assert.Equal(DiscordApi.DeferredChannelMessage, (int)answer.Body["type"]!);
        Assert.Null(answer.Body["data"]);
        Assert.StartsWith("Paired with Deventry on DESK.", Text(_discord.Of("PATCH", "/webhooks/")[0]));
        Assert.True(_pairing.IsPaired(12));
    }

    [Fact]
    public async Task A_stranger_gets_no_answer_at_all()
    {
        _pairing.Start();
        await StartAsync();

        _gateway.Command(Stranger, "pair", "WRONGCOD");
        _gateway.Command(Stranger, "tabs");
        _gateway.Message(Stranger, "hello?");
        await SettleAsync();

        Assert.Empty(_discord.Callbacks);
        Assert.Empty(_discord.Sent);
        Assert.False(_pairing.IsPaired(Stranger));
    }

    [Fact]
    public async Task Pair_from_another_chat_answers_only_there_and_welcomes_in_the_DM_it_opens()
    {
        _pairing.Start();
        await StartAsync();

        _gateway.Command(12, "pair", Code, context: 0);

        await Waiting.UntilAsync(() => _discord.Of("PATCH", "/webhooks/").Count == 1, "the answer");
        Assert.Equal(1 << 6, (int)_discord.Callbacks.Single().Body["data"]!["flags"]!);
        Assert.Equal("12", (string)_discord.Of("POST", "/users/@me/channels").Single().Body["recipient_id"]!);
        var welcome = Assert.Single(_discord.Sent);
        Assert.Equal(FakeDiscord.DmOf(12), FakeDiscord.ChannelOf(welcome));
        Assert.StartsWith("Paired with Deventry on DESK.", Text(welcome));
        Assert.EndsWith("I've written to you in our DM.", Text(_discord.Of("PATCH", "/webhooks/")[0]));
    }

    [Fact]
    public async Task Pair_from_another_chat_says_so_when_Discord_will_not_let_the_bot_write_first()
    {
        _discord.RefuseDms.Add(12);
        _pairing.Start();
        await StartAsync();

        _gateway.Command(12, "pair", Code, context: 0);

        await Waiting.UntilAsync(() => _discord.Of("PATCH", "/webhooks/").Count == 1, "the answer");
        Assert.Contains("Discord doesn't let me write to you first", Text(_discord.Of("PATCH", "/webhooks/")[0]));
        Assert.True(_pairing.IsPaired(12));
        Assert.Empty(_discord.Sent);
    }

    [Fact]
    public async Task A_server_message_is_ignored_even_from_the_paired_account()
    {
        await StartAsync();

        _gateway.Message(Me, "/tabs", guild: 777);
        await SettleAsync();

        Assert.Empty(_discord.Sent);
    }

    // ---- Commands ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_command_is_answered_in_messages_of_their_own_and_the_thinking_goes()
    {
        _desk.Tabs[0] = _desk.Tabs[0] with { Status = RemoteTabStatus.Working, Doing = "Reading Player.cs", Since = _clock.GetUtcNow() - TimeSpan.FromSeconds(80) };
        await StartAsync();

        _gateway.Command(Me, "tabs");

        await Waiting.UntilAsync(() => _discord.Of("DELETE", "/webhooks/").Count == 1, "the thinking taken away");
        Assert.Equal(DiscordApi.DeferredChannelMessage, (int)_discord.Callbacks.Single().Body["type"]!);
        var sent = Assert.Single(_discord.Sent);
        Assert.Equal(MyDm, FakeDiscord.ChannelOf(sent));
        Assert.Equal("**Tabs on DESK**\n\n**Game**\n● Grappling hook: Working · Reading Player.cs · 1m 20s\n○ Inventory: Idle", Text(sent));
        Assert.Equal(["● Grappling hook", "○ Inventory"], FakeDiscord.Buttons(sent).Select(b => b.Label));
        var calls = _discord.Calls().ToList();
        Assert.True(calls.FindIndex(c => c.Method == "DELETE") > calls.IndexOf(sent));
    }

    [Fact]
    public async Task A_commands_argument_comes_with_it()
    {
        await StartAsync();

        _gateway.Command(Me, "to", "inventory");
        await NextSentAsync(1);
        _gateway.Message(Me, "Add a slot");
        await NextSentAsync(2);

        Assert.Contains("send t2 Add a slot (Discord)", _desk.CallsCopy());
    }

    [Fact]
    public async Task Quiet_holds_messages_for_the_minutes_given()
    {
        await StartAsync();

        _gateway.Command(Me, "quiet", "30");

        Assert.StartsWith("Quiet until 14:30", Text(await NextSentAsync(1)));
        Assert.Equal(_clock.GetUtcNow() + TimeSpan.FromMinutes(30), _desk.QuietUntil);
    }

    // ---- Talking to a tab -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_plain_message_asks_which_tab_and_a_tap_sends_it()
    {
        await StartAsync();

        _gateway.Message(Me, "Fix the rope physics");

        var question = await NextSentAsync(1);
        Assert.Equal("Which tab is this for?", Text(question));
        _gateway.Tap(Me, FakeDiscord.Data(question, "Inventory"), FakeDiscord.IdOf(question));

        var sent = await NextSentAsync(2);
        Assert.Contains("send t2 Fix the rope physics (Discord)", _desk.CallsCopy());
        Assert.StartsWith("Sent to **Inventory**.", Text(sent));
        Assert.True(Silent(sent));
        Assert.Equal(DiscordApi.DeferredUpdate, (int)_discord.Callbacks.Single().Body["type"]!);
        // The question loses its buttons.
        var edit = _discord.Of("PATCH", $"/channels/{MyDm}/messages/{FakeDiscord.IdOf(question)}").Single();
        Assert.Empty(edit.Body["components"]!.AsArray());
    }

    [Fact]
    public async Task A_reply_to_a_tabs_message_goes_to_that_tab()
    {
        await StartAsync();
        _gateway.Command(Me, "to", "Grappling");
        await NextSentAsync(1);
        _desk.Raise(new RemoteTabFailed(_desk.Tabs[1], "Claude Code stopped unexpectedly (exit code 3).") { Notice = RemoteNotice.Sound });
        var failed = await NextSentAsync(2);

        _gateway.Message(Me, "Start over", replyTo: FakeDiscord.IdOf(failed));

        await Waiting.UntilAsync(() => _desk.CallsCopy().Contains("send t2 Start over (Discord)"), "the message to Inventory");
    }

    [Fact]
    public async Task A_file_is_only_text_for_now()
    {
        await StartAsync();

        _gateway.Message(Me, "", attachment: true);

        Assert.Equal("Only text for now.", Text(await NextSentAsync(1)));
    }

    // ---- Prompts ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_waiting_prompt_opens_the_DM_and_Allow_answers_it_with_a_note()
    {
        await StartAsync();
        var prompt = FakeRemoteDesk.Command();

        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0] with { Status = RemoteTabStatus.NeedsInput, Prompt = prompt }, prompt) { Notice = RemoteNotice.Sound });

        var sent = await NextSentAsync(1);
        Assert.Equal($"{Me}", (string)_discord.Of("POST", "/users/@me/channels").Single().Body["recipient_id"]!);
        Assert.Equal(MyDm, FakeDiscord.ChannelOf(sent));
        Assert.Contains("**Allow this command?**", Text(sent));
        Assert.Contains("```bash\nnpm test\n```", Text(sent));
        Assert.False(Silent(sent));
        Assert.Equal(["Allow", "Deny", "Deny with a message…"], FakeDiscord.Buttons(sent).Select(b => b.Label));

        var token = _gateway.Tap(Me, FakeDiscord.Data(sent, "Allow"), FakeDiscord.IdOf(sent));

        await Waiting.UntilAsync(() => _desk.CallsCopy().Contains("answer t1 p1 allow"), "the answer");
        await Waiting.UntilAsync(() => _discord.Of("POST", "/webhooks/").Count == 1, "the note");
        var note = _discord.Of("POST", "/webhooks/").Single();
        Assert.Equal($"/webhooks/{FakeDiscord.ApplicationId}/{token}", note.Route);
        Assert.Equal("Allow", Text(note));
        Assert.NotEqual(0, (int)note.Body["flags"]! & (1 << 6));
    }

    [Fact]
    public async Task A_closed_prompt_loses_its_buttons_and_says_how()
    {
        await StartAsync();
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], FakeRemoteDesk.Command()) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);

        _desk.Raise(new RemotePromptClosed("t1", "p1", RemotePromptEnd.AnsweredFromClient, "Allowed", "Discord", _clock.GetUtcNow()));

        var path = $"/channels/{MyDm}/messages/{FakeDiscord.IdOf(sent)}";
        await Waiting.UntilAsync(() => _discord.Of("PATCH", path).Count == 1, "the edit");
        var edit = _discord.Of("PATCH", path).Single();
        Assert.EndsWith("*Allowed from Discord at 14:00*", Text(edit));
        Assert.Empty(edit.Body["components"]!.AsArray());
    }

    [Fact]
    public async Task A_button_from_before_a_restart_is_taken_and_stale_and_loses_its_buttons()
    {
        await StartAsync();

        _gateway.Tap(Me, "zzzz:1", 4321);

        await Waiting.UntilAsync(() => _discord.Of("PATCH", $"/channels/{MyDm}/messages/4321").Count == 1, "its buttons taken away");
        Assert.Equal(DiscordApi.DeferredUpdate, (int)_discord.Callbacks.Single().Body["type"]!);
        Assert.Equal("That button is from before Deventry restarted.", Text(_discord.Of("POST", "/webhooks/").Single()));
        Assert.Empty(_desk.CallsCopy());
    }

    [Fact]
    public async Task A_strangers_tap_is_taken_and_does_nothing_and_says_nothing()
    {
        await StartAsync();
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], FakeRemoteDesk.Command()) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);

        _gateway.Tap(Stranger, FakeDiscord.Data(sent, "Allow"), FakeDiscord.IdOf(sent));
        await SettleAsync();

        Assert.Single(_discord.Callbacks);
        Assert.Empty(_discord.Of("POST", "/webhooks/"));
        Assert.Empty(_desk.CallsCopy());
    }

    [Fact]
    public async Task Someone_else_answering_first_says_another_program_uses_the_bot()
    {
        await StartAsync();
        _discord.Next.Add(("POST", "/interactions/", () => FakeDiscord.Error(HttpStatusCode.BadRequest, 40060, "Interaction has already been acknowledged.")));

        _gateway.Tap(Me, "zzzz:1", 4321);

        await Waiting.UntilAsync(() => _client.State.Reason == DiscordClient.AnotherProgram, "the warning");
        Assert.Equal(RemoteClientStatus.Connected, _client.State.Status);
    }

    // ---- Turns and closing ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_finished_turn_shows_the_replys_start_in_Discords_Markdown()
    {
        await StartAsync();

        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], TimeSpan.FromSeconds(130), "3 of 5 tasks done", "The rope **swings** now.\nSee `Hook.cs`.",
            [new RemoteFileChange("Assets/Hook.cs", 12, 3), new RemoteFileChange("Assets/Rope.cs", 4, 0, IsNew: true)]) { Notice = RemoteNotice.Sound });

        var sent = await NextSentAsync(1);
        Assert.Equal(
            "**Grappling hook** · Game (main)\nFinished in 2m · 3 of 5 tasks done\n\nThe rope **swings** now.\nSee `Hook.cs`.\n\nChanged Hook.cs +12 −3, Rope.cs (new, +4)",
            Text(sent));
        Assert.Equal(["Whole reply", "Changed files"], FakeDiscord.Buttons(sent).Select(b => b.Label));
    }

    [Fact]
    public async Task A_reply_longer_than_a_message_comes_as_a_Markdown_file()
    {
        _desk.LastReply = new RemoteReply("t1", "Grappling hook", string.Join("\n", Enumerable.Repeat("A long line of the reply.", 200)), _clock.GetUtcNow());
        await StartAsync();
        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], null, null, "Short start", []) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);

        _gateway.Tap(Me, FakeDiscord.Data(sent, "Whole reply"), FakeDiscord.IdOf(sent));

        var file = await NextSentAsync(2);
        Assert.Equal("Grappling hook reply.md", (string)file.Body["file"]!);
        Assert.Equal(_desk.LastReply.Markdown, (string)file.Body["file_content"]!);
    }

    [Fact]
    public async Task Closing_says_so_and_the_next_start_says_it_is_back_silently()
    {
        await StartAsync();

        _desk.Raise(new RemoteDeskClosing { Notice = RemoteNotice.Sound });
        await _client.StopAsync();

        Assert.Equal("Deventry on DESK is closing. Its tabs are kept, and I'll answer again when it's back.", Text(Assert.Single(_discord.Sent)));
        Assert.Equal(RemoteClientStatus.Off, _client.State.Status);

        _client = NewClient(announceBack: true);
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);
        var back = await NextSentAsync(2);
        Assert.Equal("Deventry on DESK is back.", Text(back));
        Assert.True(Silent(back));
    }

    [Fact]
    public async Task Messages_sent_and_received_are_told_as_activity_but_not_strangers()
    {
        var activity = new List<RemoteActivity>();
        _client.Activity += a =>
        {
            lock (activity)
            {
                activity.Add(a);
            }
        };
        await StartAsync();

        _gateway.Message(Stranger, "hello?");
        _gateway.Command(Me, "tabs");
        await NextSentAsync(1);
        await SettleAsync();

        lock (activity)
        {
            Assert.Equal([RemoteActivity.Received, RemoteActivity.Sent], activity);
        }
    }
}
