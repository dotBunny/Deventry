using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Remote.Chat;

namespace Deventry.Core.Remote.Discord;

/// <summary>A Discord user, or a bot (Discord's <c>User</c>).</summary>
/// <param name="GlobalName">Their display name; null when they show their username.</param>
public sealed record DiscordUser(long Id, string Username, string? GlobalName, bool IsBot)
{
    /// <summary>Their name, as Discord shows it.</summary>
    public string Name => GlobalName is { Length: > 0 } shown ? shown : Username;

    public ChatUser ToChat() => new(Id, IsBot, Name, Username);

    internal static DiscordUser? Parse(JsonNode? node) => node is JsonObject user && DiscordJson.Snowflake(user["id"]) is { } id
        ? new DiscordUser(id, user.GetString("username") ?? "", user.GetString("global_name"), user["bot"] is JsonValue bot && bot.GetValueKind() == JsonValueKind.True)
        : null;
}

/// <summary>The app the bot belongs to, as much of it as Deventry reads (Discord's <c>Application</c>).</summary>
/// <param name="InstallTypes">Where it can be installed, when Discord said: <c>0</c> to a server, <c>1</c> to an account.</param>
/// <param name="InteractionsEndpoint">The web address its commands and buttons go to instead of the Gateway; null when there's none.</param>
public sealed record DiscordApplication(long Id, string Name, IReadOnlyList<string>? InstallTypes, string? InteractionsEndpoint)
{
    /// <summary>It can be added to an account (User Install): what Deventry pairs with. Unknown counts as yes.</summary>
    public bool CanInstallToUser => InstallTypes is null || InstallTypes.Contains(DiscordJson.UserInstall);

    internal static DiscordApplication? Parse(JsonNode? node) => node is JsonObject app && DiscordJson.Snowflake(app["id"]) is { } id
        ? new DiscordApplication(id, app.GetString("name") ?? "", app["integration_types_config"] is JsonObject config ? [.. config.Select(c => c.Key)] : null,
            app.GetString("interactions_endpoint_url") is { Length: > 0 } url ? url : null)
        : null;
}

/// <summary>A message in a channel the bot can see (Discord's <c>Message</c>), as much of it as Deventry reads.</summary>
/// <param name="GuildId">The server it's in; null in a DM.</param>
/// <param name="ReplyTo">The message it replies to.</param>
/// <param name="HasMedia">It carries files or stickers, which the bot doesn't take yet.</param>
public sealed record DiscordMessage(long Id, long ChannelId, long? GuildId, DiscordUser Author, DateTimeOffset Timestamp, string? Content, long? ReplyTo, bool HasMedia)
{
    internal static DiscordMessage? Parse(JsonNode? node)
    {
        if (node is not JsonObject message || DiscordJson.Snowflake(message["id"]) is not { } id || DiscordJson.Snowflake(message["channel_id"]) is not { } channel
            || DiscordUser.Parse(message["author"]) is not { } author)
        {
            return null;
        }
        var replyTo = message["message_reference"] is JsonObject reference ? DiscordJson.Snowflake(reference["message_id"]) : null;
        var media = message["attachments"] is JsonArray { Count: > 0 } || message["sticker_items"] is JsonArray { Count: > 0 };
        return new DiscordMessage(id, channel, DiscordJson.Snowflake(message["guild_id"]), author, DiscordJson.Time(message["timestamp"]), message.GetString("content"),
            replyTo, media);
    }
}

/// <summary>What an interaction is: Discord's <c>InteractionType</c>.</summary>
public enum DiscordInteractionType
{
    Unknown = 0,
    Ping = 1,
    Command = 2,
    Component = 3,
    Autocomplete = 4,
    ModalSubmit = 5,
}

