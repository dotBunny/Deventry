using System.Net;
using System.Text.Json.Nodes;
using Deventry.Core.Remote.Chat;
using Deventry.Core.Remote.Discord;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Remote;

/// <summary>Discord's HTTP API (DESIGN.md §18, "Discord"), answered by a fake: nothing reaches Discord.</summary>
public sealed class DiscordApiTests : IDisposable
{
    private readonly FakeDiscord _discord = new();
    private readonly HttpClient _http;
    private readonly DiscordApi _api;

    public DiscordApiTests()
    {
        _http = new HttpClient(_discord);
        _api = new DiscordApi(_http, new FakeTimeProvider(), FakeDiscord.Token, "Deventry/test");
    }

    public void Dispose() => _http.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_bot_and_its_app_are_read_with_the_bots_token()
    {
        var me = await _api.GetMeAsync(Token);
        var app = await _api.GetApplicationAsync(Token);

        Assert.Equal(("deventry", true), (me.Username, me.IsBot));
        Assert.Equal(FakeDiscord.ApplicationId, app.Id);
        Assert.True(app.CanInstallToUser);
        Assert.All(_discord.Calls(), c => Assert.Equal($"Bot {FakeDiscord.Token}", c.Authorization));
    }

    [Fact]
    public async Task An_app_that_installs_only_to_servers_cannot_pair()
    {
        _discord.InstallTypes = ["0"];

        Assert.False((await _api.GetApplicationAsync(Token)).CanInstallToUser);
    }

