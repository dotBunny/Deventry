using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Remote.Chat;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Discord;

/// <summary>
/// The Discord bot (DESIGN.md §18, "Remote clients", "Discord"): an app the user makes and adds to their own Discord
/// account, in no server. It keeps Discord's Gateway open to hear its DMs and its interactions, answers each
/// interaction within Discord's 3 seconds, and writes Discord's Markdown. What it says and does in the chat is every
/// chat bot's (<see cref="ChatBotClient"/>).
/// </summary>
public sealed class DiscordClient : ChatBotClient
{
    public const string ClientKind = "discord";

    public const string ClientName = "Discord";

    public const string TokenRefused = "Discord didn't accept the bot's token. Set it again in Settings → Remote clients.";

    public const string NoUserInstall =
        "This app can't be added to a Discord account. In the Developer Portal, under Installation, tick User Install, then turn Discord off and on here.";

    public const string HasEndpoint =
        "This app sends its commands and buttons to a web address, so Deventry can't hear them. In the Developer Portal, under General Information, clear Interactions Endpoint URL, then turn Discord off and on here.";

    public const string AnotherProgram = "Another Deventry, or another program, answered first: only one should use this bot.";

    /// <summary>Pairing's command, which works in any chat, with the code.</summary>
    public static ChatCommand Pair { get; } = new("pair", "Pair with Deventry, with the code its Settings shows", "code", "The code Settings → Remote clients → Discord shows");

    private readonly DiscordApi _api;
    private readonly DiscordGateway _gateway;

    /// <summary>Each paired account's DM with the bot, by its user id, as the Gateway or Discord said.</summary>
    private readonly ConcurrentDictionary<long, long> _dms = new();

    private long _applicationId;
    private long _botId;
    private int _ready;

    /// <param name="sockets">Makes the Gateway's WebSocket: <see cref="DiscordWebSocket"/>, which tests replace.</param>
    public DiscordClient(DiscordApi api, Func<IDiscordSocket> sockets, string token, RemotePairing pairing, TimeProvider time, ILogger logger,
        ChatBotOptions? options = null)
        : base(pairing, time, logger, options)
    {
        _api = api;
        _gateway = new DiscordGateway(sockets, time, token, logger, OnDispatchAsync, (reason, _) => SetState(new RemoteClientState(RemoteClientStatus.Connecting, reason)));
    }

    public override string Kind => ClientKind;

    public override string Name => ClientName;

    protected override ChatFormat Format => DiscordFormat.Instance;

    protected override string PairCommand => Pair.Command;

    /// <summary>What the App keeps as the client's identity: its app's id, for the link that adds it, and the bot's name.</summary>
    public static string IdentityOf(long applicationId, string name) => $"{applicationId.ToString(CultureInfo.InvariantCulture)}:{name}";

