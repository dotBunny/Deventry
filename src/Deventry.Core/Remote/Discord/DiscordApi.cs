using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Remote.Chat;

namespace Deventry.Core.Remote.Discord;

/// <summary>
/// The Discord HTTP API requests Deventry makes (DESIGN.md §18, "Remote clients", "Discord"): the bot and its app, its
/// commands, the Gateway's address, opening a DM, sending, editing and deleting messages, and answering interactions.
/// The bot's token goes only in the <c>Authorization</c> header; an interaction's is in its address, so neither is
/// ever logged or put in an error: what goes wrong is a <see cref="DiscordApiException"/> in words without them. Each
/// request's deadline is on the injected clock.
/// </summary>
public sealed class DiscordApi
{
    public const string DefaultBaseUrl = "https://discord.com/api/v10";

    /// <summary>A request's deadline.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest a message's text can be.</summary>
    public const int MessageLimit = 2000;

    /// <summary>The longest a button's data can be.</summary>
    public const int CustomIdLimit = 100;

    /// <summary>The longest a button's label can be.</summary>
    public const int ButtonLabelLimit = 80;

    /// <summary>How many buttons a row holds, and how many rows a message.</summary>
    public const int ButtonsPerRow = 5;

    public const int RowsPerMessage = 5;

    // Message flags.
    private const int SuppressEmbeds = 1 << 2;
    private const int Ephemeral = 1 << 6;
    private const int SuppressNotifications = 1 << 12;

    // Interaction responses.
    public const int ChannelMessage = 4;
    public const int DeferredChannelMessage = 5;
    public const int DeferredUpdate = 6;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string _token;
    private readonly string _baseUrl;
    private readonly string _userAgent;

