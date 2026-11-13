using Deventry.Core.Remote.Chat;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Telegram;

/// <summary>How the Telegram client paces itself. Tests take the waits out.</summary>
public sealed record TelegramClientOptions : ChatBotOptions
{
    /// <summary>How long Telegram holds a poll waiting for an update.</summary>
    public int PollSeconds { get; init; } = TelegramBotApi.PollSeconds;
}

/// <summary>
/// The Telegram bot (DESIGN.md §18, "Remote clients", "Telegram"): long-polls <c>getUpdates</c>, so there's no server
/// or open port, and writes Telegram's HTML. What it says and does in the chat is every chat bot's
/// (<see cref="ChatBotClient"/>).
/// </summary>
public sealed class TelegramClient : ChatBotClient
{
    public const string ClientKind = "telegram";

    public const string ClientName = "Telegram";

    /// <summary>Messages sent this long before Deventry started listening are passed over.</summary>
    public static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(2);

    /// <summary>How long it waits after Telegram says another program polls the bot.</summary>
    public static readonly TimeSpan ConflictRetry = TimeSpan.FromMinutes(1);

    public const string TokenRefused = "Telegram didn't accept the bot's token. Set it again in Settings → Remote clients.";

    public const string AnotherProgram = "Another Deventry, or another program, is using this bot. Trying again each minute.";

    private readonly TelegramBotApi _api;
    private readonly TelegramClientOptions _options;
    private readonly TaskCompletionSource _useWebhookAnyway = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TelegramClient(TelegramBotApi api, RemotePairing pairing, TimeProvider time, ILogger logger, TelegramClientOptions? options = null)
        : base(pairing, time, logger, options)
    {
        _api = api;
        _options = options ?? new TelegramClientOptions();
    }

    public override string Kind => ClientKind;

    public override string Name => ClientName;

    protected override ChatFormat Format => TelegramHtml.Instance;

    protected override string PairCommand => "start";

    /// <summary>The web address the bot sends its updates to, which keeps it from being polled; null when it has none.</summary>
    public string? Webhook { get; private set; }

    /// <summary>
    /// <b>Use it for Deventry anyway</b>: takes the bot's webhook away, so it can be polled. Deventry never does by
    /// itself.
    /// </summary>
    public async Task UseWebhookAnywayAsync(CancellationToken cancellationToken = default)
    {
        await _api.DeleteWebhookAsync(cancellationToken).ConfigureAwait(false);
        Webhook = null;
        _useWebhookAnyway.TrySetResult();
    }

    protected override void OnStopping() => _useWebhookAnyway.TrySetCanceled();

    // ---- Polling ----------------------------------------------------------------------------------------------------

