using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Deventry.Core.Tests.Support;

/// <summary>
/// The Telegram Bot API, answered here: the bot, its webhook, and updates queued by the test, which <c>getUpdates</c>
/// waits for as Telegram's long poll does; and every message, edit and tap's answer the bot sends, kept to check.
/// Nothing reaches Telegram.
/// </summary>
public sealed class FakeTelegram : HttpMessageHandler
{
    public const string Token = "123456789:AAFakeTokenForTestsOnly_abcdefghijk";

    private readonly Lock _lock = new();
    private readonly Queue<Func<HttpResponseMessage>> _polls = new();
    private readonly SemaphoreSlim _pollReady = new(0);
    private long _nextUpdate = 100;
    private long _nextMessage = 1000;

    public string? Webhook { get; set; }

    public string BotUsername { get; set; } = "deventry_bot";

    /// <summary>What <c>getMe</c> answers with instead of the bot, such as a 401 for a refused token.</summary>
    public Func<HttpResponseMessage>? GetMeAnswer { get; set; }

    /// <summary>Every request, by method: <c>sendMessage</c> and so on, with its body.</summary>
    public List<(string Method, JsonObject Body)> Calls { get; } = [];

    public IReadOnlyList<(string Method, JsonObject Body)> CallsCopy()
    {
        lock (_lock)
        {
            return [.. Calls];
        }
    }

    public IReadOnlyList<JsonObject> Of(string method) => [.. CallsCopy().Where(c => c.Method == method).Select(c => c.Body)];

    /// <summary>The id the bot's message was given, to reply to it.</summary>
    public static long IdOf(JsonObject sent) => (long)sent["_message_id"]!;

    /// <summary>The messages sent, with their text.</summary>
    public IReadOnlyList<JsonObject> Sent => Of("sendMessage");

    public int Polls { get; private set; }

    /// <summary>A message to the bot, in a private chat by default, sent now unless <paramref name="date"/> says.</summary>
    public long Message(long from, string text, DateTimeOffset date, long? replyTo = null, string chatType = "private", long? chat = null, bool photo = false)
    {
        var id = Interlocked.Increment(ref _nextMessage);
        var message = new JsonObject
        {
            ["message_id"] = id,
            ["from"] = User(from),
            ["chat"] = new JsonObject { ["id"] = chat ?? from, ["type"] = chatType },
            ["date"] = date.ToUnixTimeSeconds(),
            ["text"] = text,
        };
        if (replyTo is { } reply)
        {
            message["reply_to_message"] = new JsonObject { ["message_id"] = reply, ["chat"] = new JsonObject { ["id"] = chat ?? from, ["type"] = chatType } };
        }
        if (photo)
        {
            message.Remove("text");
            message["photo"] = new JsonArray(new JsonObject { ["file_id"] = "x" });
        }
        Update(new JsonObject { ["message"] = message });
        return id;
    }

    /// <summary>A tap on a button of the bot's message <paramref name="messageId"/>.</summary>
    public void Tap(long from, string data, long messageId)
    {
        Update(new JsonObject
        {
            ["callback_query"] = new JsonObject
            {
                ["id"] = $"cb{Interlocked.Increment(ref _nextMessage)}",
                ["from"] = User(from),
                ["message"] = new JsonObject
                {
                    ["message_id"] = messageId,
                    ["chat"] = new JsonObject { ["id"] = from, ["type"] = "private" },
                    ["date"] = 0,
                    ["text"] = "",
                },
                ["data"] = data,
            },
        });
    }

    /// <summary>The next poll answers with this error.</summary>
    public void FailNextPoll(HttpStatusCode status, string description, int? retryAfter = null)
    {
        var error = new JsonObject { ["ok"] = false, ["error_code"] = (int)status, ["description"] = description };
        if (retryAfter is { } seconds)
        {
            error["parameters"] = new JsonObject { ["retry_after"] = seconds };
        }
        lock (_lock)
        {
            _polls.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(error.ToJsonString()) });
        }
        _pollReady.Release();
    }

    private void Update(JsonObject update)
    {
        update["update_id"] = Interlocked.Increment(ref _nextUpdate);
        lock (_lock)
        {
            _polls.Enqueue(() => Ok(new JsonArray(update)));
        }
        _pollReady.Release();
    }

    private static JsonObject User(long id) => new() { ["id"] = id, ["is_bot"] = false, ["first_name"] = $"User{id}", ["username"] = $"user{id}" };

    /// <summary>The buttons of a message sent, row by row, as their labels and data.</summary>
    public static IReadOnlyList<(string Text, string Data)> Buttons(JsonObject sent) =>
        sent["reply_markup"]?["inline_keyboard"] is JsonArray rows
            ? [.. rows.OfType<JsonArray>().SelectMany(r => r.OfType<JsonObject>()).Select(b => ((string)b["text"]!, (string)b["callback_data"]!))]
            : [];

    public static string Data(JsonObject sent, string label) => Buttons(sent).First(b => b.Text == label).Data;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var prefix = $"/bot{Token}/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Error(HttpStatusCode.NotFound, "Not Found");
        }
        var method = path[prefix.Length..];
        var body = await Body(request, cancellationToken);
        if (method is "sendMessage" or "sendDocument")
        {
            // The id it's answered with, for a test to reply to it.
            body["_message_id"] = Interlocked.Increment(ref _nextMessage);
        }
        lock (_lock)
        {
            Calls.Add((method, body));
        }
        switch (method)
        {
            case "getMe":
                return GetMeAnswer?.Invoke() ?? Ok(new JsonObject { ["id"] = 42, ["is_bot"] = true, ["first_name"] = "Deventry", ["username"] = BotUsername });
            case "getWebhookInfo":
                return Ok(new JsonObject { ["url"] = Webhook ?? "" });
            case "deleteWebhook":
                Webhook = null;
                return Ok(true);
            case "getUpdates":
                Polls++;
                await _pollReady.WaitAsync(cancellationToken);
                Func<HttpResponseMessage> answer;
                lock (_lock)
                {
                    answer = _polls.Dequeue();
                }
                return answer();
            case "sendMessage":
            case "sendDocument":
                return Ok(new JsonObject { ["message_id"] = (long)body["_message_id"]!, ["chat"] = new JsonObject { ["id"] = 0, ["type"] = "private" }, ["date"] = 0 });
            default:
                return Ok(true);
        }
    }

    private static async Task<JsonObject> Body(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is MultipartFormDataContent form)
        {
            var fields = new JsonObject();
            foreach (var part in form)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                if (part.Headers.ContentDisposition?.FileName is { } fileName)
                {
                    fields[name] = fileName.Trim('"');
                    fields["content"] = Encoding.UTF8.GetString(await part.ReadAsByteArrayAsync(cancellationToken));
                }
                else
                {
                    fields[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            }
            return fields;
        }
        return request.Content is null ? [] : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken)) as JsonObject ?? [];
    }

    private static HttpResponseMessage Ok(JsonNode result) =>
        new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["ok"] = true, ["result"] = result }.ToJsonString()) };

    public static HttpResponseMessage Error(HttpStatusCode status, string description) =>
        new(status) { Content = new StringContent(new JsonObject { ["ok"] = false, ["error_code"] = (int)status, ["description"] = description }.ToJsonString()) };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollReady.Dispose();
        }
        base.Dispose(disposing);
    }
}
