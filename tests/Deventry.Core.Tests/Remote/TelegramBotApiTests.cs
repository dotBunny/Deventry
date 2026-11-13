using System.Net;
using Deventry.Core.Remote.Chat;
using Deventry.Core.Remote.Telegram;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Remote;

/// <summary>The Bot API requests (DESIGN.md §18, "Telegram"), answered by a fake: nothing reaches Telegram.</summary>
public sealed class TelegramBotApiTests : IDisposable
{
    private readonly FakeTelegram _telegram = new();
    private readonly HttpClient _http;
    private readonly TelegramBotApi _api;

    public TelegramBotApiTests()
    {
        _http = new HttpClient(_telegram);
        _api = new TelegramBotApi(_http, new FakeTimeProvider(), FakeTelegram.Token, "Deventry/test");
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task GetMe_says_who_the_bot_is()
    {
        var me = await _api.GetMeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("deventry_bot", me.Username);
        Assert.True(me.IsBot);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_refused_token_is_unauthorized_and_never_quoted(HttpStatusCode status)
    {
        _telegram.GetMeAnswer = () => FakeTelegram.Error(status, $"Unauthorized: bot{FakeTelegram.Token} isn't one");

        var error = await Assert.ThrowsAsync<TelegramApiException>(() => _api.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.True(error.IsUnauthorized);
        Assert.DoesNotContain(FakeTelegram.Token, error.Message);
        Assert.DoesNotContain(FakeTelegram.Token.Split(':')[1], error.ToString());
    }

    [Fact]
    public async Task A_message_goes_as_HTML_without_link_previews_and_with_its_buttons()
    {
        await _api.SendMessageAsync(7, "<b>Hi</b>", [[new ChatButton("Allow", "r:1"), new ChatButton("Deny", "r:2")]], replyTo: 5, silent: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var sent = Assert.Single(_telegram.Sent);
        Assert.Equal(7, (long)sent["chat_id"]!);
        Assert.Equal("HTML", (string)sent["parse_mode"]!);
        Assert.True((bool)sent["disable_notification"]!);
        Assert.True((bool)sent["link_preview_options"]!["is_disabled"]!);
        Assert.Equal(5, (long)sent["reply_parameters"]!["message_id"]!);
        Assert.Equal([("Allow", "r:1"), ("Deny", "r:2")], FakeTelegram.Buttons(sent));
    }

    [Fact]
    public async Task Updates_are_read_with_their_messages_and_taps()
    {
        var now = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero);
        _telegram.Message(11, "hello", now, replyTo: 900);

        var update = Assert.Single(await _api.GetUpdatesAsync(null, 0, TestContext.Current.CancellationToken));

        Assert.Equal("hello", update.Message!.Text);
        Assert.Equal(11, update.Message.From!.Id);
        Assert.True(update.Message.Chat.IsPrivate);
        Assert.Equal(900, update.Message.ReplyTo);
        Assert.Equal(now, update.Message.Date);
        var poll = _telegram.Of("getUpdates").Single();
        Assert.Equal(["message", "callback_query"], poll["allowed_updates"]!.AsArray().Select(a => (string)a!));
    }

    [Fact]
    public async Task Too_many_requests_says_how_long_to_wait()
    {
        _telegram.FailNextPoll(HttpStatusCode.TooManyRequests, "Too Many Requests: retry after 7", retryAfter: 7);

        var error = await Assert.ThrowsAsync<TelegramApiException>(() => _api.GetUpdatesAsync(null, 0, TestContext.Current.CancellationToken));

        Assert.True(error.IsTooManyRequests);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public async Task Another_program_polling_is_a_conflict()
    {
        _telegram.FailNextPoll(HttpStatusCode.Conflict, "Conflict: terminated by other getUpdates request");

        var error = await Assert.ThrowsAsync<TelegramApiException>(() => _api.GetUpdatesAsync(null, 0, TestContext.Current.CancellationToken));

        Assert.True(error.IsConflict);
        Assert.False(error.IsTransient);
    }

    [Fact]
    public async Task A_long_reply_goes_as_a_Markdown_file()
    {
        await _api.SendDocumentAsync(7, "reply.md", "# Long\nreply"u8.ToArray(), "<b>Tab</b>", cancellationToken: TestContext.Current.CancellationToken);

        var sent = Assert.Single(_telegram.Of("sendDocument"));
        Assert.Equal("reply.md", (string)sent["document"]!);
        Assert.Equal("# Long\nreply", (string)sent["content"]!);
        Assert.Equal("HTML", (string)sent["parse_mode"]!);
    }

    [Fact]
    public async Task A_webhook_is_reported_and_can_be_taken_away()
    {
        _telegram.Webhook = "https://example.com/hook";

        Assert.Equal("https://example.com/hook", await _api.GetWebhookAsync(TestContext.Current.CancellationToken));
        await _api.DeleteWebhookAsync(TestContext.Current.CancellationToken);
        Assert.Null(await _api.GetWebhookAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("123456789:AAFakeTokenForTestsOnly_abcdefghijk", true)]
    [InlineData(" 123456789:AAFakeTokenForTestsOnly_abcdefghijk ", true)]
    [InlineData("AAFakeTokenForTestsOnly_abcdefghijk", false)]
    [InlineData("12ab:AAFakeTokenForTestsOnly_abcdefghijk", false)]
    [InlineData("123:short", false)]
    [InlineData("", false)]
    public void A_token_looks_like_BotFathers(string token, bool looks)
    {
        Assert.Equal(looks, TelegramBotApi.LooksLikeToken(token));
    }
}
