using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Deventry.Core.Tests.Support;

/// <summary>
/// Discord's HTTP API, answered here: the bot and its app, its commands, the Gateway's address, DMs, messages, files,
/// and the answers to interactions; every request kept to check, with the header that says whose it is. Nothing reaches
/// Discord.
/// </summary>
public sealed class FakeDiscord : HttpMessageHandler
{
    public const string Token = "MTIzNDU2Nzg5MDEyMzQ1Njc4.GabcDe.FakeTokenForTestsOnly_abcdefghijklmnop";

    public const long BotId = 4242;

    public const long ApplicationId = 4242;

    private readonly Lock _lock = new();
    private long _nextMessage = 5000;
    private readonly List<Call> _calls = [];

    /// <summary>One request: its method and route, its body (a file's form as its fields), and its <c>Authorization</c> header.</summary>
    public sealed record Call(string Method, string Route, JsonObject Body, string? Authorization);

    public string BotName { get; set; } = "deventry";

    /// <summary>The app's <c>integration_types_config</c> keys: an account install only, as Deventry asks.</summary>
    public string[] InstallTypes { get; set; } = ["1"];

    public string? InteractionsEndpoint { get; set; }

    /// <summary>What <c>/users/@me</c> answers with instead of the bot, such as a 401 for a refused token.</summary>
    public Func<HttpResponseMessage>? GetMeAnswer { get; set; }

    /// <summary>Discord won't open a DM with these users: the bot shares no server with them and they never wrote to it.</summary>
    public HashSet<long> RefuseDms { get; } = [];

    /// <summary>Answers for requests whose route starts so, once each, before anything else: an error, say.</summary>
    public List<(string Method, string RoutePrefix, Func<HttpResponseMessage> Answer)> Next { get; } = [];

    /// <summary>The DM channel Discord opens with a user.</summary>
    public static long DmOf(long user) => 900_000 + user;

    public IReadOnlyList<Call> Calls()
    {
        lock (_lock)
        {
            return [.. _calls];
        }
    }

    public IReadOnlyList<Call> Of(string method, string routeStart) =>
        [.. Calls().Where(c => c.Method == method && c.Route.StartsWith(routeStart, StringComparison.Ordinal))];

    /// <summary>The messages sent to channels, with their content.</summary>
    public IReadOnlyList<Call> Sent => [.. Calls().Where(c => c.Method == "POST" && c.Route.StartsWith("/channels/", StringComparison.Ordinal))];

    /// <summary>The answers to interactions: their callbacks.</summary>
    public IReadOnlyList<Call> Callbacks => [.. Calls().Where(c => c.Route.StartsWith("/interactions/", StringComparison.Ordinal))];

    public static string Content(Call call) => (string)call.Body["content"]!;

    public static long IdOf(Call sent) => (long)sent.Body["_message_id"]!;

