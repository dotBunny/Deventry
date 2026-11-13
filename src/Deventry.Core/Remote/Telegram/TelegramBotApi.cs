using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;

namespace Deventry.Core.Remote.Telegram;

/// <summary>
/// The Telegram Bot API requests Deventry makes (DESIGN.md §18, "Remote clients", "Telegram"): <c>getMe</c>,
/// <c>getUpdates</c>, <c>sendMessage</c>, <c>editMessageText</c>, <c>editMessageReplyMarkup</c>,
/// <c>answerCallbackQuery</c>, <c>sendDocument</c>, <c>setMyCommands</c>, <c>getWebhookInfo</c> and
/// <c>deleteWebhook</c>. Every address holds the bot's token, so none is ever logged or put in an error: what goes
/// wrong is a <see cref="TelegramApiException"/> in words without it. Each request's deadline is on the injected
/// clock.
/// </summary>
public sealed class TelegramBotApi
{
    public const string DefaultBaseUrl = "https://api.telegram.org";

    /// <summary>How long Telegram holds a <c>getUpdates</c> waiting for an update.</summary>
    public const int PollSeconds = 50;

    /// <summary>A request's deadline, besides the long poll's own wait.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest a message's text can be.</summary>
    public const int MessageLimit = 4096;

    /// <summary>The longest a button's data can be, in bytes.</summary>
    public const int ButtonDataLimit = 64;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string _token;
    private readonly string _baseUrl;
    private readonly string _userAgent;

    public TelegramBotApi(HttpClient http, TimeProvider time, string token, string userAgent, string baseUrl = DefaultBaseUrl)
    {
        _http = http;
        _time = time;
        _token = token.Trim();
        _userAgent = userAgent;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>A token looks like one: the bot's id, a colon, and its secret, as @BotFather gives it.</summary>
    public static bool LooksLikeToken(string? token) =>
        token?.Trim() is { Length: > 10 } t && t.IndexOf(':') is var colon and > 0 && t[..colon].All(char.IsAsciiDigit)
        && t[(colon + 1)..].Length >= 20 && t[(colon + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>The bot itself: its <c>@username</c>, and that the token works.</summary>
    public async Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken = default) =>
        TelegramUser.Parse(await CallAsync("getMe", null, RequestTimeout, cancellationToken).ConfigureAwait(false))
        ?? throw new TelegramApiException("Telegram's answer about the bot couldn't be read.");

    /// <summary>The updates after <paramref name="offset"/>, waiting up to <paramref name="waitSeconds"/> for one.</summary>
    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, int waitSeconds = PollSeconds, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["timeout"] = waitSeconds,
            ["allowed_updates"] = new JsonArray("message", "callback_query"),
        };
        if (offset is { } from)
        {
            body["offset"] = from;
        }
        var result = await CallAsync("getUpdates", body, RequestTimeout + TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);
        return result is JsonArray updates ? [.. updates.Select(TelegramUpdate.Parse).OfType<TelegramUpdate>()] : [];
    }