    /// <summary>The app's id and the bot's name in an identity; an identity in no other form is the name alone.</summary>
    public static (long? ApplicationId, string Name) ParseIdentity(string identity)
    {
        var colon = identity.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && long.TryParse(identity[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? (id, identity[(colon + 1)..]) : (null, identity);
    }

    // ---- Listening --------------------------------------------------------------------------------------------------

    protected override async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (await RetryAsync(_api.GetMeAsync, cancellationToken).ConfigureAwait(false) is not { } me
            || await RetryAsync(_api.GetApplicationAsync, cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return;
        }
        _botId = me.Id;
        _applicationId = app.Id;
        SetIdentity(IdentityOf(app.Id, me.Username));
        if (!app.CanInstallToUser)
        {
            SetState(new RemoteClientState(RemoteClientStatus.Failed, NoUserInstall));
            return;
        }
        if (app.InteractionsEndpoint is not null)
        {
            SetState(new RemoteClientState(RemoteClientStatus.Failed, HasEndpoint));
            return;
        }
        try
        {
            await _api.SetCommandsAsync(app.Id, Pair, Commands, cancellationToken).ConfigureAwait(false);
        }
        catch (DiscordApiException ex)
        {
            Logger.LogInformation("Discord: couldn't set the app's commands: {Error}", ex.Message);
        }
        if (await RetryAsync(_api.GetGatewayAsync, cancellationToken).ConfigureAwait(false) is not { } address)
        {
            return;
        }
        var end = await _gateway.RunAsync(address, cancellationToken).ConfigureAwait(false);
        SetState(new RemoteClientState(RemoteClientStatus.Failed, end.Code == 4004 ? TokenRefused : end.Reason));
    }

    /// <summary>A request, until it's answered: null when the token was refused.</summary>
    private async Task<T?> RetryAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken) where T : class
    {
        for (var failures = 1; ; failures++)
        {
            try
            {
                return await call(cancellationToken).ConfigureAwait(false);
            }
            catch (DiscordApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return null;
            }
            catch (DiscordApiException ex)
            {
                var wait = ex.IsTooManyRequests ? ex.RetryAfter ?? Backoff(failures) : Backoff(failures);
                var reason = ex.IsTooManyRequests ? $"Discord asked Deventry to wait {Formats.Duration(wait)}." : ex.Message;
                Logger.LogInformation("Discord: {Error} Trying again in {Wait}.", ex.Message, Formats.Duration(wait));
                // The wait starts before the state says so: whoever reads the state can count on it from then on.
                var waiting = Task.Delay(wait, Time, cancellationToken);
                SetState(new RemoteClientState(RemoteClientStatus.Connecting, reason));
                await waiting.ConfigureAwait(false);
            }
        }
    }

    /// <summary>Each event from the Gateway, on its thread: the work it brings is posted to the loop, after any answer Discord needs at once.</summary>
    private async Task OnDispatchAsync(string name, JsonObject? data, CancellationToken cancellationToken)
    {
        switch (name)
        {
            case "READY":
                if (DiscordUser.Parse(data?["user"]) is { } me)
                {
                    _botId = me.Id;
                }
                SetState(RemoteClientState.Connected);
                if (Interlocked.Exchange(ref _ready, 1) == 0 && AnnounceBack)
                {
                    Post(AnnounceBackAsync);
                }
                break;
            case "RESUMED":
                SetState(RemoteClientState.Connected);
                break;
            case "MESSAGE_CREATE":
                OnMessage(DiscordMessage.Parse(data));
                break;
            case "INTERACTION_CREATE" when DiscordInteraction.Parse(data) is { } interaction:
                await OnInteractionAsync(interaction, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>A message in the bot's DM. Discord keeps none for a bot while it's away, so none is stale.</summary>
    private void OnMessage(DiscordMessage? message)
    {
        if (message is null || message.GuildId is not null || message.Author.IsBot || message.Author.Id == _botId)
        {
            return;
        }
        if (Pairing.IsPaired(message.Author.Id))
        {
            _dms[message.Author.Id] = message.ChannelId;
        }
        var chat = new ChatMessage(message.ChannelId, message.Id, message.Author.ToChat(), message.Timestamp, message.Content, message.ReplyTo, message.HasMedia,
            IsPrivate: true);
        Post(token => HandleMessageAsync(chat, stale: false, token));
    }

    private async Task OnInteractionAsync(DiscordInteraction interaction, CancellationToken cancellationToken)
    {
        if (interaction.User is not { IsBot: false } user)
        {
            return;
        }
        var paired = Pairing.IsPaired(user.Id);
        if (paired && interaction.InBotDm && interaction.ChannelId is { } dm)
        {
            _dms[user.Id] = dm;
        }
        switch (interaction.Type)
        {
            case DiscordInteractionType.Component:
                // Every tap is taken at once, saying nothing; what it does follows. A stranger's does nothing.
                if (await AcknowledgeAsync(interaction, DiscordApi.DeferredUpdate, ephemeral: false, cancellationToken).ConfigureAwait(false))
                {
                    var tap = new ChatTap(DiscordJson.Id(interaction.Id), user.ToChat(), interaction.ChannelId, interaction.MessageId, interaction.CustomId, interaction.Token);
                    Post(token => HandleTapAsync(tap, token));
                }
                break;
            case DiscordInteractionType.Command when interaction.CommandName == Pair.Command:
                await OnPairAsync(interaction, user, paired, cancellationToken).ConfigureAwait(false);
                break;
            case DiscordInteractionType.Command when paired && interaction is { InBotDm: true, ChannelId: { } channel }:
                if (await AcknowledgeAsync(interaction, DiscordApi.DeferredChannelMessage, ephemeral: false, cancellationToken).ConfigureAwait(false))
                {
                    // The command is answered in messages of their own, which can be replied to and edited for as long
                    // as they're needed; the answer that says the bot is thinking is taken away once they're sent.
                    var text = interaction.Argument is { Length: > 0 } argument ? $"/{interaction.CommandName} {argument}" : $"/{interaction.CommandName}";
                    var command = new ChatMessage(channel, 0, user.ToChat(), Time.GetUtcNow(), text, null, HasMedia: false, IsPrivate: true);
                    Post(async token =>
                    {
                        await HandleMessageAsync(command, stale: false, token).ConfigureAwait(false);
                        await QuietlyAsync(t => _api.DeleteAnswerAsync(_applicationId, interaction.Token, t), token).ConfigureAwait(false);
                    });
                }
                break;
            case DiscordInteractionType.Command when paired:
                await QuietlyAsync(t => _api.AnswerInteractionAsync(interaction.Id, interaction.Token, DiscordApi.ChannelMessage, Format.Escape("Use me in our DM."),
                    ephemeral: true, t), cancellationToken).ConfigureAwait(false);
                break;
            // A stranger's command gets no answer at all, so the bot doesn't say it belongs to anyone.
        }
    }

    /// <summary>
    /// <c>/pair</c>, in any chat. The code is checked here, on the Gateway's thread: a wrong one gets no answer at all,
    /// and the right one's answer can't wait for the loop, with Discord's 3 seconds running. Paired from another chat,
    /// the welcome goes to the bot's DM, which it opens itself if Discord lets it.
    /// </summary>
    private async Task OnPairAsync(DiscordInteraction interaction, DiscordUser user, bool paired, CancellationToken cancellationToken)
    {
        if (!paired && (interaction.Argument is not { Length: > 0 } code || Pairing.TryPair(code, user.Id, user.Name, user.Username) != PairingOutcome.Paired))
        {
            return;
        }
        if (!paired)
        {
            Logger.LogInformation("Discord: paired an account.");
        }
        if (!await AcknowledgeAsync(interaction, DiscordApi.DeferredChannelMessage, ephemeral: !interaction.InBotDm, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        if (paired || interaction is { InBotDm: true, ChannelId: not null })
        {
            Post(token => QuietlyAsync(t => _api.EditAnswerAsync(_applicationId, interaction.Token, paired ? AlreadyPaired() : Welcome(), t), token));
            return;
        }
        Post(async token =>
        {
            var said = Format.Escape(RemoteMessages.Paired(Desk.MachineName));
            string answer;
            try
            {
                await SendTextAsync(await DmOfAsync(user.Id, token).ConfigureAwait(false), Welcome(), silent: false, token).ConfigureAwait(false);
                answer = $"{said} {Format.Escape("I've written to you in our DM.")}";
            }
            catch (DiscordApiException ex) when (ex.IsCannotMessageUser)
            {
                answer = $"{said} {Format.Escape("Discord doesn't let me write to you first: open my profile, choose Message, and send /help there.")}";
            }
            await QuietlyAsync(t => _api.EditAnswerAsync(_applicationId, interaction.Token, answer, t), token).ConfigureAwait(false);
        });
    }

    /// <summary>Answers an interaction at once, as Discord needs within 3 seconds. False when it couldn't.</summary>
    private async Task<bool> AcknowledgeAsync(DiscordInteraction interaction, int type, bool ephemeral, CancellationToken cancellationToken)
    {
        try
        {
            await _api.AnswerInteractionAsync(interaction.Id, interaction.Token, type, ephemeral: ephemeral, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DiscordApiException ex) when (ex.IsAlreadyAnswered)
        {
            Logger.LogWarning("Discord: an interaction was answered by someone else first.");
            SetState(new RemoteClientState(RemoteClientStatus.Connected, AnotherProgram));
            return false;
        }
        catch (DiscordApiException ex)
        {
            Logger.LogWarning("Discord: couldn't answer an interaction: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>Something an interaction's token does once its 3 seconds are answered: one that fails is logged, and the rest goes on.</summary>
    private async Task QuietlyAsync(Func<CancellationToken, Task> call, CancellationToken cancellationToken)
    {
        try
        {
            await call(cancellationToken).ConfigureAwait(false);
        }
        catch (DiscordApiException ex)
        {
            Logger.LogInformation("Discord: {Error}", ex.Message);
        }
    }

    /// <summary>The DM with a paired account: the one the Gateway showed, or one Discord opens.</summary>
    private async Task<long> DmOfAsync(long userId, CancellationToken cancellationToken)
    {
        if (_dms.TryGetValue(userId, out var dm))
        {
            return dm;
        }
        dm = await _api.OpenDmAsync(userId, cancellationToken).ConfigureAwait(false);
        _dms[userId] = dm;
        return dm;
    }

    // ---- Sending ----------------------------------------------------------------------------------------------------

    protected override Task<long> DeliverMessageAsync(long chat, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, long? replyTo, bool silent,
        CancellationToken cancellationToken) =>
        _api.SendMessageAsync(chat, text, buttons, replyTo, silent, cancellationToken);

    protected override Task DeliverEditAsync(long chat, long message, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken) =>
        _api.EditMessageAsync(chat, message, text, buttons, cancellationToken);

    protected override Task DeliverButtonsAsync(long chat, long message, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken) =>
        _api.EditMessageAsync(chat, message, null, buttons, cancellationToken);

    protected override Task<long> DeliverFileAsync(long chat, string fileName, byte[] content, string caption, bool silent, CancellationToken cancellationToken) =>
        _api.SendFileAsync(chat, fileName, content, caption, silent, cancellationToken);

    /// <summary>The tap was taken already; a note follows it, which only its sender sees.</summary>
    protected override Task AnswerTapAsync(ChatTap tap, string? text, CancellationToken cancellationToken) =>
        text is { Length: > 0 } && tap.Token is { } token
            ? _api.FollowUpAsync(_applicationId, token, Format.Escape(text), ephemeral: true, cancellationToken)
            : Task.CompletedTask;

    protected override async Task<IReadOnlyList<long>> PairedChatsAsync(CancellationToken cancellationToken)
    {
        var chats = new List<long>();
        foreach (var account in Pairing.Accounts)
        {
            try
            {
                chats.Add(await DmOfAsync(account.UserId, cancellationToken).ConfigureAwait(false));
            }
            catch (DiscordApiException ex)
            {
                Logger.LogWarning("Discord: couldn't open a DM with a paired account: {Error}", ex.Message);
            }
        }
        return [.. chats.Distinct()];
    }
}