    [Fact]
    public async Task A_refused_token_is_unauthorized()
    {
        _discord.GetMeAnswer = () => FakeDiscord.Error(HttpStatusCode.Unauthorized, 0, "401: Unauthorized");

        var error = await Assert.ThrowsAsync<DiscordApiException>(() => _api.GetMeAsync(Token));

        Assert.True(error.IsUnauthorized);
        Assert.DoesNotContain(FakeDiscord.Token, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_goes_without_previews_or_pings_and_silently_when_asked()
    {
        await _api.SendMessageAsync(9, "**Hi**", [[new ChatButton("Allow", "r:1"), new ChatButton("Deny", "r:2")]], replyTo: 5, silent: true, Token);

        var sent = Assert.Single(_discord.Sent);
        Assert.Equal("/channels/9/messages", sent.Route);
        Assert.Equal("**Hi**", FakeDiscord.Content(sent));
        Assert.Equal((1 << 2) | (1 << 12), (int)sent.Body["flags"]!);
        Assert.Empty(sent.Body["allowed_mentions"]!["parse"]!.AsArray());
        Assert.Equal("5", (string)sent.Body["message_reference"]!["message_id"]!);
        Assert.False((bool)sent.Body["message_reference"]!["fail_if_not_exists"]!);
        Assert.Equal([("Allow", "r:1"), ("Deny", "r:2")], FakeDiscord.Buttons(sent));
    }

    [Fact]
    public void Buttons_past_Discords_limits_are_laid_out_again_five_to_a_row_and_labels_are_cut()
    {
        var many = Enumerable.Range(1, 30).Select(i => new ChatButton($"Tab {i}", $"r:{i}")).Chunk(2).Select(r => (IReadOnlyList<ChatButton>)r).ToList();

        var rows = DiscordApi.Components(many);

        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(5, r!["components"]!.AsArray().Count));
        var label = (string)DiscordApi.Components([[new ChatButton(new string('x', 120), "r:1")]])[0]!["components"]![0]!["label"]!;
        Assert.Equal(80, label.Length);
        Assert.EndsWith("…", label);
    }

    [Fact]
    public void Text_too_long_is_cut_and_a_code_block_it_cuts_into_is_closed()
    {
        var fitted = DiscordApi.Fit("```bash\n" + new string('a', 3000));

        Assert.True(fitted.Length <= DiscordApi.MessageLimit);
        Assert.EndsWith("\n```…", fitted);
        Assert.Equal("short", DiscordApi.Fit("short"));
    }

    [Fact]
    public async Task An_interaction_is_answered_without_the_bots_token_and_its_own_is_never_in_an_error()
    {
        _discord.Next.Add(("POST", "/interactions/", () => FakeDiscord.Error(HttpStatusCode.NotFound, 10062, "Unknown interaction")));

        var error = await Assert.ThrowsAsync<DiscordApiException>(() => _api.AnswerInteractionAsync(77, "secret-interaction-token", DiscordApi.DeferredUpdate,
            cancellationToken: Token));

        Assert.DoesNotContain("secret-interaction-token", error.Message, StringComparison.Ordinal);
        Assert.Equal(10062, error.Code);
        var call = Assert.Single(_discord.Callbacks);
        Assert.Null(call.Authorization);
        Assert.Equal(DiscordApi.DeferredUpdate, (int)call.Body["type"]!);
    }

    [Fact]
    public async Task An_ephemeral_answer_and_follow_up_are_for_their_user_only()
    {
        await _api.AnswerInteractionAsync(77, "t", DiscordApi.DeferredChannelMessage, ephemeral: true, cancellationToken: Token);
        await _api.FollowUpAsync(FakeDiscord.ApplicationId, "t", "Allow", ephemeral: true, Token);

        Assert.Equal(1 << 6, (int)_discord.Callbacks.Single().Body["data"]!["flags"]!);
        var followUp = _discord.Of("POST", "/webhooks/").Single();
        Assert.Equal($"/webhooks/{FakeDiscord.ApplicationId}/t", followUp.Route);
        Assert.Equal((1 << 2) | (1 << 6), (int)followUp.Body["flags"]!);
    }

    [Fact]
    public async Task Too_many_requests_says_how_long_to_wait()
    {
        _discord.Next.Add(("POST", "/channels/", () => FakeDiscord.Error(HttpStatusCode.TooManyRequests, 0, "You are being rate limited.", retryAfter: 1.5)));

        var error = await Assert.ThrowsAsync<DiscordApiException>(() => _api.SendMessageAsync(9, "x", cancellationToken: Token));

        Assert.True(error.IsTooManyRequests);
        Assert.Equal(TimeSpan.FromSeconds(1.5), error.RetryAfter);
    }

    [Fact]
    public async Task A_DM_Discord_refuses_says_so()
    {
        _discord.RefuseDms.Add(12);

        var error = await Assert.ThrowsAsync<DiscordApiException>(() => _api.OpenDmAsync(12, Token));

        Assert.True(error.IsCannotMessageUser);
        Assert.Equal(FakeDiscord.DmOf(13), await _api.OpenDmAsync(13, Token));
    }

    [Fact]
    public async Task Pair_works_in_any_chat_and_the_rest_in_the_bots_DM_for_installs_to_an_account()
    {
        await _api.SetCommandsAsync(FakeDiscord.ApplicationId, DiscordClient.Pair, ChatBotClient.Commands, Token);

        var call = Assert.Single(_discord.Of("PUT", "/applications/"));
        Assert.Equal($"/applications/{FakeDiscord.ApplicationId}/commands", call.Route);
        var commands = call.Body["_array"]!.AsArray().Select(c => c!.AsObject()).ToList();
        Assert.Equal(["pair", "tabs", "to", "stop", "usage", "quiet", "new", "help"], commands.Select(c => (string)c["name"]!));
        Assert.All(commands, c => Assert.Equal([1], Ints(c["integration_types"])));
        Assert.Equal([0, 1, 2], Ints(commands[0]["contexts"]));
        Assert.True((bool)commands[0]["options"]![0]!["required"]!);
        Assert.All(commands.Skip(1), c => Assert.Equal([1], Ints(c["contexts"])));
        Assert.False((bool)commands.Single(c => (string)c["name"]! == "to")["options"]![0]!["required"]!);

        static IEnumerable<int> Ints(JsonNode? list) => list!.AsArray().Select(n => (int)n!);
    }

    [Fact]
    public async Task A_file_goes_as_an_attachment_with_its_caption()
    {
        await _api.SendFileAsync(9, "Hook reply.md", "# All of it"u8.ToArray(), "**Hook**: the whole reply", silent: true, Token);

        var sent = Assert.Single(_discord.Sent);
        Assert.Equal("Hook reply.md", (string)sent.Body["file"]!);
        Assert.Equal("# All of it", (string)sent.Body["file_content"]!);
        Assert.Equal("**Hook**: the whole reply", FakeDiscord.Content(sent));
    }

    [Theory]
    [InlineData("MTIzNDU2Nzg5MDEyMzQ1Njc4.GabcDe.abcdefghijklmnopqrstuvwxyz0123456789", true)]
    [InlineData("123456789:AAFakeTokenForTestsOnly_abcdefghijk", false)]
    [InlineData("not a token", false)]
    [InlineData("a.b.c", false)]
    public void A_token_looks_like_a_bots_or_not(string token, bool looks)
    {
        Assert.Equal(looks, DiscordApi.LooksLikeToken(token));
    }
}