    /// <summary>Sends a message in Telegram's HTML. Returns its id.</summary>
    /// <param name="silent">The phone doesn't make a sound for it.</param>
    public async Task<long> SendMessageAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons = null, long? replyTo = null,
        bool silent = false, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["link_preview_options"] = new JsonObject { ["is_disabled"] = true },
        };
        if (silent)
        {
            body["disable_notification"] = true;
        }
        if (replyTo is { } reply)
        {
            body["reply_parameters"] = new JsonObject { ["message_id"] = reply, ["allow_sending_without_reply"] = true };
        }
        if (buttons is { Count: > 0 })
        {
            body["reply_markup"] = Markup(buttons);
        }
        return MessageId(await CallAsync("sendMessage", body, RequestTimeout, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Changes a message's text, and its buttons: none takes them away. An edit into what it says already is no error.</summary>
    public async Task EditMessageTextAsync(long chatId, long messageId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons = null,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["chat_id"] = chatId,
            ["message_id"] = messageId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["link_preview_options"] = new JsonObject { ["is_disabled"] = true },
        };
        if (buttons is { Count: > 0 })
        {
            body["reply_markup"] = Markup(buttons);
        }
        await IgnoringNotModified(CallAsync("editMessageText", body, RequestTimeout, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Changes only a message's buttons: none takes them away.</summary>
    public async Task EditButtonsAsync(long chatId, long messageId, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["chat_id"] = chatId, ["message_id"] = messageId };
        body["reply_markup"] = Markup(buttons ?? []);
        await IgnoringNotModified(CallAsync("editMessageReplyMarkup", body, RequestTimeout, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Answers a button's tap, which stops its spinner, with a short note over the chat when there's one.</summary>
    public async Task AnswerCallbackAsync(string callbackId, string? text = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["callback_query_id"] = callbackId };
        if (text is { Length: > 0 })
        {
            body["text"] = text.Length > 200 ? text[..199] + "…" : text;
        }
        await CallAsync("answerCallbackQuery", body, RequestTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a file, such as a reply too long for a message, as Markdown. Returns its message's id.</summary>
    public async Task<long> SendDocumentAsync(long chatId, string fileName, byte[] content, string? captionHtml = null, long? replyTo = null,
        bool silent = false, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "chat_id" },
        };
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "document", fileName);
        if (captionHtml is { Length: > 0 })
        {
            form.Add(new StringContent(captionHtml), "caption");
            form.Add(new StringContent("HTML"), "parse_mode");
        }
        if (silent)
        {
            form.Add(new StringContent("true"), "disable_notification");
        }
        if (replyTo is { } reply)
        {
            form.Add(new StringContent(new JsonObject { ["message_id"] = reply, ["allow_sending_without_reply"] = true }.ToJsonString()), "reply_parameters");
        }
        return MessageId(await CallWithContentAsync("sendDocument", form, RequestTimeout, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Puts the commands in Telegram's <b>/</b> menu.</summary>
    public async Task SetCommandsAsync(IReadOnlyList<TelegramCommand> commands, CancellationToken cancellationToken = default)
    {
        var list = new JsonArray();
        foreach (var command in commands)
        {
            list.Add(new JsonObject { ["command"] = command.Command, ["description"] = command.Description });
        }
        await CallAsync("setMyCommands", new JsonObject { ["commands"] = list }, RequestTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The web address the bot sends its updates to, or null when it has none and can be polled.</summary>
    public async Task<string?> GetWebhookAsync(CancellationToken cancellationToken = default) =>
        await CallAsync("getWebhookInfo", null, RequestTimeout, cancellationToken).ConfigureAwait(false) is JsonObject info
        && info.GetString("url") is { Length: > 0 } url ? url : null;

    /// <summary>Takes the bot's webhook away, keeping the updates waiting, so it can be polled.</summary>
    public Task DeleteWebhookAsync(CancellationToken cancellationToken = default) =>
        CallAsync("deleteWebhook", new JsonObject { ["drop_pending_updates"] = false }, RequestTimeout, cancellationToken);

    private static JsonObject Markup(IReadOnlyList<IReadOnlyList<TelegramButton>> rows)
    {
        var keyboard = new JsonArray();
        foreach (var row in rows)
        {
            var buttons = new JsonArray();
            foreach (var button in row)
            {
                buttons.Add(new JsonObject { ["text"] = button.Text, ["callback_data"] = button.Data });
            }
            keyboard.Add(buttons);
        }
        return new JsonObject { ["inline_keyboard"] = keyboard };
    }

    private static long MessageId(JsonNode? result) =>
        result is JsonObject message && TelegramUser.Long(message["message_id"]) is { } id
            ? id
            : throw new TelegramApiException("Telegram's answer about the message couldn't be read.");

    private static async Task IgnoringNotModified(Task call)
    {
        try
        {
            await call.ConfigureAwait(false);
        }
        catch (TelegramApiException ex) when (ex.IsNotModified)
        {
            // It says that already.
        }
    }

    private Task<JsonNode?> CallAsync(string method, JsonObject? body, TimeSpan timeout, CancellationToken cancellationToken) =>
        CallWithContentAsync(method, body is null ? null : new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json"), timeout, cancellationToken);

    /// <summary>Calls <paramref name="method"/>, and gives back its <c>result</c>, or throws why not.</summary>
    private async Task<JsonNode?> CallWithContentAsync(string method, HttpContent? content, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, $"{_baseUrl}/bot{_token}/{method}") { Content = content };
        request.Headers.UserAgent.ParseAdd(_userAgent);
        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
            using (response)
            {
                text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TelegramApiException("Telegram didn't answer in time.", inner: Scrubbed(ex));
        }
        catch (HttpRequestException ex)
        {
            throw new TelegramApiException($"Couldn't reach Telegram: {Scrub(ex.Message)}", inner: Scrubbed(ex));
        }
        JsonObject? answer;
        try
        {
            answer = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            answer = null;
        }
        if (answer?["ok"] is JsonValue ok && ok.GetValueKind() == JsonValueKind.True)
        {
            return answer["result"];
        }
        var code = TelegramUser.Long(answer?["error_code"]) is { } number ? (int)number : (int)response.StatusCode;
        var description = answer?.GetString("description") is { Length: > 0 } said ? Scrub(said) : $"{(int)response.StatusCode} {response.ReasonPhrase}";
        var retryAfter = answer?["parameters"] is JsonObject parameters && TelegramUser.Long(parameters["retry_after"]) is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?)null;
        throw new TelegramApiException($"Telegram answered {method} with {description}", code, retryAfter);
    }

    /// <summary>Takes the token out of anything that may have quoted an address.</summary>
    private string Scrub(string text) => _token.Length == 0 ? text : text.Replace(_token, "<token>", StringComparison.Ordinal);

    /// <summary>An inner exception whose message may quote the address has its message scrubbed instead.</summary>
    private Exception? Scrubbed(Exception ex) => ex.Message.Contains(_token, StringComparison.Ordinal) ? new HttpRequestException(Scrub(ex.Message)) : ex;
}
