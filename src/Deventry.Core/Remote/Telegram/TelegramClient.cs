using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Remote.Telegram;

/// <summary>How the Telegram client paces itself. Tests take the waits out.</summary>
public sealed record TelegramClientOptions
{
    /// <summary>The least time between two messages or edits: Telegram allows about one a second to a chat.</summary>
    public TimeSpan MessageSpacing { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long Telegram holds a poll waiting for an update.</summary>
    public int PollSeconds { get; init; } = TelegramBotApi.PollSeconds;
}

/// <summary>
/// The Telegram bot (DESIGN.md §18, "Remote clients", "Telegram"): long-polls <c>getUpdates</c>, so there's no server
/// or open port; answers only the paired accounts, in private chats; sends what happens at the desk as messages with
/// buttons; and sends the user's replies to their tabs. One instance per start: the App makes a new one each time the
/// bot is turned on or its token changes.
/// </summary>
/// <remarks>
/// Everything it does with its own state runs on one loop, in order: the updates the poll brings, the desk's events
/// and its timers each post their work there, so nothing in it needs a lock.
/// </remarks>
public sealed partial class TelegramClient : IRemoteClient
{
    public const string ClientKind = "telegram";

    public const string ClientName = "Telegram";

    /// <summary>Messages sent this long before Deventry started listening are passed over.</summary>
    public static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(2);

    /// <summary>How long it waits after Telegram says another program polls the bot.</summary>
    public static readonly TimeSpan ConflictRetry = TimeSpan.FromMinutes(1);

    /// <summary>How long stopping waits for what's being sent, such as the closing message.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public const string TokenRefused = "Telegram didn't accept the bot's token. Set it again in Settings → Remote clients.";

    public const string AnotherProgram = "Another Deventry, or another program, is using this bot. Trying again each minute.";

    private readonly TelegramBotApi _api;
    private readonly RemotePairing _pairing;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TelegramClientOptions _options;
    private readonly Channel<Func<CancellationToken, Task>> _work = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _polling = new();
    private readonly CancellationTokenSource _sending = new();
    private readonly TaskCompletionSource _useWebhookAnyway = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>This run's id, which its buttons carry: one from another run is stale.</summary>
    private readonly string _run = RandomNumberGenerator.GetString("abcdefghijkmnpqrstuvwxyz23456789", 4);

    private IRemoteDesk? _desk;
    private Task? _pollTask;
    private Task? _workTask;
    private int _started;

    public TelegramClient(TelegramBotApi api, RemotePairing pairing, TimeProvider time, ILogger logger, TelegramClientOptions? options = null)
    {
        _api = api;
        _pairing = pairing;
        _time = time;
        _logger = logger;
        _options = options ?? new TelegramClientOptions();
    }

    public string Kind => ClientKind;

    public string Name => ClientName;

    public RemoteClientState State { get; private set; } = RemoteClientState.Off;

    public event Action<RemoteClientState>? StateChanged;

    /// <summary>The bot's <c>@username</c>, without the @, once <c>getMe</c> has said it.</summary>
    public string? Identity { get; private set; }

    /// <summary>Raised on the poll's thread when <c>getMe</c> says who the bot is: Settings shows it, and the App keeps it.</summary>
    public event Action<string>? Identified;

    /// <summary>The web address the bot sends its updates to, which keeps it from being polled; null when it has none.</summary>
    public string? Webhook { get; private set; }