    protected override async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (!await IdentifyAsync(cancellationToken).ConfigureAwait(false) || !await CheckWebhookAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            await _api.SetCommandsAsync(Commands, cancellationToken).ConfigureAwait(false);
        }
        catch (TelegramApiException ex)
        {
            Logger.LogInformation("Telegram: couldn't set the bot's commands: {Error}", ex.Message);
        }
        var since = Time.GetUtcNow();
        if (AnnounceBack)
        {
            Post(AnnounceBackAsync);
        }
        await PollAsync(since - StaleAge, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>getMe</c>, until it answers. False when the token was refused.</summary>
    private async Task<bool> IdentifyAsync(CancellationToken cancellationToken)
    {
        for (var failures = 1; ; failures++)
        {
            try
            {
                var me = await _api.GetMeAsync(cancellationToken).ConfigureAwait(false);
                if (me.Username is { Length: > 0 } username)
                {
                    SetIdentity(username);
                }
                return true;
            }
            catch (TelegramApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return false;
            }
            catch (TelegramApiException ex)
            {
                await WaitAfterAsync(ex, failures, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A bot with a webhook can't be polled: it waits for <b>Use it for Deventry anyway</b>. False when stopped meanwhile.</summary>
    private async Task<bool> CheckWebhookAsync(CancellationToken cancellationToken)
    {
        for (var failures = 1; ; failures++)
        {
            try
            {
                Webhook = await _api.GetWebhookAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (TelegramApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return false;
            }
            catch (TelegramApiException ex)
            {
                await WaitAfterAsync(ex, failures, cancellationToken).ConfigureAwait(false);
            }
        }
        if (Webhook is null)
        {
            return true;
        }
        SetState(new RemoteClientState(RemoteClientStatus.Failed, "This bot sends its updates to a web address, so Deventry can't listen to it.")
        {
            Fix = new RemoteClientFix("Use it for Deventry anyway",
                $"Its updates go to {Webhook}. Using it for Deventry takes that away, and whatever reads it there stops getting them.", UseWebhookAnywayAsync),
        });
        try
        {
            await _useWebhookAnyway.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        SetState(new RemoteClientState(RemoteClientStatus.Connecting));
        return true;
    }

    private async Task PollAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        long? offset = null;
        var first = true;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<TelegramUpdate> updates;
            try
            {
                updates = await _api.GetUpdatesAsync(offset, _options.PollSeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TelegramApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return;
            }
            catch (TelegramApiException ex)
            {
                failures++;
                await WaitAfterAsync(ex, failures, cancellationToken).ConfigureAwait(false);
                continue;
            }
            failures = 0;
            SetState(RemoteClientState.Connected);
            foreach (var update in updates)
            {
                offset = Math.Max(offset ?? 0, update.UpdateId + 1);
                var stale = update.Message is { } message && message.Date < staleBefore;
                Post(token => HandleUpdateAsync(update, stale, token));
            }
            if (first)
            {
                first = false;
                // Once the updates kept while Deventry was closed are in, it says how many it passed over.
                Post(ReportPassedOverAsync);
            }
        }
    }

    private Task HandleUpdateAsync(TelegramUpdate update, bool stale, CancellationToken cancellationToken) =>
        update.Callback is { } callback ? HandleTapAsync(new ChatTap(callback.Id, User(callback.From), callback.Message?.Chat.Id, callback.Message?.MessageId, callback.Data),
                cancellationToken)
        : update.Message is { } message ? HandleMessageAsync(new ChatMessage(message.Chat.Id, message.MessageId, message.From is { } from ? User(from) : null, message.Date,
                message.Text, message.ReplyTo, message.HasMedia, message.Chat.IsPrivate), stale, cancellationToken)
        : Task.CompletedTask;

    private static ChatUser User(TelegramUser user) => new(user.Id, user.IsBot, user.Name, user.Username);

    /// <summary>
    /// Waits after a failed request: a <c>429</c>'s <c>retry_after</c>, a minute for another program polling the bot, or
    /// the backoff, saying why meanwhile.
    /// </summary>
    private async Task WaitAfterAsync(TelegramApiException ex, int failures, CancellationToken cancellationToken)
    {
        var (wait, reason) = ex.IsTooManyRequests ? (ex.RetryAfter ?? Backoff(failures), null)
            : ex.IsConflict ? (ConflictRetry, AnotherProgram)
            : (Backoff(failures), ex.Message);
        reason ??= $"Telegram asked Deventry to wait {Formats.Duration(wait)}.";
        Logger.LogInformation("Telegram: {Error} Trying again in {Wait}.", ex.Message, Formats.Duration(wait));
        // The wait starts before the state says so: whoever reads the state can count on it from then on.
        var waiting = Task.Delay(wait, Time, cancellationToken);
        SetState(new RemoteClientState(RemoteClientStatus.Connecting, reason));
        await waiting.ConfigureAwait(false);
    }

    // ---- Sending ----------------------------------------------------------------------------------------------------

    protected override Task<long> DeliverMessageAsync(long chat, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, long? replyTo, bool silent,
        CancellationToken cancellationToken) =>
        _api.SendMessageAsync(chat, text, buttons, replyTo, silent, cancellationToken);

    protected override Task DeliverEditAsync(long chat, long message, string text, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken) =>
        _api.EditMessageTextAsync(chat, message, text, buttons, cancellationToken);

    protected override Task DeliverButtonsAsync(long chat, long message, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons, CancellationToken cancellationToken) =>
        _api.EditButtonsAsync(chat, message, buttons, cancellationToken);

    protected override Task<long> DeliverFileAsync(long chat, string fileName, byte[] content, string caption, bool silent, CancellationToken cancellationToken) =>
        _api.SendDocumentAsync(chat, fileName, content, caption, silent: silent, cancellationToken: cancellationToken);

    protected override Task AnswerTapAsync(ChatTap tap, string? text, CancellationToken cancellationToken) =>
        _api.AnswerCallbackAsync(tap.Id, text, cancellationToken);

    /// <summary>The paired accounts' private chats, whose ids are the accounts'.</summary>
    protected override Task<IReadOnlyList<long>> PairedChatsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<long>>([.. Pairing.Accounts.Select(a => a.UserId).Distinct()]);
}
