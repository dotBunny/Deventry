using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Deventry.Core.Remote.Discord;

namespace Deventry.Core.Tests.Support;

/// <summary>
/// Discord's Gateway, played here: each socket the client opens says hello, answers an identify with <c>READY</c> and
/// a resume with <c>RESUMED</c>, and acknowledges heartbeats, unless the test says otherwise; the test sends events and
/// closes sockets as Discord would. Everything the client sends is kept.
/// </summary>
public sealed class FakeDiscordGateway
{
    private readonly Lock _lock = new();
    private readonly List<FakeDiscordSocket> _sockets = [];
    private readonly List<JsonObject> _received = [];
    private long _sequence;

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(41.25);

    /// <summary>Answers heartbeats; off, Discord goes silent and the client should reconnect.</summary>
    public bool AcknowledgeHeartbeats { get; set; } = true;

    /// <summary>Opening a socket fails with this many errors first.</summary>
    public int FailConnects { get; set; }

    public string SessionId { get; set; } = "session-1";

    public long BotId { get; set; } = FakeDiscord.BotId;

    public IDiscordSocket Create()
    {
        var socket = new FakeDiscordSocket(this);
        lock (_lock)
        {
            _sockets.Add(socket);
        }
        return socket;
    }

    public IReadOnlyList<FakeDiscordSocket> Sockets
    {
        get
        {
            lock (_lock)
            {
                return [.. _sockets];
            }
        }
    }

    public FakeDiscordSocket Current => Sockets[^1];

    /// <summary>Everything the client sent, on every socket.</summary>
    public IReadOnlyList<JsonObject> Received
    {
        get
        {
            lock (_lock)
            {
                return [.. _received];
            }
        }
    }

    public IReadOnlyList<JsonObject> Op(int op) => [.. Received.Where(p => (int)p["op"]! == op)];

    public long Sequence => Interlocked.Read(ref _sequence);

    /// <summary>An event, on the socket open now.</summary>
    public void Dispatch(string type, JsonNode? data) =>
        Current.Push(new JsonObject { ["op"] = 0, ["t"] = type, ["s"] = Interlocked.Increment(ref _sequence), ["d"] = data });

    /// <summary>A message to the bot in its DM.</summary>
    public long Message(long from, string content, long? replyTo = null, long? guild = null, bool attachment = false)
    {
        var id = Interlocked.Increment(ref _sequence) + 70_000;
        var message = new JsonObject
        {
            ["id"] = $"{id}",
            ["channel_id"] = $"{FakeDiscord.DmOf(from)}",
            ["author"] = User(from),
            ["content"] = content,
            ["timestamp"] = "2026-11-12T14:00:00.000000+00:00",
            ["attachments"] = attachment ? new JsonArray(new JsonObject { ["id"] = "1" }) : new JsonArray(),
        };
        if (replyTo is { } reply)
        {
            message["message_reference"] = new JsonObject { ["message_id"] = $"{reply}", ["channel_id"] = $"{FakeDiscord.DmOf(from)}" };
        }
        if (guild is { } server)
        {
            message["guild_id"] = $"{server}";
        }
        Dispatch("MESSAGE_CREATE", message);
        return id;
    }

    /// <summary>A slash command, in the bot's DM unless <paramref name="context"/> says another chat.</summary>
    public string Command(long from, string name, string? argument = null, int context = 1)
    {
        var token = $"itoken-{Interlocked.Increment(ref _sequence)}";
        var data = new JsonObject { ["id"] = "1", ["name"] = name, ["type"] = 1 };
        if (argument is not null)
        {
            data["options"] = new JsonArray(new JsonObject { ["name"] = "x", ["type"] = 3, ["value"] = argument });
        }
        Dispatch("INTERACTION_CREATE", Interaction(from, 2, token, context, data, message: null));
        return token;
    }

    /// <summary>A tap on a button of the bot's message <paramref name="message"/>, in its DM.</summary>
    public string Tap(long from, string customId, long message)
    {
        var token = $"itoken-{Interlocked.Increment(ref _sequence)}";
        Dispatch("INTERACTION_CREATE", Interaction(from, 3, token, 1, new JsonObject { ["custom_id"] = customId, ["component_type"] = 2 }, message));
        return token;
    }