    /// <summary>Says, silently, that Deventry is back, once it's listening: it said it was closing last time.</summary>
    public bool AnnounceBack { get; init; }

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
            throw new InvalidOperationException("A Telegram client starts once; make another to start again.");
        }
        _desk = desk;
        desk.EventRaised += OnDeskEvent;
        SetState(new RemoteClientState(RemoteClientStatus.Connecting));
        _workTask = Task.Run(() => RunWorkAsync(_sending.Token), CancellationToken.None);
        _pollTask = Task.Run(() => PollAsync(_polling.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <b>Use it for Deventry anyway</b>: takes the bot's webhook away, so it can be polled. Deventry never does by
    /// itself.
    /// </summary>
    public async Task UseWebhookAnywayAsync(CancellationToken cancellationToken = default)
    {
        await _api.DeleteWebhookAsync(cancellationToken).ConfigureAwait(false);
        Webhook = null;
        _useWebhookAnyway.TrySetResult();
    }

    public async Task StopAsync()
    {
        if (_desk is { } desk)
        {
            desk.EventRaised -= OnDeskEvent;
        }
        await _polling.CancelAsync().ConfigureAwait(false);
        _useWebhookAnyway.TrySetCanceled();
        _work.Writer.TryComplete();
        if (_pollTask is { } poll)
        {
            await poll.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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
                    _logger.LogWarning("Telegram: stopped without waiting for what it was sending.");
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
        _polling.Dispose();
        _sending.Dispose();
    }

    private void SetState(RemoteClientState state)
    {
        if (state == State)
        {
            return;
        }
        State = state;
        StateChanged?.Invoke(state);
    }

    // ---- Polling ----------------------------------------------------------------------------------------------------

    /// <summary>After <paramref name="failures"/> failures in a row: 5 seconds, 30, a minute, then every 5 minutes.</summary>
    public static TimeSpan Backoff(int failures) => failures switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        3 => TimeSpan.FromMinutes(1),
        _ => TimeSpan.FromMinutes(5),
    };

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await IdentifyAsync(cancellationToken).ConfigureAwait(false) || !await CheckWebhookAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            try
            {
                await _api.SetCommandsAsync(Commands, cancellationToken).ConfigureAwait(false);
            }
            catch (TelegramApiException ex)
            {
                _logger.LogInformation("Telegram: couldn't set the bot's commands: {Error}", ex.Message);
            }
            var since = _time.GetUtcNow();
            if (AnnounceBack)
            {
                Post(token => BroadcastAsync(TelegramFormat.Escape(RemoteMessages.Back(Desk.MachineName)), silent: true, cancellationToken: token));
            }
            await ListenAsync(since - StaleAge, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram: polling stopped.");
            SetState(new RemoteClientState(RemoteClientStatus.Failed, $"Deventry stopped listening to the bot: {ex.Message}"));
        }
    }

    /// <summary><c>getMe</c>, until it answers. False when the token was refused.</summary>
    private async Task<bool> IdentifyAsync(CancellationToken cancellationToken)
    {
        for (var failures = 1; ; failures++)
        {
            try
            {
                var me = await _api.GetMeAsync(cancellationToken).ConfigureAwait(false);
                if (me.Username is { Length: > 0 } username)
                {
                    Identity = username;
                    Identified?.Invoke(username);
                }
                return true;
            }
            catch (TelegramApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return false;
            }
            catch (TelegramApiException ex)
            {
                await WaitAfterAsync(ex, failures, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A bot with a webhook can't be polled: it waits for <b>Use it for Deventry anyway</b>. False when stopped meanwhile.</summary>
    private async Task<bool> CheckWebhookAsync(CancellationToken cancellationToken)
    {
        for (var failures = 1; ; failures++)
        {
            try
            {
                Webhook = await _api.GetWebhookAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (TelegramApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return false;
            }
            catch (TelegramApiException ex)
            {
                await WaitAfterAsync(ex, failures, cancellationToken).ConfigureAwait(false);
            }
        }
        if (Webhook is null)
        {
            return true;
        }
        SetState(new RemoteClientState(RemoteClientStatus.Failed, "This bot sends its updates to a web address, so Deventry can't listen to it.")
        {
            Fix = new RemoteClientFix("Use it for Deventry anyway",
                $"Its updates go to {Webhook}. Using it for Deventry takes that away, and whatever reads it there stops getting them.", UseWebhookAnywayAsync),
        });
        try
        {
            await _useWebhookAnyway.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        SetState(new RemoteClientState(RemoteClientStatus.Connecting));
        return true;
    }

    private async Task ListenAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        long? offset = null;
        var first = true;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<TelegramUpdate> updates;
            try
            {
                updates = await _api.GetUpdatesAsync(offset, _options.PollSeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TelegramApiException ex) when (ex.IsUnauthorized)
            {
                SetState(new RemoteClientState(RemoteClientStatus.Failed, TokenRefused));
                return;
            }
            catch (TelegramApiException ex)
            {
                failures++;
                await WaitAfterAsync(ex, failures, cancellationToken).ConfigureAwait(false);
                continue;
            }
            failures = 0;
            SetState(RemoteClientState.Connected);
            foreach (var update in updates)
            {
                offset = Math.Max(offset ?? 0, update.UpdateId + 1);
                var stale = update.Message is { } message && message.Date < staleBefore;
                Post(token => HandleUpdateAsync(update, stale, token));
            }
            if (first)
            {
                first = false;
                // Once the updates kept while Deventry was closed are in, it says how many it passed over.
                Post(ReportPassedOverAsync);
            }
        }
    }

    /// <summary>
    /// Waits after a failed request: a <c>429</c>'s <c>retry_after</c>, a minute for another program polling the bot, or
    /// the backoff, saying why meanwhile.
    /// </summary>
    private async Task WaitAfterAsync(TelegramApiException ex, int failures, CancellationToken cancellationToken)
    {
        TimeSpan wait;
        if (ex.IsTooManyRequests)
        {
            wait = ex.RetryAfter ?? Backoff(failures);
        }
        else if (ex.IsConflict)
        {
            SetState(new RemoteClientState(RemoteClientStatus.Connecting, AnotherProgram));
            wait = ConflictRetry;
        }
        else
        {
            SetState(new RemoteClientState(RemoteClientStatus.Connecting, ex.Message));
            wait = Backoff(failures);
        }
        _logger.LogInformation("Telegram: {Error} Trying again in {Wait}.", ex.Message, Formats.Duration(wait));
        await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
    }

    // ---- The work loop ----------------------------------------------------------------------------------------------

    private void Post(Func<CancellationToken, Task> work) => _work.Writer.TryWrite(work);

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
                _logger.LogWarning("Telegram: {Error}", ex is TelegramApiException ? ex.Message : ex.ToString());
            }
        }
    }

    private DateTimeOffset _nextSend;

    /// <summary>Keeps to about a message a second, and waits out a <c>429</c> once before giving up on a message.</summary>
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
            catch (TelegramApiException ex) when (ex.IsTooManyRequests && attempt == 0)
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
