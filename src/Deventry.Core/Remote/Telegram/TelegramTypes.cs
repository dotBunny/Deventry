using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Remote.Chat;

namespace Deventry.Core.Remote.Telegram;

/// <summary>A Telegram user, or a bot (Bot API <c>User</c>).</summary>
public sealed record TelegramUser(long Id, bool IsBot, string FirstName, string? LastName, string? Username)
{
    /// <summary>Their name, as Telegram shows it.</summary>
    public string Name => LastName is { Length: > 0 } last ? $"{FirstName} {last}" : FirstName;

    internal static TelegramUser? Parse(JsonNode? node) => node is JsonObject user && Long(user["id"]) is { } id
        ? new TelegramUser(id, user["is_bot"] is JsonValue bot && bot.GetValueKind() == JsonValueKind.True,
            user.GetString("first_name") ?? "", user.GetString("last_name"), user.GetString("username"))
        : null;

    internal static long? Long(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
}

/// <summary>A chat (Bot API <c>Chat</c>): only private chats count.</summary>
public sealed record TelegramChat(long Id, string Type)
{
    public bool IsPrivate => Type == "private";
}

/// <summary>A message the bot received, as much of it as Deventry reads (Bot API <c>Message</c>).</summary>
/// <param name="ReplyTo">The bot's message it replies to, with Telegram's reply.</param>
/// <param name="HasMedia">It carries a photo, a file, a voice message or the like, which the bot doesn't take yet.</param>
public sealed record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, DateTimeOffset Date, string? Text, long? ReplyTo, bool HasMedia)
{
    private static readonly string[] MediaFields = ["photo", "document", "voice", "audio", "video", "video_note", "sticker", "animation", "contact", "location"];

    internal static TelegramMessage? Parse(JsonNode? node)
    {
        if (node is not JsonObject message || TelegramUser.Long(message["message_id"]) is not { } id
            || message["chat"] is not JsonObject chat || TelegramUser.Long(chat["id"]) is not { } chatId)
        {
            return null;
        }
        var date = TelegramUser.Long(message["date"]) is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;
        var replyTo = message["reply_to_message"] is JsonObject reply ? TelegramUser.Long(reply["message_id"]) : null;
        return new TelegramMessage(id, new TelegramChat(chatId, chat.GetString("type") ?? ""), TelegramUser.Parse(message["from"]), date,
            message.GetString("text") ?? message.GetString("caption"), replyTo, MediaFields.Any(f => message[f] is not null));
    }
}

/// <summary>A tap on one of the bot's buttons (Bot API <c>CallbackQuery</c>).</summary>
/// <param name="Message">The bot's message the button is on; null when Telegram no longer has it.</param>
public sealed record TelegramCallback(string Id, TelegramUser From, TelegramMessage? Message, string? Data)
{
    internal static TelegramCallback? Parse(JsonNode? node) =>
        node is JsonObject callback && callback.GetString("id") is { } id && TelegramUser.Parse(callback["from"]) is { } from
            ? new TelegramCallback(id, from, TelegramMessage.Parse(callback["message"]), callback.GetString("data"))
            : null;
}

/// <summary>One update from <c>getUpdates</c>: a message or a button's tap. Anything else Deventry skips.</summary>
public sealed record TelegramUpdate(long UpdateId, TelegramMessage? Message, TelegramCallback? Callback)
{
    internal static TelegramUpdate? Parse(JsonNode? node) =>
        node is JsonObject update && TelegramUser.Long(update["update_id"]) is { } id
            ? new TelegramUpdate(id, TelegramMessage.Parse(update["message"]), TelegramCallback.Parse(update["callback_query"]))
            : null;
}

/// <summary>
/// Telegram refused a request, or couldn't be reached. The message is safe to show and log: it never holds the bot's
/// token, which every Bot API address does.
/// </summary>
public sealed class TelegramApiException(string message, int? errorCode = null, TimeSpan? retryAfter = null, Exception? inner = null) : ChatApiException(message, inner)
{
    /// <summary>The Bot API's <c>error_code</c>, an HTTP status; null when Telegram wasn't reached.</summary>
    public int? ErrorCode { get; } = errorCode;

    /// <summary>How long Telegram asked to wait, with a <c>429</c>.</summary>
    public override TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The token isn't a bot's: <c>401</c>, or <c>404</c> for one that isn't a token at all.</summary>
    public bool IsUnauthorized => ErrorCode is 401 or 404;

    /// <summary>Another program polls the same bot, or it has a webhook.</summary>
    public bool IsConflict => ErrorCode == 409;

    public override bool IsTooManyRequests => ErrorCode == 429;

    /// <summary>An edit Telegram refused: the message was deleted, or is too old to edit.</summary>
    public override bool IsMessageGone => ErrorCode == 400;

    /// <summary>Telegram couldn't be reached, or failed on its side: worth trying again.</summary>
    public bool IsTransient => ErrorCode is null or >= 500;

    /// <summary>An edit that would leave the message as it was, which Telegram calls an error.</summary>
    public bool IsNotModified => ErrorCode == 400 && Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase);
}
