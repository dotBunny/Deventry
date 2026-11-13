using System.Net;
using System.Text.Json.Nodes;
using Deventry.Core.Remote;
using Deventry.Core.Remote.Telegram;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Remote;

/// <summary>
/// The Telegram bot (DESIGN.md §18, "Remote clients", "Telegram"), against a fake Bot API and a fake desk: pairing,
/// who's answered, talking to tabs, the buttons, what the desk's events send, and polling's failures.
/// </summary>
public sealed class TelegramClientTests : IAsyncLifetime
{
    private const long Me = 11;
    private const long Stranger = 99;

    private readonly FakeTelegram _telegram = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero));
    private readonly FakeRemoteDesk _desk = new();
    private readonly HttpClient _http;
    private readonly RemotePairing _pairing;
    private TelegramClient _client;

    public TelegramClientTests()
    {
        _http = new HttpClient(_telegram);
        _pairing = new RemotePairing(TelegramClient.ClientKind, _clock, [new RemoteAccount { Client = TelegramClient.ClientKind, UserId = Me, Name = "Me" }], () => "7KQ2M9XD");
        _client = NewClient();
        _desk.Tabs = [FakeRemoteDesk.Tab("t1", "Grappling hook"), FakeRemoteDesk.Tab("t2", "Inventory")];
    }

    private TelegramClient NewClient(bool announceBack = false) =>
        new(new TelegramBotApi(_http, _clock, FakeTelegram.Token, "Deventry/test"), _pairing, _clock, NullLogger.Instance,
            new TelegramClientOptions { MessageSpacing = TimeSpan.Zero, PollSeconds = 0 })
        { AnnounceBack = announceBack };

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _http.Dispose();
        _telegram.Dispose();
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    private async Task StartAsync()
    {
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Connecting && _telegram.Polls >= 1, "the first poll");
    }

    /// <summary>Waits until the bot has handled everything sent so far: the poll after them has started, and its work is done.</summary>
    private async Task SettleAsync()
    {
        var polls = _telegram.Polls;
        _telegram.Message(Stranger, "(a stranger's message, to mark the place)", Now);
        await Waiting.UntilAsync(() => _telegram.Polls > polls, "the next poll");
        await _client.WhenIdleAsync().WaitAsync(Waiting.Timeout, TestContext.Current.CancellationToken);
    }

    private async Task<JsonObject> NextSentAsync(int count)
    {
        await Waiting.UntilAsync(() => _telegram.Sent.Count >= count, $"{count} messages sent");
        return _telegram.Sent[count - 1];
    }

    private static string Text(JsonObject sent) => (string)sent["text"]!;

    // ---- Pairing and who's answered -----------------------------------------------------------------------------

    [Fact]
    public async Task The_right_code_pairs_a_new_account_and_the_bot_says_where()
    {
        _pairing.Start();
        await StartAsync();

        _telegram.Message(12, "/start 7KQ2M9XD", Now);

        var sent = await NextSentAsync(1);
        Assert.Equal(12, (long)sent["chat_id"]!);
        Assert.StartsWith("Paired with Deventry on DESK.", Text(sent));
        Assert.True(_pairing.IsPaired(12));
    }

    [Fact]
    public async Task A_stranger_gets_no_answer_at_all()
    {
        _pairing.Start();
        await StartAsync();

        _telegram.Message(Stranger, "/start WRONGCOD", Now);
        _telegram.Message(Stranger, "hello?", Now);
        _telegram.Message(Stranger, "/tabs", Now);
        await SettleAsync();

        Assert.Empty(_telegram.Sent);
        Assert.False(_pairing.IsPaired(Stranger));
    }

    [Fact]
    public async Task A_group_chat_is_ignored_even_from_the_paired_account()
    {
        await StartAsync();

        _telegram.Message(Me, "/tabs", Now, chatType: "group", chat: -500);
        await SettleAsync();

        Assert.Empty(_telegram.Sent);
    }

    [Fact]
    public async Task Messages_from_before_Deventry_started_are_passed_over_and_counted_once()
    {
        // Kept by Telegram while Deventry was closed: they arrive in the first poll.
        _telegram.Message(Me, "/stop", Now - TimeSpan.FromHours(3));
        _telegram.Message(Me, "anything", Now - TimeSpan.FromMinutes(10));
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _telegram.Sent.Count >= 1, "the note about them");
        await SettleAsync();

        var note = Assert.Single(_telegram.Sent);
        Assert.Contains("Passed over 1 message", Text(note));
        Assert.DoesNotContain(_desk.CallsCopy(), c => c.StartsWith("stop", StringComparison.Ordinal));
    }

    // ---- Talking to a tab -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_plain_message_with_no_tab_picked_asks_which_and_goes_once_one_is_picked()
    {
        await StartAsync();

        _telegram.Message(Me, "Fix the rope physics", Now);

        var question = await NextSentAsync(1);
        Assert.Equal("Which tab is this for?", Text(question));
        _telegram.Tap(Me, FakeTelegram.Data(question, "Inventory"), 5000);

        var sent = await NextSentAsync(2);
        Assert.Contains("send t2 Fix the rope physics (Telegram)", _desk.CallsCopy());
        Assert.StartsWith("Sent to <b>Inventory</b>.", Text(sent));
        Assert.True((bool)sent["disable_notification"]!);
    }

    [Fact]
    public async Task A_reply_to_a_tabs_message_goes_to_that_tab()
    {
        await StartAsync();
        _telegram.Message(Me, "/to Grappling", Now);
        Assert.Contains("<b>Grappling hook</b>", Text(await NextSentAsync(1)));
        _desk.Raise(new RemoteTabFailed(_desk.Tabs[1], "Claude Code stopped unexpectedly (exit code 3).") { Notice = RemoteNotice.Sound });
        var failed = await NextSentAsync(2);

        // Replied to while /to points at another tab.
        _telegram.Message(Me, "Start over", Now, replyTo: FakeTelegram.IdOf(failed));

        await Waiting.UntilAsync(() => _desk.CallsCopy().Contains("send t2 Start over (Telegram)"), "the message to Inventory");
    }

    [Fact]
    public async Task After_to_plain_messages_go_to_the_picked_tab()
    {
        await StartAsync();

        _telegram.Message(Me, "/to inventory", Now);
        await NextSentAsync(1);
        _telegram.Message(Me, "Add a slot", Now);
        await NextSentAsync(2);

        Assert.Contains("send t2 Add a slot (Telegram)", _desk.CallsCopy());
    }

    [Fact]
    public async Task A_message_the_desk_refuses_gets_why()
    {
        _desk.Result = RemoteResult.No(RemoteAccess.Refusal(RemoteAction.Send));
        await StartAsync();

        _telegram.Message(Me, "/to inventory", Now);
        await NextSentAsync(1);
        _telegram.Message(Me, "Add a slot", Now);

        Assert.Equal(TelegramFormat.Escape(RemoteAccess.Refusal(RemoteAction.Send)), Text(await NextSentAsync(2)));
    }

    [Fact]
    public async Task A_photo_is_only_text_for_now()
    {
        await StartAsync();

        _telegram.Message(Me, "", Now, photo: true);

        Assert.Equal("Only text for now.", Text(await NextSentAsync(1)));
    }

    // ---- Prompts ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_waiting_prompt_comes_with_the_cards_buttons_and_Allow_answers_it()
    {
        await StartAsync();
        var prompt = FakeRemoteDesk.Command();

        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0] with { Status = RemoteTabStatus.NeedsInput, Prompt = prompt }, prompt) { Notice = RemoteNotice.Sound });

        var sent = await NextSentAsync(1);
        Assert.Contains("<b>Allow this command?</b>", Text(sent));
        Assert.Contains("<pre><code class=\"language-bash\">npm test</code></pre>", Text(sent));
        Assert.Null(sent["disable_notification"]);
        Assert.Equal(["Allow", "Deny", "Deny with a message…"], FakeTelegram.Buttons(sent).Select(b => b.Text));

        _telegram.Tap(Me, FakeTelegram.Data(sent, "Allow"), 5000);
        await Waiting.UntilAsync(() => _desk.CallsCopy().Contains("answer t1 p1 allow"), "the answer");
    }

    [Fact]
    public async Task A_closed_prompt_loses_its_buttons_and_says_how()
    {
        await StartAsync();
        var prompt = FakeRemoteDesk.Command();
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], prompt) { Notice = RemoteNotice.Sound });
        await NextSentAsync(1);

        _desk.Raise(new RemotePromptClosed("t1", "p1", RemotePromptEnd.AnsweredFromClient, "Allowed", "Telegram", Now));

        await Waiting.UntilAsync(() => _telegram.Of("editMessageText").Count == 1, "the edit");
        var edit = _telegram.Of("editMessageText").Single();
        Assert.EndsWith("<i>Allowed from Telegram at 14:00</i>", (string)edit["text"]!);
        Assert.Null(edit["reply_markup"]);
    }

    [Fact]
    public async Task Deny_with_a_message_asks_for_the_words_and_the_next_message_is_them()
    {
        await StartAsync();
        var prompt = FakeRemoteDesk.Command();
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], prompt) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);

        _telegram.Tap(Me, FakeTelegram.Data(sent, "Deny with a message…"), 5000);
        var ask = await NextSentAsync(2);
        Assert.StartsWith("What should Claude do instead?", Text(ask));
        _telegram.Message(Me, "Use the staging database", Now);

        await Waiting.UntilAsync(() => _desk.CallsCopy().Contains("answer t1 p1 deny-message: Use the staging database"), "the answer with words");
    }

    [Fact]
    public async Task A_prompt_the_user_does_not_want_messages_for_is_not_sent()
    {
        await StartAsync();

        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], FakeRemoteDesk.Command()) { Notice = RemoteNotice.None });
        await SettleAsync();

        Assert.Empty(_telegram.Sent);
    }

    [Fact]
    public async Task At_the_desk_with_Always_a_prompt_comes_silently_and_not_again_on_going_away()
    {
        await StartAsync();
        var prompt = FakeRemoteDesk.Command();

        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], prompt) { Notice = RemoteNotice.Silent });
        var sent = await NextSentAsync(1);
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], prompt) { Notice = RemoteNotice.Sound, Again = true });
        await SettleAsync();

        Assert.True((bool)sent["disable_notification"]!);
        Assert.Single(_telegram.Sent);
    }

    [Fact]
    public async Task Just_what_happened_names_the_tab_and_Show_sends_the_prompt_when_asked()
    {
        _desk.Detail = RemoteDetail.JustWhatHappened;
        await StartAsync();
        var prompt = FakeRemoteDesk.Command(command: "rm -rf build");
        _desk.Tabs[0] = _desk.Tabs[0] with { Status = RemoteTabStatus.NeedsInput, Prompt = prompt };

        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], prompt) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);
        Assert.Equal("Grappling hook needs you.", Text(sent));
        Assert.DoesNotContain("rm -rf", sent.ToJsonString());

        _telegram.Tap(Me, FakeTelegram.Data(sent, "Show"), 5000);
        var shown = await NextSentAsync(2);
        Assert.Contains("rm -rf build", Text(shown));
        Assert.Contains("Allow", FakeTelegram.Buttons(shown).Select(b => b.Text));
    }

    [Fact]
    public async Task Watching_only_shows_prompts_without_buttons()
    {
        _desk.Access = RemoteAccessLevel.Watch;
        await StartAsync();

        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], FakeRemoteDesk.Command()) { Notice = RemoteNotice.Sound });

        var sent = await NextSentAsync(1);
        Assert.Empty(FakeTelegram.Buttons(sent));
        Assert.Contains("answer this at the desk", Text(sent));
    }

    [Fact]
    public async Task A_button_from_before_a_restart_is_stale_and_loses_its_buttons()
    {
        await StartAsync();

        _telegram.Tap(Me, "zzzz:1", 4321);

        await Waiting.UntilAsync(() => _telegram.Of("answerCallbackQuery").Count == 1, "the tap's answer");
        Assert.Equal("That button is from before Deventry restarted.", (string)_telegram.Of("answerCallbackQuery").Single()["text"]!);
        await Waiting.UntilAsync(() => _telegram.Of("editMessageReplyMarkup").Count == 1, "its buttons taken away");
        Assert.Equal(4321, (long)_telegram.Of("editMessageReplyMarkup").Single()["message_id"]!);
        Assert.Empty(_desk.CallsCopy());
    }

    [Fact]
    public async Task A_strangers_tap_does_nothing_and_says_nothing()
    {
        await StartAsync();
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], FakeRemoteDesk.Command()) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);

        _telegram.Tap(Stranger, FakeTelegram.Data(sent, "Allow"), 5000);

        await Waiting.UntilAsync(() => _telegram.Of("answerCallbackQuery").Count == 1, "the tap's answer");
        Assert.Null(_telegram.Of("answerCallbackQuery").Single()["text"]);
        Assert.Empty(_desk.CallsCopy());
    }

    // ---- Turns, errors and closing ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_finished_turn_shows_the_replys_start_and_its_files()
    {
        await StartAsync();

        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], TimeSpan.FromSeconds(130), "3 of 5 tasks done", "The rope **swings** now.\nSee `Hook.cs`.",
            [new RemoteFileChange("Assets/Hook.cs", 12, 3), new RemoteFileChange("Assets/Rope.cs", 4, 0, IsNew: true)]) { Notice = RemoteNotice.Sound });

        var sent = await NextSentAsync(1);
        Assert.Equal(
            "<b>Grappling hook</b> · Game (main)\nFinished in 2m · 3 of 5 tasks done\n\nThe rope <b>swings</b> now.\nSee <code>Hook.cs</code>.\n\nChanged Hook.cs +12 −3, Rope.cs (new, +4)",
            Text(sent));
        Assert.Equal(["Whole reply", "Changed files"], FakeTelegram.Buttons(sent).Select(b => b.Text));
    }

    [Fact]
    public async Task A_reply_longer_than_a_message_comes_as_a_Markdown_file()
    {
        _desk.LastReply = new RemoteReply("t1", "Grappling hook", string.Join("\n", Enumerable.Repeat("A long line of the reply.", 400)), Now);
        await StartAsync();
        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], null, null, "Short start", []) { Notice = RemoteNotice.Sound });
        var sent = await NextSentAsync(1);

        _telegram.Tap(Me, FakeTelegram.Data(sent, "Whole reply"), 5000);

        await Waiting.UntilAsync(() => _telegram.Of("sendDocument").Count == 1, "the file");
        var file = _telegram.Of("sendDocument").Single();
        Assert.Equal("Grappling hook reply.md", (string)file["document"]!);
        Assert.Equal(_desk.LastReply.Markdown, (string)file["content"]!);
    }

    [Fact]
    public async Task A_message_sent_from_here_is_answered_when_the_turn_ends_even_at_the_desk()
    {
        await StartAsync();
        _telegram.Message(Me, "/to grappling", Now);
        await NextSentAsync(1);
        _telegram.Message(Me, "Swing faster", Now);
        await NextSentAsync(2);

        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], TimeSpan.FromMinutes(1), null, "Faster now.", []) { Notice = RemoteNotice.None });

        var turn = await NextSentAsync(3);
        Assert.Contains("Faster now.", Text(turn));
        // The live status says it finished.
        await Waiting.UntilAsync(() => _telegram.Of("editMessageText").Any(e => ((string)e["text"]!).EndsWith("Finished in 1m", StringComparison.Ordinal)), "the live status's end");
    }

    [Fact]
    public async Task An_error_offers_to_start_the_tab_again()
    {
        await StartAsync();

        _desk.Raise(new RemoteTabFailed(_desk.Tabs[0] with { Status = RemoteTabStatus.Error, CanRestart = true }, "Claude Code stopped unexpectedly (exit code 3).")
        {
            Notice = RemoteNotice.Sound,
        });
        var sent = await NextSentAsync(1);
        Assert.Contains("exit code 3", Text(sent));

        _telegram.Tap(Me, FakeTelegram.Data(sent, "Start it again"), 5000);
        await Waiting.UntilAsync(() => _desk.CallsCopy().Contains("restart t1"), "the restart");
    }

    [Fact]
    public async Task Closing_says_so_and_the_next_start_says_it_is_back_silently()
    {
        await StartAsync();

        _desk.Raise(new RemoteDeskClosing { Notice = RemoteNotice.Sound });
        await _client.StopAsync();

        Assert.Equal("Deventry on DESK is closing. Its tabs are kept, and I'll answer again when it's back.", Text(Assert.Single(_telegram.Sent)));
        Assert.Equal(RemoteClientStatus.Off, _client.State.Status);

        _client = NewClient(announceBack: true);
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);
        var back = await NextSentAsync(2);
        Assert.Equal("Deventry on DESK is back.", Text(back));
        Assert.True((bool)back["disable_notification"]!);
    }

    // ---- Commands ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tabs_lists_them_by_project_with_a_button_each()
    {
        _desk.Tabs[0] = _desk.Tabs[0] with { Status = RemoteTabStatus.Working, Doing = "Reading Player.cs", Since = Now - TimeSpan.FromSeconds(80) };
        await StartAsync();

        _telegram.Message(Me, "/tabs", Now);

        var sent = await NextSentAsync(1);
        Assert.Equal("<b>Tabs on DESK</b>\n\n<b>Game</b>\n● Grappling hook: Working · Reading Player.cs · 1m 20s\n○ Inventory: Idle", Text(sent));
        Assert.Equal(["● Grappling hook", "○ Inventory"], FakeTelegram.Buttons(sent).Select(b => b.Text));
    }

    [Fact]
    public async Task Stop_stops_the_one_tab_working()
    {
        _desk.Tabs[1] = _desk.Tabs[1] with { Status = RemoteTabStatus.Working };
        await StartAsync();

        _telegram.Message(Me, "/stop", Now);

        Assert.Equal("Stopped <b>Inventory</b>.", Text(await NextSentAsync(1)));
        Assert.Contains("stop t2", _desk.CallsCopy());
    }

    [Fact]
    public async Task Stop_is_refused_below_the_full_level()
    {
        _desk.Access = RemoteAccessLevel.Answer;
        _desk.Tabs[1] = _desk.Tabs[1] with { Status = RemoteTabStatus.Working };
        await StartAsync();

        _telegram.Message(Me, "/stop", Now);

        Assert.Equal(TelegramFormat.Escape(RemoteAccess.Refusal(RemoteAction.Stop)), Text(await NextSentAsync(1)));
        Assert.Empty(_desk.CallsCopy());
    }

    [Fact]
    public async Task Quiet_holds_messages_for_the_minutes_given()
    {
        await StartAsync();
        _telegram.Message(Me, "/quiet 30", Now);
        Assert.StartsWith("Quiet until 14:30", Text(await NextSentAsync(1)));

        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], null, null, "Done.", []) { Notice = RemoteNotice.Sound });
        await SettleAsync();
        Assert.Single(_telegram.Sent);

        _clock.Advance(TimeSpan.FromMinutes(30));
        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], null, null, "Done again.", []) { Notice = RemoteNotice.Sound });
        Assert.Contains("Done again.", Text(await NextSentAsync(2)));
    }

    [Fact]
    public async Task Quiet_is_the_desks_so_it_holds_every_client()
    {
        await StartAsync();

        _telegram.Message(Me, "/quiet 30", Now);
        await NextSentAsync(1);
        Assert.Equal(Now + TimeSpan.FromMinutes(30), _desk.QuietUntil);

        _telegram.Message(Me, "/quiet off", Now);
        Assert.Equal("Messages are back on.", Text(await NextSentAsync(2)));
        Assert.Null(_desk.QuietUntil);

        // Set elsewhere, such as the header's Quiet for an hour, it holds this client too.
        _desk.SetQuiet(Now + TimeSpan.FromHours(1));
        _desk.Raise(new RemoteTurnFinished(_desk.Tabs[0], null, null, "Done.", []) { Notice = RemoteNotice.Sound });
        await SettleAsync();
        Assert.Equal(2, _telegram.Sent.Count);
    }

    [Fact]
    public async Task Messages_sent_and_received_are_told_as_activity_but_not_strangers_or_edits()
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

        _telegram.Message(Stranger, "hello?", Now);
        _telegram.Message(Me, "/tabs", Now);
        await NextSentAsync(1);
        _desk.Raise(new RemotePromptWaiting(_desk.Tabs[0], FakeRemoteDesk.Command()) { Notice = RemoteNotice.Sound });
        await NextSentAsync(2);
        _desk.Raise(new RemotePromptClosed("t1", "p1", RemotePromptEnd.AnsweredAtDesk, "Allowed", null, Now));
        await Waiting.UntilAsync(() => _telegram.Of("editMessageText").Count == 1, "the edit");
        await SettleAsync();

        lock (activity)
        {
            Assert.Equal([RemoteActivity.Received, RemoteActivity.Sent, RemoteActivity.Sent], activity);
        }
    }

    [Fact]
    public async Task New_is_refused_unless_Open_new_tabs_is_on()
    {
        await StartAsync();

        _telegram.Message(Me, "/new", Now);

        Assert.Equal(TelegramFormat.Escape(RemoteAccess.Refusal(RemoteAction.OpenTab)), Text(await NextSentAsync(1)));
    }

    [Fact]
    public async Task New_opens_a_tab_in_a_folder_and_points_messages_at_it()
    {
        _desk.CanOpenTabs = true;
        _desk.Folders = [new RemoteFolder("C:/Demo/Game", "Game", true)];
        await StartAsync();

        _telegram.Message(Me, "/new", Now);
        var folders = await NextSentAsync(1);
        _telegram.Tap(Me, FakeTelegram.Data(folders, "★ Game"), 5000);

        Assert.StartsWith("Opened <b>Grappling hook</b>", Text(await NextSentAsync(2)));
        Assert.Contains("open C:/Demo/Game", _desk.CallsCopy());
    }

    [Theory]
    [InlineData("/tabs", "tabs", null)]
    [InlineData("/to@deventry_bot Grappling hook", "to", "Grappling hook")]
    [InlineData("/QUIET 30", "quiet", "30")]
    [InlineData("hello", null, null)]
    [InlineData("/", null, null)]
    public void Commands_are_read_with_their_argument(string text, string? command, string? argument)
    {
        Assert.Equal((command, argument), TelegramClient.ParseCommand(text));
    }

    // ---- Polling ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_refused_token_fails_with_why()
    {
        _telegram.GetMeAnswer = () => FakeTelegram.Error(HttpStatusCode.Unauthorized, "Unauthorized");

        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Failed, "the failure");
        Assert.Equal(TelegramClient.TokenRefused, _client.State.Reason);
    }

    [Fact]
    public async Task Another_program_polling_the_bot_is_said_and_tried_again_each_minute()
    {
        _telegram.FailNextPoll(HttpStatusCode.Conflict, "Conflict: terminated by other getUpdates request");
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _client.State.Reason == TelegramClient.AnotherProgram, "the conflict");
        Assert.Equal(1, _telegram.Polls);

        _clock.Advance(TelegramClient.ConflictRetry);
        await Waiting.UntilAsync(() => _telegram.Polls == 2, "the next try");
    }

    [Fact]
    public async Task A_server_error_backs_off_and_too_many_requests_waits_as_asked()
    {
        _telegram.FailNextPoll(HttpStatusCode.BadGateway, "Bad Gateway");
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => _client.State.Reason?.Contains("Bad Gateway", StringComparison.Ordinal) == true, "the error");

        _telegram.FailNextPoll(HttpStatusCode.TooManyRequests, "Too Many Requests", retryAfter: 20);
        _clock.Advance(TelegramClient.Backoff(1));
        await Waiting.UntilAsync(() => _telegram.Polls == 2, "the retry after 5 seconds");

        await Waiting.UntilAsync(() => _client.State.Reason == "Telegram asked Deventry to wait 20s.", $"the 429, not {_client.State.Reason}");
        _clock.Advance(TimeSpan.FromSeconds(19));
        await Waiting.NeverAsync(() => _telegram.Polls > 2, "polling before retry_after");
        _clock.Advance(TimeSpan.FromSeconds(1));
        await Waiting.UntilAsync(() => _telegram.Polls == 3, "the poll after retry_after");
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(4, 300)]
    [InlineData(9, 300)]
    public void Backoff_is_5_seconds_30_a_minute_then_5_minutes(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), TelegramClient.Backoff(failures));
    }

    [Fact]
    public async Task A_bot_with_a_webhook_waits_for_Use_it_anyway()
    {
        _telegram.Webhook = "https://example.com/hook";
        await _client.StartAsync(_desk, TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _client.State.Status == RemoteClientStatus.Failed, "the webhook found");
        Assert.Equal("https://example.com/hook", _client.Webhook);
        Assert.Equal("Use it for Deventry anyway", _client.State.Fix?.Label);
        Assert.Equal(0, _telegram.Polls);

        await _client.UseWebhookAnywayAsync(TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => _telegram.Polls == 1, "polling");
        Assert.Single(_telegram.Of("deleteWebhook"));
    }

    [Fact]
    public async Task The_bots_name_and_commands_are_set_once_started()
    {
        string? identified = null;
        _client.Identified += name => identified = name;

        await StartAsync();

        Assert.Equal("deventry_bot", identified);
        var commands = Assert.Single(_telegram.Of("setMyCommands"));
        Assert.Equal(["tabs", "to", "stop", "usage", "quiet", "new", "help"], commands["commands"]!.AsArray().Select(c => (string)c!["command"]!));
    }
}
