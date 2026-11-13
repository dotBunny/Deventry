using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;
using Deventry.Core.Remote.Chat;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Discord;

/// <summary>Why the Gateway stopped for good: a close code Discord won't take back, such as <c>4004</c> for a refused token.</summary>
public sealed record DiscordGatewayEnd(int Code, string Reason);

/// <summary>
/// Discord's Gateway (DESIGN.md §18, "Discord"): the WebSocket a bot keeps open to hear its DMs and interactions. It
/// says hello and identifies, beats its heart at the interval Discord gives and reconnects when Discord stops
/// answering it, resumes a session that broke off, and backs off as Telegram's polling does while Discord can't be
/// reached. Its timers are on the injected clock; the WebSocket is <see cref="IDiscordSocket"/>, which tests replace.
/// </summary>
public sealed class DiscordGateway
{
    /// <summary>Direct messages: the only events Deventry asks for, and not a privileged intent.</summary>
    public const int DirectMessages = 1 << 12;

    /// <summary>How long opening the WebSocket may take.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A close Deventry makes to come back and resume: anything but 1000 and 1001, which end the session.</summary>
    public const int ResumeClose = 4000;

    private readonly Func<IDiscordSocket> _sockets;
    private readonly TimeProvider _time;
    private readonly string _token;
    private readonly ILogger _logger;
    private readonly Func<string, JsonObject?, CancellationToken, Task> _dispatch;
    private readonly Action<string, TimeSpan> _waiting;

    private string? _sessionId;
    private string? _resumeUrl;
    private long? _sequence;
    private int _acked;

    /// <param name="dispatch">Each event, by its name (<c>READY</c>, <c>MESSAGE_CREATE</c>…), awaited before the next is read.</param>
    /// <param name="waiting">The Gateway broke off: why, and how long until it tries again.</param>
    public DiscordGateway(Func<IDiscordSocket> sockets, TimeProvider time, string token, ILogger logger, Func<string, JsonObject?, CancellationToken, Task> dispatch,
        Action<string, TimeSpan> waiting)
    {
        _sockets = sockets;
        _time = time;
        _token = token.Trim();
        _logger = logger;
        _dispatch = dispatch;
        _waiting = waiting;
    }

    /// <summary>The close codes that end it for good, and why, in words to show.</summary>
    public static string? Fatal(int? code) => code switch
    {
        4004 => "Discord didn't accept the app's token.",
        4010 or 4011 => "Discord asked for sharding, which a bot in no server shouldn't need.",
        4012 => "Discord no longer takes the Gateway version Deventry uses.",
        4013 or 4014 => "Discord refused what Deventry asked to hear.",
        _ => null,
    };

