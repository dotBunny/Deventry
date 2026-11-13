using Deventry.Core.Remote.Chat;
using Deventry.Core.Remote.Discord;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Remote;

/// <summary>
/// Discord's Gateway (DESIGN.md §18, "Discord"), against a fake one on the fake clock: identifying, the heart, a silent
/// Discord, reconnecting and resuming, the sessions that can't be resumed, the codes that end it, and the backoff.
/// </summary>
public sealed class DiscordGatewayTests : IAsyncDisposable
{
    private const string Address = "wss://gateway.discord.test";

    private readonly FakeDiscordGateway _discord = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero));
    private readonly List<string> _events = [];
    private readonly List<(string Reason, TimeSpan Wait)> _waits = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly DiscordGateway _gateway;
    private Task<DiscordGatewayEnd>? _run;

    public DiscordGatewayTests()
    {
        _gateway = new DiscordGateway(_discord.Create, _clock, FakeDiscord.Token, NullLogger.Instance,
            (name, _, _) =>
            {
                lock (_events)
                {
                    _events.Add(name);
                }
                return Task.CompletedTask;
            },
            (reason, wait) =>
            {
                lock (_waits)
                {
                    _waits.Add((reason, wait));
                }
            });
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is { } run)
        {
            await ((Task)run).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        _stop.Dispose();
    }

    private IReadOnlyList<string> Events()
    {
        lock (_events)
        {
            return [.. _events];
        }
    }

    private IReadOnlyList<(string Reason, TimeSpan Wait)> Waits()
    {
        lock (_waits)
        {
            return [.. _waits];
        }
    }

    private async Task RunAsync()
    {
        _run = Task.Run(() => _gateway.RunAsync(Address, _stop.Token), TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => Events().Contains("READY"), "READY");
    }

    [Fact]
    public async Task Hello_is_answered_with_an_identify_for_direct_messages_only()
    {
        await RunAsync();

        var identify = Assert.Single(_discord.Op(2));
        Assert.Equal(FakeDiscord.Token, (string)identify["d"]!["token"]!);
        Assert.Equal(DiscordGateway.DirectMessages, (int)identify["d"]!["intents"]!);
        Assert.Equal(new Uri("wss://gateway.discord.test/?v=10&encoding=json"), _discord.Current.Address);
    }

    [Fact]
    public async Task The_heart_beats_at_Discords_interval_with_the_last_sequence()
    {
        await RunAsync();

        _clock.Advance(_discord.HeartbeatInterval);

        await Waiting.UntilAsync(() => _discord.Op(1).Count >= 1, "a heartbeat");
        Assert.Equal(_discord.Sequence, (long)_discord.Op(1)[0]["d"]!);
        var beats = _discord.Op(1).Count;
        // The next beat checks the last was answered: an event after the answer, read in order, says it's been read.
        _discord.Dispatch("MARK", new System.Text.Json.Nodes.JsonObject());
        await Waiting.UntilAsync(() => Events().Contains("MARK"), "the heartbeat's answer read");
        _clock.Advance(_discord.HeartbeatInterval);
        await Waiting.UntilAsync(() => _discord.Op(1).Count == beats + 1, "the next heartbeat");
    }

    [Fact]
    public async Task A_silent_Discord_is_left_and_the_session_resumed_on_a_new_socket()
    {
        _discord.AcknowledgeHeartbeats = false;
        await RunAsync();

        // The first beat goes unanswered; the next finds it so.
        _clock.Advance(_discord.HeartbeatInterval);
        _clock.Advance(_discord.HeartbeatInterval);

        await Waiting.UntilAsync(() => _discord.Op(6).Count == 1, "the resume");
        var resume = _discord.Op(6)[0]["d"]!;
        Assert.Equal("session-1", (string)resume["session_id"]!);
        Assert.Equal(FakeDiscord.Token, (string)resume["token"]!);
        Assert.Equal(2, _discord.Sockets.Count);
        Assert.StartsWith("wss://resume.discord.test/", _discord.Current.Address!.ToString(), StringComparison.Ordinal);
        Assert.Single(_discord.Op(2));
    }

    [Fact]
    public async Task Reconnect_asked_for_resumes_at_once_closing_so_the_session_lasts()
    {
        await RunAsync();
        var first = _discord.Current;

        first.Push(new System.Text.Json.Nodes.JsonObject { ["op"] = 7, ["d"] = null });

        await Waiting.UntilAsync(() => Events().Contains("RESUMED"), "the resume");
        Assert.Equal(DiscordGateway.ResumeClose, first.ClosedWith);
        Assert.True(first.Disposed);
        Assert.Single(_discord.Op(6));
    }

    [Fact]
    public async Task An_invalid_session_identifies_again_after_a_moment()
    {
        await RunAsync();

        _discord.Current.Push(new System.Text.Json.Nodes.JsonObject { ["op"] = 9, ["d"] = false });

        await Waiting.UntilAsync(() => Waits().Count == 1, "the wait");
        Assert.InRange(Waits()[0].Wait, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        _clock.Advance(TimeSpan.FromSeconds(5));
        await Waiting.UntilAsync(() => _discord.Op(2).Count == 2, "a new identify");
        Assert.Empty(_discord.Op(6));
    }

    [Fact]
    public async Task A_refused_token_ends_it_for_good()
    {
        await RunAsync();

        _discord.Current.CloseFromDiscord(4004, "Authentication failed.");

        var end = await _run!.WaitAsync(Waiting.Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(4004, end.Code);
        Assert.Single(_discord.Sockets);
    }

    [Fact]
    public async Task A_timed_out_session_identifies_rather_than_resumes()
    {
        await RunAsync();

        _discord.Current.CloseFromDiscord(4009, "Session timed out.");

        await Waiting.UntilAsync(() => Waits().Count == 1, "the wait");
        Assert.Equal(ChatBotClient.Backoff(1), Waits()[0].Wait);
        _clock.Advance(Waits()[0].Wait);
        await Waiting.UntilAsync(() => _discord.Op(2).Count == 2, "a new identify");
        Assert.Empty(_discord.Op(6));
    }

    [Fact]
    public async Task A_connection_that_fails_backs_off_5_seconds_then_30()
    {
        _discord.FailConnects = 2;
        _run = Task.Run(() => _gateway.RunAsync(Address, _stop.Token), TestContext.Current.CancellationToken);

        await Waiting.UntilAsync(() => Waits().Count == 1, "the first wait");
        Assert.Equal(TimeSpan.FromSeconds(5), Waits()[0].Wait);
        Assert.StartsWith("Couldn't reach Discord", Waits()[0].Reason, StringComparison.Ordinal);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await Waiting.UntilAsync(() => Waits().Count == 2, "the second wait");
        Assert.Equal(TimeSpan.FromSeconds(30), Waits()[1].Wait);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await Waiting.UntilAsync(() => Events().Contains("READY"), "READY");
    }

    [Fact]
    public async Task Events_are_handed_on_in_order()
    {
        await RunAsync();

        _discord.Dispatch("MESSAGE_CREATE", new System.Text.Json.Nodes.JsonObject());
        _discord.Dispatch("INTERACTION_CREATE", new System.Text.Json.Nodes.JsonObject());

        await Waiting.UntilAsync(() => Events().Count == 3, "the events");
        Assert.Equal(["READY", "MESSAGE_CREATE", "INTERACTION_CREATE"], Events());
    }

    [Fact]
    public async Task Stopping_closes_the_session_for_good()
    {
        await RunAsync();

        await _stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _run!.WaitAsync(Waiting.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(1000, _discord.Current.ClosedWith);
    }
}