    private static JsonObject Interaction(long from, int type, string token, int context, JsonObject data, long? message)
    {
        var interaction = new JsonObject
        {
            ["id"] = $"{Random.Shared.NextInt64(1, long.MaxValue)}",
            ["application_id"] = $"{FakeDiscord.ApplicationId}",
            ["type"] = type,
            ["token"] = token,
            ["context"] = context,
            ["channel_id"] = context == 1 ? $"{FakeDiscord.DmOf(from)}" : "31337",
            ["data"] = data,
            ["authorizing_integration_owners"] = new JsonObject { ["1"] = $"{from}" },
        };
        if (context == 0)
        {
            interaction["guild_id"] = "777";
            interaction["member"] = new JsonObject { ["user"] = User(from) };
        }
        else
        {
            interaction["user"] = User(from);
        }
        if (message is { } id)
        {
            interaction["message"] = new JsonObject { ["id"] = $"{id}", ["channel_id"] = $"{FakeDiscord.DmOf(from)}" };
        }
        return interaction;
    }

    public static JsonObject User(long id) => new() { ["id"] = $"{id}", ["username"] = $"user{id}", ["global_name"] = $"User {id}" };

    internal void Receive(FakeDiscordSocket socket, JsonObject payload)
    {
        lock (_lock)
        {
            _received.Add(payload);
        }
        switch ((int)payload["op"]!)
        {
            case 2:
                socket.Push(new JsonObject
                {
                    ["op"] = 0,
                    ["t"] = "READY",
                    ["s"] = Interlocked.Increment(ref _sequence),
                    ["d"] = new JsonObject
                    {
                        ["v"] = 10,
                        ["user"] = new JsonObject { ["id"] = $"{BotId}", ["username"] = "deventry", ["bot"] = true },
                        ["guilds"] = new JsonArray(),
                        ["session_id"] = SessionId,
                        ["resume_gateway_url"] = "wss://resume.discord.test",
                        ["application"] = new JsonObject { ["id"] = $"{FakeDiscord.ApplicationId}" },
                    },
                });
                break;
            case 6:
                socket.Push(new JsonObject { ["op"] = 0, ["t"] = "RESUMED", ["s"] = Interlocked.Increment(ref _sequence), ["d"] = null });
                break;
            case 1 when AcknowledgeHeartbeats:
                socket.Push(new JsonObject { ["op"] = 11 });
                break;
        }
    }

    internal bool TakeConnectFailure()
    {
        lock (_lock)
        {
            if (FailConnects <= 0)
            {
                return false;
            }
            FailConnects--;
            return true;
        }
    }
}

/// <summary>One socket to the fake Gateway: what the client receives is queued here.</summary>
public sealed class FakeDiscordSocket(FakeDiscordGateway gateway) : IDiscordSocket
{
    private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();

    public Uri? Address { get; private set; }

    /// <summary>The code the client closed it with; null while it hasn't.</summary>
    public int? ClosedWith { get; private set; }

    public int? CloseStatus { get; private set; }

    public string? CloseDescription { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>What the client hasn't read yet: none once it has taken everything sent.</summary>
    public int Pending => _inbound.Reader.Count;

    public Task ConnectAsync(Uri address, CancellationToken cancellationToken)
    {
        Address = address;
        if (gateway.TakeConnectFailure())
        {
            throw new WebSocketException("The remote party closed the WebSocket connection without completing the close handshake.");
        }
        Push(new JsonObject { ["op"] = 10, ["d"] = new JsonObject { ["heartbeat_interval"] = gateway.HeartbeatInterval.TotalMilliseconds } });
        return Task.CompletedTask;
    }

    public Task SendAsync(string text, CancellationToken cancellationToken)
    {
        if (ClosedWith is not null || CloseStatus is not null)
        {
            throw new WebSocketException("The socket is closed.");
        }
        gateway.Receive(this, (JsonObject)JsonNode.Parse(text)!);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) => await _inbound.Reader.ReadAsync(cancellationToken);

    public Task CloseAsync(int status, CancellationToken cancellationToken)
    {
        ClosedWith ??= status;
        _inbound.Writer.TryWrite(null);
        return Task.CompletedTask;
    }

    /// <summary>Discord closes it, with its code.</summary>
    public void CloseFromDiscord(int status, string? description = null)
    {
        CloseStatus = status;
        CloseDescription = description;
        _inbound.Writer.TryWrite(null);
    }

    public void Push(JsonObject payload) => _inbound.Writer.TryWrite(payload.ToJsonString());

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