    /// <summary>
    /// Keeps the Gateway open until <paramref name="cancellationToken"/> stops it, reconnecting as it must. Returns why
    /// it stopped for good, when Discord said.
    /// </summary>
    public async Task<DiscordGatewayEnd> RunAsync(string address, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resume = _sessionId is not null;
            var socket = _sockets();
            Session end;
            try
            {
                using (var deadline = new CancellationTokenSource(ConnectTimeout, _time))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token))
                {
                    await socket.ConnectAsync(Address(resume && _resumeUrl is { } again ? again : address), linked.Token).ConfigureAwait(false);
                }
                end = await RunSessionAsync(socket, resume, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CloseQuietlyAsync(socket, 1000).ConfigureAwait(false);
                await socket.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or HttpRequestException)
            {
                end = new Session($"Couldn't reach Discord: {ex.Message}", Healthy: false);
            }
            await socket.DisposeAsync().ConfigureAwait(false);
            if (end.Fatal is { } fatal)
            {
                return fatal;
            }
            failures = end.Healthy ? 1 : failures + 1;
            var wait = end.Wait ?? ChatBotClient.Backoff(failures);
            _logger.LogInformation("Discord: {Reason} Trying again in {Wait}.", end.Reason, Formats.Duration(wait));
            // The wait starts before it's told: whoever reads the state can count on it from then on.
            var waiting = Task.Delay(wait, _time, cancellationToken);
            _waiting(end.Reason, wait);
            await waiting.ConfigureAwait(false);
        }
    }

    private static Uri Address(string gateway) => new($"{gateway.TrimEnd('/')}/?v=10&encoding=json");

    /// <summary>How a session ended: for good, or to come back after <see cref="Wait"/> (or the backoff), resuming when it can.</summary>
    /// <param name="Healthy">It got as far as <c>READY</c> or <c>RESUMED</c>: the failures start again from one.</param>
    private sealed record Session(string Reason, bool Healthy, TimeSpan? Wait = null, DiscordGatewayEnd? Fatal = null);

    private async Task<Session> RunSessionAsync(IDiscordSocket socket, bool resume, CancellationToken cancellationToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ITimer? heart = null;
        var healthy = false;
        try
        {
            while (true)
            {
                string? text;
                try
                {
                    text = await socket.ReceiveAsync(session.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The heart found Discord silent and broke the socket off.
                    return new Session("Discord stopped answering.", healthy, TimeSpan.Zero);
                }
                if (text is null)
                {
                    // 4007 (a wrong sequence) and 4009 (the session timed out) can't be resumed: the next connection identifies.
                    if (socket.CloseStatus is 4007 or 4009)
                    {
                        _sessionId = null;
                        _sequence = null;
                    }
                    return Closed(socket.CloseStatus, socket.CloseDescription, healthy);
                }
                if (Parse(text) is not { } payload)
                {
                    continue;
                }
                if (payload["s"] is JsonValue s && s.TryGetValue<long>(out var sequence))
                {
                    _sequence = sequence;
                }
                switch (payload["op"] is JsonValue op && op.TryGetValue<int>(out var code) ? code : -1)
                {
                    case 10: // Hello
                        var interval = payload["d"]?["heartbeat_interval"] is JsonValue i && i.TryGetValue<double>(out var ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(41);
                        heart?.Dispose();
                        heart = StartHeart(socket, interval, session);
                        await socket.SendAsync(resume && _sessionId is { } id ? Resume(id) : Identify(), cancellationToken).ConfigureAwait(false);
                        break;
                    case 11: // Heartbeat ACK
                        Interlocked.Exchange(ref _acked, 1);
                        break;
                    case 1: // A heartbeat asked for now
                        await socket.SendAsync(Heartbeat(), cancellationToken).ConfigureAwait(false);
                        break;
                    case 7: // Reconnect
                        await CloseQuietlyAsync(socket, ResumeClose).ConfigureAwait(false);
                        return new Session("Discord asked Deventry to reconnect.", healthy, TimeSpan.Zero);
                    case 9: // Invalid Session: resumable or not, it waits a moment as Discord asks
                        if (payload["d"] is not JsonValue resumable || resumable.GetValueKind() != JsonValueKind.True)
                        {
                            _sessionId = null;
                            _sequence = null;
                        }
                        await CloseQuietlyAsync(socket, ResumeClose).ConfigureAwait(false);
                        return new Session("Discord ended the session.", healthy, TimeSpan.FromSeconds(1 + Random.Shared.NextDouble() * 4));
                    case 0: // Dispatch
                        var name = payload.GetString("t");
                        var data = payload["d"] as JsonObject;
                        if (name == "READY" && data is not null)
                        {
                            _sessionId = data["session_id"]?.GetValue<string>();
                            _resumeUrl = data["resume_gateway_url"]?.GetValue<string>();
                            healthy = true;
                        }
                        else if (name == "RESUMED")
                        {
                            healthy = true;
                        }
                        if (name is not null)
                        {
                            await _dispatch(name, data, cancellationToken).ConfigureAwait(false);
                        }
                        break;
                }
            }
        }
        finally
        {
            heart?.Dispose();
        }
    }

    /// <summary>
    /// Beats every <paramref name="interval"/>, the first after a random part of it as Discord asks. A beat that finds
    /// the last unanswered ends the session, to resume on a new socket.
    /// </summary>
    private ITimer StartHeart(IDiscordSocket socket, TimeSpan interval, CancellationTokenSource session)
    {
        Interlocked.Exchange(ref _acked, 1);
        return _time.CreateTimer(_ =>
        {
            if (Interlocked.Exchange(ref _acked, 0) == 0)
            {
                try
                {
                    session.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The session ended as the beat came.
                }
                return;
            }
            _ = BeatAsync();
        }, null, interval * Random.Shared.NextDouble(), interval);

        async Task BeatAsync()
        {
            try
            {
                await socket.SendAsync(Heartbeat(), session.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The socket is going: the receive says why.
            }
        }
    }

    private static Session Closed(int? code, string? description, bool healthy)
    {
        if (Fatal(code) is { } reason)
        {
            return new Session(reason, healthy, Fatal: new DiscordGatewayEnd(code!.Value, reason));
        }
        var why = code is { } c ? $"Discord closed the connection ({c}{(description is { Length: > 0 } d ? $": {d}" : "")})." : "The connection to Discord broke off.";
        return new Session(why, healthy);
    }

    private string Identify() => new JsonObject
    {
        ["op"] = 2,
        ["d"] = new JsonObject
        {
            ["token"] = _token,
            ["intents"] = DirectMessages,
            ["properties"] = new JsonObject { ["os"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux", ["browser"] = "Deventry", ["device"] = "Deventry" },
        },
    }.ToJsonString();

    private string Resume(string sessionId) => new JsonObject
    {
        ["op"] = 6,
        ["d"] = new JsonObject { ["token"] = _token, ["session_id"] = sessionId, ["seq"] = _sequence },
    }.ToJsonString();

    private string Heartbeat() => new JsonObject { ["op"] = 1, ["d"] = _sequence }.ToJsonString();

    private static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task CloseQuietlyAsync(IDiscordSocket socket, int status)
    {
        try
        {
            await socket.CloseAsync(status, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Closing a socket that's broken already.
        }
    }
}