    public DiscordApi(HttpClient http, TimeProvider time, string token, string userAgent, string baseUrl = DefaultBaseUrl)
    {
        _http = http;
        _time = time;
        _token = token.Trim();
        _userAgent = userAgent;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// A token looks like a bot's, as the Developer Portal gives it: three parts with dots between them, of letters,
    /// digits, <c>-</c> and <c>_</c>, the first the bot's id in base 64.
    /// </summary>
    public static bool LooksLikeToken(string? token) =>
        token?.Trim().Split('.') is [var id, var time, var secret]
        && id.Length >= 16 && time.Length >= 4 && secret.Length >= 20
        && $"{id}{time}{secret}".All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>The bot itself: its name, and that the token works.</summary>
    public async Task<DiscordUser> GetMeAsync(CancellationToken cancellationToken = default) =>
        DiscordUser.Parse(await CallAsync(HttpMethod.Get, "/users/@me", null, "reading the bot", cancellationToken).ConfigureAwait(false))
        ?? throw new DiscordApiException("Discord's answer about the bot couldn't be read.");

    /// <summary>The bot's app: its id, where it can be installed, and where its interactions go.</summary>
    public async Task<DiscordApplication> GetApplicationAsync(CancellationToken cancellationToken = default) =>
        DiscordApplication.Parse(await CallAsync(HttpMethod.Get, "/applications/@me", null, "reading the app", cancellationToken).ConfigureAwait(false))
        ?? throw new DiscordApiException("Discord's answer about the app couldn't be read.");

    /// <summary>The Gateway's address, to open its WebSocket on.</summary>
    public async Task<string> GetGatewayAsync(CancellationToken cancellationToken = default) =>
        await CallAsync(HttpMethod.Get, "/gateway/bot", null, "finding the Gateway", cancellationToken).ConfigureAwait(false) is JsonObject gateway
        && gateway.GetString("url") is { Length: > 0 } url
            ? url
            : throw new DiscordApiException("Discord's answer about the Gateway couldn't be read.");

    /// <summary>
    /// Sets the app's slash commands, replacing any it had: <paramref name="pair"/> wherever the user is, with its code,
    /// and the rest in the bot's DM. Each is for installs to an account.
    /// </summary>
    public Task SetCommandsAsync(long applicationId, ChatCommand pair, IReadOnlyList<ChatCommand> commands, CancellationToken cancellationToken = default)
    {
        var list = new JsonArray { Command(pair, required: true, DiscordJson.GuildContext, DiscordJson.BotDmContext, DiscordJson.PrivateChannelContext) };
        foreach (var command in commands)
        {
            list.Add(Command(command, required: false, DiscordJson.BotDmContext));
        }
        return CallAsync(HttpMethod.Put, $"/applications/{DiscordJson.Id(applicationId)}/commands", list, "setting the app's commands", cancellationToken);

        static JsonObject Command(ChatCommand command, bool required, params int[] contexts)
        {
            var json = new JsonObject
            {
                ["name"] = command.Command,
                ["type"] = 1,
                ["description"] = command.Description,
                ["integration_types"] = new JsonArray(int.Parse(DiscordJson.UserInstall, CultureInfo.InvariantCulture)),
                ["contexts"] = new JsonArray([.. contexts.Select(c => (JsonNode)c)]),
            };
            if (command.Argument is { } argument)
            {
                json["options"] = new JsonArray(new JsonObject
                {
                    ["type"] = 3,
                    ["name"] = argument,
                    ["description"] = command.ArgumentDescription ?? argument,
                    ["required"] = required,
                });
            }
            return json;
        }
    }

    /// <summary>The DM channel with <paramref name="userId"/>: the one there is, or a new one. Discord may refuse it (<see cref="DiscordApiException.IsCannotMessageUser"/>).</summary>
    public async Task<long> OpenDmAsync(long userId, CancellationToken cancellationToken = default) =>
        await CallAsync(HttpMethod.Post, "/users/@me/channels", new JsonObject { ["recipient_id"] = DiscordJson.Id(userId) }, "opening a DM", cancellationToken)
            .ConfigureAwait(false) is JsonObject channel && DiscordJson.Snowflake(channel["id"]) is { } id
            ? id
            : throw new DiscordApiException("Discord's answer about the DM couldn't be read.");

    /// <summary>Sends a message in Discord's Markdown, with no link previews and pinging no one. Returns its id.</summary>
    /// <param name="silent">Sent as <c>@silent</c>: the phone doesn't make a sound for it.</param>
    public async Task<long> SendMessageAsync(long channelId, string content, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons = null, long? replyTo = null,
        bool silent = false, CancellationToken cancellationToken = default)
    {
        var body = MessageBody(content, silent);
        if (replyTo is { } reply)
        {
            body["message_reference"] = new JsonObject { ["message_id"] = DiscordJson.Id(reply), ["fail_if_not_exists"] = false };
        }
        if (buttons is { Count: > 0 })
        {
            body["components"] = Components(buttons);
        }
        return MessageId(await CallAsync(HttpMethod.Post, $"/channels/{DiscordJson.Id(channelId)}/messages", body, "sending a message", cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>Changes a message's text, when there's some, and its buttons: none takes them away.</summary>
    public Task EditMessageAsync(long channelId, long messageId, string? content, IReadOnlyList<IReadOnlyList<ChatButton>>? buttons,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["components"] = Components(buttons ?? []) };
        if (content is not null)
        {
            body["content"] = Fit(content);
        }
        return CallAsync(HttpMethod.Patch, $"/channels/{DiscordJson.Id(channelId)}/messages/{DiscordJson.Id(messageId)}", body, "editing a message", cancellationToken);
    }

    /// <summary>Sends a file, such as a reply too long for a message, with a caption. Returns its message's id.</summary>
    public async Task<long> SendFileAsync(long channelId, string fileName, byte[] content, string? caption = null, bool silent = false,
        CancellationToken cancellationToken = default)
    {
        var payload = MessageBody(caption ?? "", silent);
        payload["attachments"] = new JsonArray(new JsonObject { ["id"] = 0, ["filename"] = fileName });
        using var form = new MultipartFormDataContent
        {
            { new StringContent(payload.ToJsonString(Json), Encoding.UTF8, "application/json"), "payload_json" },
        };
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "files[0]", fileName);
        return MessageId(await SendAsync(HttpMethod.Post, $"/channels/{DiscordJson.Id(channelId)}/messages", form, "sending a file", auth: true, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Answers an interaction, as it must be within 3 seconds: <see cref="DeferredChannelMessage"/> shows that the bot is
    /// working on it, <see cref="DeferredUpdate"/> just takes a tap, and <see cref="ChannelMessage"/> answers with words.
    /// </summary>
    /// <param name="ephemeral">Only the user who used it sees the answer.</param>
    public Task AnswerInteractionAsync(long interactionId, string interactionToken, int type, string? content = null, bool ephemeral = false,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["type"] = type };
        if (content is not null || ephemeral)
        {
            var data = content is null ? new JsonObject() : MessageBody(content, silent: false);
            if (ephemeral)
            {
                data["flags"] = (data["flags"]?.GetValue<int>() ?? 0) | Ephemeral;
            }
            body["data"] = data;
        }
        return CallAsync(HttpMethod.Post, $"/interactions/{DiscordJson.Id(interactionId)}/{interactionToken}/callback", body, "answering an interaction",
            cancellationToken, auth: false);
    }

    /// <summary>Says something after an interaction was answered, while its token lasts: only its user sees it when <paramref name="ephemeral"/>.</summary>
    public Task FollowUpAsync(long applicationId, string interactionToken, string content, bool ephemeral, CancellationToken cancellationToken = default)
    {
        var body = MessageBody(content, silent: false);
        if (ephemeral)
        {
            body["flags"] = (body["flags"]?.GetValue<int>() ?? 0) | Ephemeral;
        }
        return CallAsync(HttpMethod.Post, $"/webhooks/{DiscordJson.Id(applicationId)}/{interactionToken}", body, "following up an interaction", cancellationToken,
            auth: false);
    }

    /// <summary>Puts words in a deferred answer to an interaction, while its token lasts.</summary>
    public Task EditAnswerAsync(long applicationId, string interactionToken, string content, CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Patch, $"/webhooks/{DiscordJson.Id(applicationId)}/{interactionToken}/messages/@original",
            new JsonObject { ["content"] = Fit(content), ["allowed_mentions"] = NoMentions() }, "editing an interaction's answer", cancellationToken, auth: false);

    /// <summary>Takes a deferred answer away, once what it answered is said in messages of their own.</summary>
    public Task DeleteAnswerAsync(long applicationId, string interactionToken, CancellationToken cancellationToken = default) =>
        CallAsync(HttpMethod.Delete, $"/webhooks/{DiscordJson.Id(applicationId)}/{interactionToken}/messages/@original", null, "deleting an interaction's answer",
            cancellationToken, auth: false);

    // ---- Bodies -----------------------------------------------------------------------------------------------------

    private static JsonObject MessageBody(string content, bool silent) => new()
    {
        ["content"] = Fit(content),
        ["flags"] = SuppressEmbeds | (silent ? SuppressNotifications : 0),
        ["allowed_mentions"] = NoMentions(),
    };

    private static JsonObject NoMentions() => new() { ["parse"] = new JsonArray() };

    /// <summary>
    /// Text cut to fit a message, as a last resort: what the bot writes fits already, but a prompt's Markdown escaped
    /// can grow. A code block it cuts into is closed.
    /// </summary>
    internal static string Fit(string content)
    {
        if (content.Length <= MessageLimit)
        {
            return content;
        }
        var cut = content[..(MessageLimit - 6)];
        var fences = 0;
        for (var at = cut.IndexOf("```", StringComparison.Ordinal); at >= 0; at = cut.IndexOf("```", at + 3, StringComparison.Ordinal))
        {
            fences++;
        }
        return fences % 2 == 1 ? $"{cut}\n```…" : $"{cut}…";
    }

    /// <summary>
    /// Buttons as Discord's rows: as laid out when they fit its limits of 5 a row and 5 rows; otherwise in rows of 5, as
    /// many as fit. Labels are cut at 80 characters.
    /// </summary>
    internal static JsonArray Components(IReadOnlyList<IReadOnlyList<ChatButton>> rows)
    {
        var laidOut = rows.Count <= RowsPerMessage && rows.All(r => r.Count <= ButtonsPerRow)
            ? rows
            : [.. rows.SelectMany(r => r).Chunk(ButtonsPerRow).Take(RowsPerMessage).Select(r => (IReadOnlyList<ChatButton>)r)];
        var components = new JsonArray();
        foreach (var row in laidOut.Where(r => r.Count > 0))
        {
            var buttons = new JsonArray();
            foreach (var button in row)
            {
                buttons.Add(new JsonObject
                {
                    ["type"] = 2,
                    ["style"] = 2,
                    ["label"] = button.Text.Length > ButtonLabelLimit ? $"{button.Text[..(ButtonLabelLimit - 1)]}…" : button.Text,
                    ["custom_id"] = button.Data,
                });
            }
            components.Add(new JsonObject { ["type"] = 1, ["components"] = buttons });
        }
        return components;
    }

    private static long MessageId(JsonNode? result) =>
        result is JsonObject message && DiscordJson.Snowflake(message["id"]) is { } id
            ? id
            : throw new DiscordApiException("Discord's answer about the message couldn't be read.");

    // ---- Calls ------------------------------------------------------------------------------------------------------

    private Task<JsonNode?> CallAsync(HttpMethod method, string route, JsonNode? body, string what, CancellationToken cancellationToken, bool auth = true) =>
        SendAsync(method, route, body is null ? null : new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json"), what, auth, cancellationToken);

    /// <summary>Makes a request, and gives back its answer, or throws why not.</summary>
    /// <param name="what">What it does, for an error: "sending a message". Never the route, which can hold an interaction's token.</param>
    private async Task<JsonNode?> SendAsync(HttpMethod method, string route, HttpContent? content, string what, bool auth, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var request = new HttpRequestMessage(method, $"{_baseUrl}{route}") { Content = content };
        request.Headers.UserAgent.ParseAdd(_userAgent);
        if (auth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _token);
        }
        HttpStatusCode status;
        string text;
        TimeSpan? retryHeader;
        try
        {
            using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
            status = response.StatusCode;
            retryHeader = response.Headers.RetryAfter?.Delta;
            text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new DiscordApiException($"Discord didn't answer in time, {what}.");
        }
        catch (HttpRequestException ex)
        {
            // The address can hold an interaction's token: the error is said without it.
            throw new DiscordApiException($"Couldn't reach Discord, {what}: {ex.HttpRequestError}.");
        }
        JsonNode? answer;
        try
        {
            answer = text.Length > 0 ? JsonNode.Parse(text) : null;
        }
        catch (JsonException)
        {
            answer = null;
        }
        if ((int)status is >= 200 and < 300)
        {
            return answer;
        }
        var error = answer as JsonObject;
        var code = error?["code"] is JsonValue c && c.TryGetValue<int>(out var number) ? number : (int?)null;
        var message = error?.GetString("message") is { Length: > 0 } said ? said : status.ToString();
        var retryAfter = error?["retry_after"] is JsonValue r && r.TryGetValue<double>(out var seconds) ? TimeSpan.FromSeconds(seconds) : retryHeader;
        throw new DiscordApiException($"Discord answered {what} with {(int)status} {message}", (int)status, code, retryAfter);
    }
}