/// <summary>
/// A slash command or a button's tap (Discord's <c>Interaction</c>), which must be answered within 3 seconds; its token
/// answers it for 15 minutes.
/// </summary>
/// <param name="Context">Where it was used: <c>0</c> a server, <c>1</c> the bot's DM, <c>2</c> another DM; null when Discord didn't say.</param>
/// <param name="CommandName">A command's name: <c>pair</c>.</param>
/// <param name="Argument">A command's first option, as text.</param>
/// <param name="CustomId">A button's data.</param>
/// <param name="MessageId">The message a button is on.</param>
public sealed record DiscordInteraction(long Id, string Token, DiscordInteractionType Type, int? Context, long? ChannelId, long? GuildId, DiscordUser? User,
    string? CommandName, string? Argument, string? CustomId, long? MessageId)
{
    /// <summary>Used in the bot's DM.</summary>
    public bool InBotDm => Context == DiscordJson.BotDmContext || (Context is null && GuildId is null);

    internal static DiscordInteraction? Parse(JsonNode? node)
    {
        if (node is not JsonObject interaction || DiscordJson.Snowflake(interaction["id"]) is not { } id || interaction.GetString("token") is not { Length: > 0 } token)
        {
            return null;
        }
        var type = interaction["type"] is JsonValue t && t.TryGetValue<int>(out var number) && Enum.IsDefined((DiscordInteractionType)number)
            ? (DiscordInteractionType)number
            : DiscordInteractionType.Unknown;
        var context = interaction["context"] is JsonValue c && c.TryGetValue<int>(out var where) ? where : (int?)null;
        var channel = DiscordJson.Snowflake(interaction["channel_id"]) ?? (interaction["channel"] is JsonObject ch ? DiscordJson.Snowflake(ch["id"]) : null);
        var user = DiscordUser.Parse(interaction["user"]) ?? (interaction["member"] is JsonObject member ? DiscordUser.Parse(member["user"]) : null);
        var data = interaction["data"] as JsonObject;
        var argument = data?["options"] is JsonArray { Count: > 0 } options && options[0] is JsonObject first ? DiscordJson.Text(first["value"]) : null;
        var message = interaction["message"] is JsonObject m ? DiscordJson.Snowflake(m["id"]) : null;
        return new DiscordInteraction(id, token, type, context, channel, DiscordJson.Snowflake(interaction["guild_id"]), user, data?.GetString("name"), argument,
            data?.GetString("custom_id"), message);
    }
}

/// <summary>
/// Discord refused a request, or couldn't be reached. The message is safe to show and log: it never holds the bot's
/// token, nor an interaction's.
/// </summary>
/// <param name="status">The HTTP status; null when Discord wasn't reached.</param>
/// <param name="code">Discord's own error code, such as <c>50007</c>; null when it gave none.</param>
public sealed class DiscordApiException(string message, int? status = null, int? code = null, TimeSpan? retryAfter = null, Exception? inner = null)
    : ChatApiException(message, inner)
{
    public int? Status { get; } = status;

    public int? Code { get; } = code;

    public override TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The token isn't a bot's.</summary>
    public bool IsUnauthorized => Status == 401;

    public override bool IsTooManyRequests => Status == 429;

    /// <summary>Unknown Message or Unknown Channel, or a message the bot can't edit.</summary>
    public override bool IsMessageGone => Status == 404 || Code is 10003 or 10008 or 50005;

    /// <summary>Discord won't let the bot write to the user first (<c>50007</c>), as it may not without a server they share.</summary>
    public bool IsCannotMessageUser => Code is 50007 or 50278;

    /// <summary>Someone answered the interaction first: another program uses the same app.</summary>
    public bool IsAlreadyAnswered => Code == 40060;

    /// <summary>Discord couldn't be reached, or failed on its side: worth trying again.</summary>
    public bool IsTransient => Status is null or >= 500;
}

/// <summary>Reading Discord's JSON: ids are snowflakes, sent as strings.</summary>
internal static class DiscordJson
{
    /// <summary>The key in an app's <c>integration_types_config</c> for installs to an account.</summary>
    public const string UserInstall = "1";

    public const int GuildContext = 0;

    public const int BotDmContext = 1;

    public const int PrivateChannelContext = 2;

    public static long? Snowflake(JsonNode? node) => node is JsonValue value
        ? value.TryGetValue<string>(out var text) ? long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null
        : value.TryGetValue<long>(out var number) ? number : null
        : null;

    public static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    public static DateTimeOffset Time(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : DateTimeOffset.MinValue;

    /// <summary>An option's value as text, whether Discord sent a string or a number.</summary>
    public static string? Text(JsonNode? node) => node is JsonValue value
        ? value.TryGetValue<string>(out var text) ? text : value.ToJsonString()
        : null;
}