    public static long ChannelOf(Call call) => long.Parse(call.Route.Split('/')[2], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The buttons of a message sent, row by row, as their labels and data.</summary>
    public static IReadOnlyList<(string Label, string Data)> Buttons(Call sent) =>
        sent.Body["components"] is JsonArray rows
            ? [.. rows.OfType<JsonObject>().SelectMany(r => r["components"]!.AsArray().OfType<JsonObject>()).Select(b => ((string)b["label"]!, (string)b["custom_id"]!))]
            : [];

    public static string Data(Call sent, string label) => Buttons(sent).First(b => b.Label == label).Data;

    public static HttpResponseMessage Error(HttpStatusCode status, int code, string message, double? retryAfter = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (retryAfter is { } seconds)
        {
            error["retry_after"] = seconds;
        }
        return new HttpResponseMessage(status) { Content = new StringContent(error.ToJsonString()) };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        const string prefix = "/api/v10";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Error(HttpStatusCode.NotFound, 0, "404: Not Found");
        }
        var route = path[prefix.Length..];
        var method = request.Method.Method;
        var body = await Body(request, cancellationToken);
        var auth = request.Headers.Authorization?.ToString();
        Func<HttpResponseMessage>? queued = null;
        lock (_lock)
        {
            var at = Next.FindIndex(n => n.Method == method && route.StartsWith(n.RoutePrefix, StringComparison.Ordinal));
            if (at >= 0)
            {
                queued = Next[at].Answer;
                Next.RemoveAt(at);
            }
            if (queued is null && method == "POST" && route.StartsWith("/channels/", StringComparison.Ordinal))
            {
                // The id it's answered with, for a test to reply to it.
                body["_message_id"] = ++_nextMessage;
            }
            _calls.Add(new Call(method, route, body, auth));
        }
        if (queued is not null)
        {
            return queued();
        }
        if (!route.StartsWith("/interactions/", StringComparison.Ordinal) && !route.StartsWith("/webhooks/", StringComparison.Ordinal) && auth != $"Bot {Token}")
        {
            return Error(HttpStatusCode.Unauthorized, 0, "401: Unauthorized");
        }
        return (method, route) switch
        {
            ("GET", "/users/@me") => GetMeAnswer?.Invoke() ?? Ok(new JsonObject { ["id"] = $"{BotId}", ["username"] = BotName, ["bot"] = true }),
            ("GET", "/applications/@me") => Ok(App()),
            ("GET", "/gateway/bot") => Ok(new JsonObject { ["url"] = "wss://gateway.discord.test" }),
            ("PUT", _) when route.EndsWith("/commands", StringComparison.Ordinal) => Ok(new JsonArray()),
            ("POST", "/users/@me/channels") => OpenDm(body),
            ("POST", _) when route.StartsWith("/channels/", StringComparison.Ordinal) =>
                Ok(new JsonObject { ["id"] = $"{(long)body["_message_id"]!}", ["channel_id"] = route.Split('/')[2], ["content"] = body["content"]?.DeepClone() }),
            ("POST", _) when route.StartsWith("/webhooks/", StringComparison.Ordinal) => Ok(new JsonObject { ["id"] = "1" }),
            ("PATCH", _) => Ok(new JsonObject { ["id"] = "1" }),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        };
    }

    private JsonObject App()
    {
        var config = new JsonObject();
        foreach (var type in InstallTypes)
        {
            config[type] = new JsonObject();
        }
        return new JsonObject
        {
            ["id"] = $"{ApplicationId}",
            ["name"] = "Deventry",
            ["integration_types_config"] = config,
            ["interactions_endpoint_url"] = InteractionsEndpoint,
        };
    }

    private HttpResponseMessage OpenDm(JsonObject body)
    {
        var user = long.Parse((string)body["recipient_id"]!, System.Globalization.CultureInfo.InvariantCulture);
        return RefuseDms.Contains(user)
            ? Error(HttpStatusCode.Forbidden, 50007, "Cannot send messages to this user")
            : Ok(new JsonObject { ["id"] = $"{DmOf(user)}", ["type"] = 1 });
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
                    fields["file"] = fileName.Trim('"');
                    fields["file_content"] = Encoding.UTF8.GetString(await part.ReadAsByteArrayAsync(cancellationToken));
                }
                else if (name == "payload_json" && JsonNode.Parse(await part.ReadAsStringAsync(cancellationToken)) is JsonObject payload)
                {
                    foreach (var (key, value) in payload)
                    {
                        fields[key] = value?.DeepClone();
                    }
                }
            }
            return fields;
        }
        // A list, such as the commands, is kept under "_array".
        return request.Content is null ? []
            : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken)) switch
            {
                JsonObject json => json,
                JsonArray list => new JsonObject { ["_array"] = list },
                _ => [],
            };
    }

    private static HttpResponseMessage Ok(JsonNode result) => new(HttpStatusCode.OK) { Content = new StringContent(result.ToJsonString()) };
}
