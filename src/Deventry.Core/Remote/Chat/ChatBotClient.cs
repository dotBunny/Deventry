using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Chat;

/// <summary>How a chat bot paces itself. Tests take the waits out.</summary>
public record ChatBotOptions
{
    /// <summary>
    /// The least time between two messages or edits: Telegram allows about one a second to a chat, and Discord five in
    /// five seconds to a channel.
    /// </summary>
    public TimeSpan MessageSpacing { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// A remote client that's a bot in a chat service (DESIGN.md §18, "Remote clients"): Telegram's and Discord's. It
/// answers only the paired accounts, in private chats; sends what happens at the desk as messages with buttons; sends
/// the user's replies to their tabs; and keeps a tab's live status. Each service says how it listens, how its messages
/// go and how they're written. One instance per start: the App makes a new one each time a client is turned on or its
/// secret changes.
/// </summary>
/// <remarks>
/// Everything it does with its own state runs on one loop, in order: what the service brings, the desk's events and
/// its timers each post their work there, so nothing in it needs a lock.
/// </remarks>
public abstract partial class ChatBotClient : IRemoteClient
{
    /// <summary>How long stopping waits for what's being sent, such as the closing message.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly RemotePairing _pairing;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ChatBotOptions _options;
    private readonly Channel<Func<CancellationToken, Task>> _work = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _listening = new();
    private readonly CancellationTokenSource _sending = new();

    /// <summary>This run's id, which its buttons carry: one from another run is stale.</summary>
    private readonly string _run = RandomNumberGenerator.GetString("abcdefghijkmnpqrstuvwxyz23456789", 4);

    private IRemoteDesk? _desk;
    private Task? _listenTask;
    private Task? _workTask;
    private int _started;

    protected ChatBotClient(RemotePairing pairing, TimeProvider time, ILogger logger, ChatBotOptions? options)
    {
        _pairing = pairing;
        _time = time;
        _logger = logger;
        _options = options ?? new ChatBotOptions();
    }

    public abstract string Kind { get; }

    public abstract string Name { get; }

    public RemoteClientState State { get; private set; } = RemoteClientState.Off;

    public event Action<RemoteClientState>? StateChanged;

    /// <summary>Its own account's name in its service, once the service has said.</summary>
    public string? Identity { get; private set; }

    /// <summary>Raised on the listening thread when the service says what the bot is called: Settings shows it, and the App keeps it.</summary>
    public event Action<string>? Identified;

    /// <summary>Raised on the work loop as it sends a message or a file, or takes one from a paired account.</summary>
    public event Action<RemoteActivity>? Activity;

    /// <summary>Says, silently, that Deventry is back, once it's listening: it said it was closing last time.</summary>
    public bool AnnounceBack { get; init; }

    protected RemotePairing Pairing => _pairing;

    protected TimeProvider Time => _time;

    protected ILogger Logger => _logger;

    /// <summary>How the service writes messages.</summary>
    protected abstract ChatFormat Format { get; }

    /// <summary>The command an account that isn't paired sends its code with: Telegram's <c>start</c>, Discord's <c>pair</c>.</summary>
    protected abstract string PairCommand { get; }

    /// <summary>Listens to the service until <paramref name="cancellationToken"/> stops it, saying how it goes with <see cref="SetState"/>.</summary>
    protected abstract Task ListenAsync(CancellationToken cancellationToken);

    /// <summary>Stopping has begun: whatever listening waits on besides its token stops waiting.</summary>
    protected virtual void OnStopping()
    {
    }

    /// <summary>For tests: the loop the work runs on, to wait for what's been posted.</summary>
    internal Task WhenIdleAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            done.TrySetResult();
            return Task.CompletedTask;
        });
        return done.Task;
    }

    public Task StartAsync(IRemoteDesk desk, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException($"A {Name} client starts once; make another to start again.");
        }
        _desk = desk;
        desk.EventRaised += OnDeskEvent;
        SetState(new RemoteClientState(RemoteClientStatus.Connecting));
        _workTask = Task.Run(() => RunWorkAsync(_sending.Token), CancellationToken.None);
        _listenTask = Task.Run(() => RunListeningAsync(_listening.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_desk is { } desk)
        {
            desk.EventRaised -= OnDeskEvent;
        }
        await _listening.CancelAsync().ConfigureAwait(false);
        OnStopping();
        _work.Writer.TryComplete();
        if (_listenTask is { } listen)
        {
            await listen.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        if (_workTask is { } work)
        {
            // What's being sent, such as the closing message, has a moment to go.
            try
            {
                await work.WaitAsync(StopTimeout, _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // What it's sending is cut off; work waiting on something that doesn't hear the cancel is left behind.
                await _sending.CancelAsync().ConfigureAwait(false);
                try
                {
                    await work.WaitAsync(StopTimeout, _time).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("{Client}: stopped without waiting for what it was sending.", Name);
                }
            }
        }
        // The loop has ended: nothing else touches the timers now.
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }
        _timers.Clear();
        SetState(RemoteClientState.Off);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _listening.Dispose();
        _sending.Dispose();
        GC.SuppressFinalize(this);
    }

    protected void SetState(RemoteClientState state)
    {
        if (state == State)
        {
            return;
        }
        State = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>The service said what the bot is called.</summary>
    protected void SetIdentity(string identity)
    {
        Identity = identity;
        Identified?.Invoke(identity);
    }

    private async Task RunListeningAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ListenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Client}: listening stopped.", Name);
            SetState(new RemoteClientState(RemoteClientStatus.Failed, $"Deventry stopped listening to {Name}: {ex.Message}"));
        }
    }

    /// <summary>After <paramref name="failures"/> failures in a row: 5 seconds, 30, a minute, then every 5 minutes.</summary>
    public static TimeSpan Backoff(int failures) => failures switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        3 => TimeSpan.FromMinutes(1),
        _ => TimeSpan.FromMinutes(5),
    };

    // ---- The work loop ----------------------------------------------------------------------------------------------

    /// <summary>Runs <paramref name="work"/> on the loop, after what's there already.</summary>
    protected void Post(Func<CancellationToken, Task> work) => _work.Writer.TryWrite(work);

    private async Task RunWorkAsync(CancellationToken cancellationToken)
    {
        await foreach (var work in _work.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                await work(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One message that can't go never stops the others.
                _logger.LogWarning("{Client}: {Error}", Name, ex is ChatApiException ? ex.Message : ex.ToString());
            }
        }
    }

    private DateTimeOffset _nextSend;

    /// <summary>Keeps to the service's pace, and waits out a <c>429</c> once before giving up on a message.</summary>
    private async Task<T> PacedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var wait = _nextSend - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
            }
            try
            {
                return await call(cancellationToken).ConfigureAwait(false);
            }
            catch (ChatApiException ex) when (ex.IsTooManyRequests && attempt == 0)
            {
                _nextSend = _time.GetUtcNow() + (ex.RetryAfter ?? TimeSpan.FromSeconds(5));
            }
            finally
            {
                if (_nextSend < _time.GetUtcNow() + _options.MessageSpacing)
                {
                    _nextSend = _time.GetUtcNow() + _options.MessageSpacing;
                }
            }
        }
    }

    private Task PacedAsync(Func<CancellationToken, Task> call, CancellationToken cancellationToken) =>
        PacedAsync(async token =>
        {
            await call(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    // ---- Timers -----------------------------------------------------------------------------------------------------

    private readonly Dictionary<string, ITimer> _timers = new(StringComparer.Ordinal);

    /// <summary>Runs <paramref name="work"/> on the loop after <paramref name="delay"/>, replacing any timer of the same name.</summary>
    private void After(string name, TimeSpan delay, Func<CancellationToken, Task> work)
    {
        if (_timers.Remove(name, out var old))
        {
            old.Dispose();
        }
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => Post(token =>
        {
            if (_timers.TryGetValue(name, out var current) && ReferenceEquals(current, timer))
            {
                _timers.Remove(name);
                current.Dispose();
                return work(token);
            }
            return Task.CompletedTask;
        }), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timers[name] = timer;
        timer.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }
}
