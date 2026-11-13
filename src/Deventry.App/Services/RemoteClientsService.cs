using System.Collections.Concurrent;
using Deventry.App.ViewModels;
using Deventry.Core.Credentials;
using Deventry.Core.Remote;
using Deventry.Core.Remote.Telegram;
using Deventry.Core.Settings;
using Deventry.Usage;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>
/// The remote clients (DESIGN.md §18, "Remote clients"): the desk they reach Deventry through, over the shell's tabs,
/// and the clients themselves, one of each kind Deventry knows (<see cref="AllTypes"/>) that Settings → Remote clients
/// turns on. Each kind's secret, such as the Telegram bot's token, is only in the credential store. Decides when a
/// client sends a message: the events the user wants, loud while they're away and, with <b>Always</b>, silent at the
/// desk. The desk's methods may be called from any thread; everything else is on the UI thread.
/// </summary>
public sealed class RemoteClientsService : IRemoteDesk, IAsyncDisposable
{
    /// <summary>The kinds of remote client Deventry knows, in the order Settings lists them.</summary>
    public static IReadOnlyList<IRemoteClientType> AllTypes { get; } = [new TelegramClientType()];

    private readonly AppServices _services;
    private readonly IUserPresence _presence;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _updating = new(1, 1);
    private readonly Dictionary<string, RemotePairing> _pairings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _hasSecret = new(StringComparer.Ordinal);

    /// <summary>The clients that are on, by kind; changed as they start and stop, which may be off the UI thread.</summary>
    private readonly ConcurrentDictionary<string, IRemoteClient> _clients = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);
    private readonly UiTimeout _quietEnds;

    /// <summary>When the quiet ends, as UTC ticks; 0 for none. Clients read it on their own threads.</summary>
    private long _quietUntilTicks;

    private ShellViewModel? _shell;
    private ITimer? _presenceTimer;
    private PresenceReading _reading;
    private bool _away;
    private bool _started;
    private bool _closing;

    public RemoteClientsService(AppServices services, ICredentialStore credentials, IUserPresence presence)
    {
        _services = services;
        Credentials = credentials;
        _presence = presence;
        _logger = services.Loggers.CreateLogger("RemoteClients");
        _quietEnds = new UiTimeout(services.Time, services.Dispatcher);
        UseTypes(AllTypes);
        ClientFactory = (type, secret, pairing) => type.Create(new RemoteClientContext(services.Http, services.Time, secret, services.UserAgent, pairing,
            services.Loggers.CreateLogger(type.Name), services.State.RemoteClients.SaidClosing));
    }

    /// <summary>The kinds of client this Deventry offers: <see cref="AllTypes"/>, unless a test gives others.</summary>
    public IReadOnlyList<IRemoteClientType> Types { get; private set; } = AllTypes;

    /// <summary>The kinds of client offered, each with its pairing: <see cref="AllTypes"/>, or for tests others, before anything starts them.</summary>
    internal void UseTypes(IReadOnlyList<IRemoteClientType> types)
    {
        Types = types;
        foreach (var type in types.Where(t => !_pairings.ContainsKey(t.Kind)))
        {
            var pairing = new RemotePairing(type.Kind, _services.Time, _services.State.RemoteClients.Accounts);
            pairing.Changed += () => _services.Dispatcher.Post(() => OnPairingChanged(pairing));
            _pairings[type.Kind] = pairing;
        }
    }

    /// <summary>The credential store the clients' secrets are kept in. Tests give a fake one.</summary>
    internal ICredentialStore Credentials { get; set; }

    /// <summary>Makes a client of a kind, with its secret. Tests give a fake one, so nothing reaches the service.</summary>
    internal Func<IRemoteClientType, string, RemotePairing, IRemoteClient> ClientFactory { get; set; }

    private RemoteClientSettings Settings => _services.Settings.RemoteClients;

    /// <summary>Raised on the UI thread when anything Settings shows changes: a client's state, its name, the accounts.</summary>
    public event Action? Changed;

    /// <summary>A remote client is on: the tabs tell it what happens.</summary>
    public bool IsActive => !_clients.IsEmpty;

    public IRemoteClientType Type(string kind) => Types.First(t => t.Kind == kind);

    /// <summary>Pairing a phone with the client of <paramref name="kind"/>, and the accounts paired with it.</summary>
    public RemotePairing PairingOf(string kind) => _pairings[kind];

    /// <summary>The client of <paramref name="kind"/>, while it's on.</summary>
    public IRemoteClient? ClientOf(string kind) => _clients.GetValueOrDefault(kind);

    /// <summary>The client of <paramref name="kind"/> has its secret in the credential store; null until it's been read.</summary>
    public bool? HasSecret(string kind) => _hasSecret.TryGetValue(kind, out var has) ? has : null;

    /// <summary>Where the client of <paramref name="kind"/> stands, for Settings and Diagnostics.</summary>
    public RemoteClientState StateOf(string kind) => ClientOf(kind)?.State
        ?? (Settings.IsEnabled(kind) && HasSecret(kind) == false
            ? new RemoteClientState(RemoteClientStatus.Failed, $"Set its {Type(kind).SecretName} to start it.")
            : RemoteClientState.Off);

    /// <summary>The client's own account's name, once its service has said: the bot's <c>@username</c>, without the @.</summary>
    public string? IdentityOf(string kind) => _services.State.RemoteClients.Identities.GetValueOrDefault(kind);

    /// <summary>When the client of <paramref name="kind"/> last sent the user a message, this run; null before it has.</summary>
    public DateTimeOffset? LastSent(string kind) => _lastSent.TryGetValue(kind, out var at) ? at : null;

    /// <summary>
    /// Raised on the UI thread when a client sends the user a message or gets one from a paired account: the header's
    /// indicator lights up.
    /// </summary>
    public event Action<string, RemoteActivity>? ActivityHappened;

    private void OnActivity(string kind, RemoteActivity activity)
    {
        if (activity == RemoteActivity.Sent)
        {
            _lastSent[kind] = _services.Time.GetUtcNow();
        }
        ActivityHappened?.Invoke(kind, activity);
    }

    // ---- Quiet --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Until when the clients send no messages but answers to what the user did from them: <c>/quiet</c>, or the header's
    /// <b>Quiet for an hour</b>. Null when they send; kept only while Deventry runs.
    /// </summary>
    public DateTimeOffset? QuietUntil =>
        Volatile.Read(ref _quietUntilTicks) is var ticks and not 0 && new DateTimeOffset(ticks, TimeSpan.Zero) is var until && until > _services.Time.GetUtcNow()
            ? until
            : null;

    /// <summary>Holds messages until <paramref name="until"/>, or lets them go again with null. From any thread.</summary>
    public void SetQuiet(DateTimeOffset? until)
    {
        Volatile.Write(ref _quietUntilTicks, until?.UtcTicks ?? 0);
        _services.Dispatcher.Post(() =>
        {
            // The header says when it ends, and once it has.
            if (QuietUntil is { } end)
            {
                _quietEnds.Restart(end - _services.Time.GetUtcNow(), () => Changed?.Invoke());
            }
            else
            {
                _quietEnds.Cancel();
            }
            Changed?.Invoke();
        });
    }

    /// <summary>What this desktop can tell about the user being away, for Settings.</summary>
    public PresenceSignals PresenceSignals => _presence.Signals;

    /// <summary>The user is away from the desk, as Settings → Remote clients counts it.</summary>
    public bool IsAway => _away;

    // ---- Starting and stopping --------------------------------------------------------------------------------------

    /// <summary>The tabs are up: the clients Settings turns on start.</summary>
    public void Start(ShellViewModel shell)
    {
        _shell = shell;
        _started = true;
        _ = UpdateAsync();
    }

    /// <summary>Settings changed: a client turned on or off starts or stops, and keeping the computer awake follows.</summary>
    public void OnSettingsChanged()
    {
        _ = UpdateAsync();
        UpdateAway();
    }

    /// <summary>For tests: starts or stops the clients as Settings says, and waits for it.</summary>
    internal Task UpdateAsync() => UpdateAsync(restart: null);

    /// <param name="restart">A kind whose client starts again, with a new secret, if it's on.</param>
    private async Task UpdateAsync(string? restart)
    {
        await _updating.WaitAsync().ConfigureAwait(true);
        try
        {
            foreach (var type in Types)
            {
                var wanted = _started && !_closing && Settings.IsEnabled(type.Kind);
                if (_clients.ContainsKey(type.Kind) && (!wanted || restart == type.Kind))
                {
                    await StopClientAsync(type).ConfigureAwait(true);
                }
                if (wanted && !_clients.ContainsKey(type.Kind))
                {
                    await StartClientAsync(type).ConfigureAwait(true);
                }
            }
            if (_clients.IsEmpty)
            {
                StopPresence();
            }
        }
        finally
        {
            _updating.Release();
            _services.Dispatcher.Post(() =>
            {
                _services.KeepAwake.SetRemoteClientOn(IsActive);
                UpdateAway();
                Changed?.Invoke();
            });
        }
    }

    /// <summary>Tells Settings, on the UI thread, that something it shows changed.</summary>
    private void NotifyChanged() => _services.Dispatcher.Post(() => Changed?.Invoke());

    /// <summary>
    /// Where the secret of the client of <paramref name="kind"/> is in the credential store: its kind's key, then this data
    /// folder's id, so the demo or another Deventry on this computer, with a data folder of its own, never reads it or
    /// competes for its bot.
    /// </summary>
    internal string CredentialKeyOf(string kind) => $"{Type(kind).CredentialKey}-{_services.MachineId}";

    private async Task<string?> ReadSecretAsync(IRemoteClientType type)
    {
        string? secret;
        try
        {
            secret = await Credentials.ReadAsync(CredentialKeyOf(type.Kind)).ConfigureAwait(true);
        }
        catch (CredentialStoreException ex)
        {
            _logger.LogWarning("Couldn't read {Client}'s {Secret}: {Error}", type.Name, type.SecretName, ex.Message);
            secret = null;
        }
        _hasSecret[type.Kind] = secret is { Length: > 0 };
        return secret is { Length: > 0 } ? secret : null;
    }

    private async Task StartClientAsync(IRemoteClientType type)
    {
        if (await ReadSecretAsync(type).ConfigureAwait(true) is not { } secret)
        {
            return;
        }
        var client = ClientFactory(type, secret, PairingOf(type.Kind));
        client.StateChanged += _ => NotifyChanged();
        client.Identified += name => _services.Dispatcher.Post(() => OnIdentified(type.Kind, name));
        client.Activity += activity => _services.Dispatcher.Post(() => OnActivity(type.Kind, activity));
        _clients[type.Kind] = client;
        if (_services.State.RemoteClients.SaidClosing)
        {
            _services.State.RemoteClients.SaidClosing = false;
            _services.SaveState();
        }
        StartPresence();
        await client.StartAsync(this).ConfigureAwait(true);
        _logger.LogInformation("{Client}: started.", type.Name);
    }

    private async Task StopClientAsync(IRemoteClientType type)
    {
        if (!_clients.TryRemove(type.Kind, out var client))
        {
            return;
        }
        await client.DisposeAsync().ConfigureAwait(true);
        _logger.LogInformation("{Client}: stopped.", type.Name);
    }

    /// <summary>
    /// <b>Set token…</b>: keeps the client's secret in the credential store and starts the client with it, if it's on.
    /// One that can't be its secret is refused.
    /// </summary>
    /// <exception cref="ArgumentException">It can't be the client's secret: the message says why.</exception>
    /// <exception cref="CredentialStoreException">It couldn't be kept.</exception>
    public async Task SetSecretAsync(string kind, string secret)
    {
        var type = Type(kind);
        if (type.CheckSecret(secret.Trim()) is { } problem)
        {
            throw new ArgumentException(problem, nameof(secret));
        }
        await Credentials.WriteAsync(CredentialKeyOf(kind), type.CredentialLabel, secret.Trim()).ConfigureAwait(true);
        _hasSecret[kind] = true;
        // Another account, maybe: its name comes with the client.
        _services.State.RemoteClients.Identities.Remove(kind);
        _services.SaveState();
        await UpdateAsync(restart: kind).ConfigureAwait(true);
    }

    /// <summary><b>Remove…</b>: the secret leaves the credential store, and the client stops.</summary>
    /// <exception cref="CredentialStoreException">It couldn't be removed.</exception>
    public async Task RemoveSecretAsync(string kind)
    {
        await Credentials.DeleteAsync(CredentialKeyOf(kind)).ConfigureAwait(true);
        _hasSecret[kind] = false;
        _services.State.RemoteClients.Identities.Remove(kind);
        _services.SaveState();
        await UpdateAsync(restart: kind).ConfigureAwait(true);
    }

    /// <summary>What the client's state offers to do about why it can't connect, such as Telegram's <b>Use it for Deventry anyway</b>.</summary>
    public async Task ApplyFixAsync(string kind)
    {
        if (StateOf(kind).Fix is { } fix)
        {
            await fix.ApplyAsync(CancellationToken.None).ConfigureAwait(true);
            NotifyChanged();
        }
    }

    /// <summary>Reads whether a client has its secret, for Settings before it's turned on.</summary>
    public async Task CheckSecretAsync(string kind)
    {
        await ReadSecretAsync(Type(kind)).ConfigureAwait(true);
        NotifyChanged();
    }

    private void OnIdentified(string kind, string name)
    {
        if (IdentityOf(kind) != name)
        {
            _services.State.RemoteClients.Identities[kind] = name;
            _services.SaveState();
            Changed?.Invoke();
        }
    }

    private void OnPairingChanged(RemotePairing pairing)
    {
        var state = _services.State.RemoteClients;
        state.Accounts.RemoveAll(a => a.Client == pairing.Client);
        state.Accounts.AddRange(pairing.Accounts);
        _services.SaveState();
        Changed?.Invoke();
    }

    /// <summary>
    /// Deventry is closing: the clients say so, and stop once it's sent. They say, silently, that it's back when it
    /// starts again. A crash sends nothing.
    /// </summary>
    public async Task CloseAsync()
    {
        _closing = true;
        if (_clients.Values.Any(c => c.State.Status == RemoteClientStatus.Connected))
        {
            Raise(new RemoteDeskClosing { Notice = _away ? RemoteNotice.Sound : RemoteNotice.Silent });
            _services.State.RemoteClients.SaidClosing = true;
            _services.SaveState();
        }
        await UpdateAsync().ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        foreach (var type in Types)
        {
            await StopClientAsync(type).ConfigureAwait(true);
        }
        StopPresence();
        _presence.Dispose();
        _quietEnds.Dispose();
    }

    // ---- Away from the desk -----------------------------------------------------------------------------------------

    /// <summary>Deventry's window came to the front or went to the back: the first thing that counts as away.</summary>
    public void SetAppActive(bool active)
    {
        UpdateAway();
        if (!active && IsActive)
        {
            // Locked or idle may have come with it.
            _ = ReadPresenceAsync();
        }
    }

    private void StartPresence()
    {
        if (_presenceTimer is not null)
        {
            return;
        }
        _presence.Changed += OnPresenceChanged;
        _presence.Start();
        _presenceTimer ??= _services.Time.CreateTimer(_ => _ = ReadPresenceAsync(), null, TimeSpan.Zero, RemoteAway.ReadInterval);
    }

    private void StopPresence()
    {
        _presence.Changed -= OnPresenceChanged;
        _presence.Stop();
        _presenceTimer?.Dispose();
        _presenceTimer = null;
        _reading = PresenceReading.Unknown;
    }

    private void OnPresenceChanged() => _ = ReadPresenceAsync();

    /// <summary>For tests: reads presence now and waits until the desk has it.</summary>
    internal async Task ReadPresenceAsync()
    {
        PresenceReading reading;
        try
        {
            reading = await _presence.ReadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Couldn't read whether anyone's at the computer: {Error}", ex.Message);
            return;
        }
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _services.Dispatcher.Post(() =>
        {
            if (IsActive)
            {
                _reading = reading;
                UpdateAway();
            }
            applied.TrySetResult();
        });
        await applied.Task.ConfigureAwait(false);
    }

    /// <summary>Works out whether the user is away; on becoming away, the prompts already waiting are sent.</summary>
    private void UpdateAway()
    {
        var away = RemoteAway.IsAway(_services.Notifications.IsAppActive, _reading, RemoteAway.After(Settings.AwayAfterMinutes));
        if (away == _away)
        {
            return;
        }
        _away = away;
        Changed?.Invoke();
        if (!away || !IsActive || _shell is null)
        {
            return;
        }
        foreach (var (tab, project) in Tabs())
        {
            foreach (var prompt in tab.RemotePrompts())
            {
                Raise(new RemotePromptWaiting(tab.ToRemote(project), prompt) { Notice = NoticeFor(Settings.NeedsInput), Again = true });
            }
        }
    }

    private RemoteNotice NoticeFor(bool wanted) => RemoteAway.Notice(wanted, _away, Settings.When);

    // ---- What the tabs tell the desk --------------------------------------------------------------------------------

    public event Action<RemoteEvent>? EventRaised;

    private void Raise(RemoteEvent raised)
    {
        try
        {
            EventRaised?.Invoke(raised);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A remote client failed with an event.");
        }
    }

    /// <summary>The tabs, each with its group's name, in the sidebar's order.</summary>
    private IEnumerable<(TabViewModel Tab, string Project)> Tabs() =>
        _shell is null ? [] : _shell.Groups.SelectMany(g => g.Tabs.Select(t => (t, g.Label)));

    private string ProjectOf(TabViewModel tab) =>
        _shell?.Groups.FirstOrDefault(g => g.Tabs.Contains(tab)) is { } group ? group.Label : tab.FolderName;

    internal void OnTabChanged(TabViewModel tab)
    {
        if (IsActive)
        {
            Raise(new RemoteTabChanged(tab.ToRemote(ProjectOf(tab), withPrompt: false)));
        }
    }

    internal void OnTabClosed(TabViewModel tab)
    {
        if (IsActive)
        {
            Raise(new RemoteTabClosed(tab.Id, tab.DisplayName));
        }
    }

    internal void OnPromptWaiting(TabViewModel tab, RemotePrompt prompt)
    {
        if (IsActive)
        {
            Raise(new RemotePromptWaiting(tab.ToRemote(ProjectOf(tab)), prompt) { Notice = NoticeFor(Settings.NeedsInput) });
        }
    }

    internal void OnPromptClosed(TabViewModel tab, string promptId, RemotePromptEnd how, string outcome, string? client)
    {
        tab.RememberClosed(promptId, how, client);
        if (IsActive)
        {
            Raise(new RemotePromptClosed(tab.Id, promptId, how, outcome, client, _services.Time.GetUtcNow()));
        }
    }

    internal void OnTurnFinished(TabViewModel tab, TimeSpan? took, string? tasks, string? reply, IReadOnlyList<RemoteFileChange> files)
    {
        if (IsActive)
        {
            Raise(new RemoteTurnFinished(tab.ToRemote(ProjectOf(tab)), took, tasks, reply, files) { Notice = NoticeFor(Settings.TurnFinished) });
        }
    }

    internal void OnTabFailed(TabViewModel tab, string message, bool needsSignIn)
    {
        if (IsActive)
        {
            Raise(new RemoteTabFailed(tab.ToRemote(ProjectOf(tab)), message, needsSignIn) { Notice = NoticeFor(Settings.ProcessErrors) });
        }
    }

    /// <summary>An app-wide notification: a usage alert, an account's spend warning, or a project action's end.</summary>
    internal void OnAppNotification(NotificationKind kind, string title, string body, string? tabId)
    {
        if (!IsActive)
        {
            return;
        }
        if (kind == NotificationKind.UsageAlert)
        {
            Raise(new RemoteUsageAlert(title, body) { Notice = NoticeFor(Settings.UsageAlerts) });
        }
        else if (kind == NotificationKind.ProjectAction)
        {
            Raise(new RemoteProjectAction(tabId, title, body) { Notice = NoticeFor(Settings.ProjectActions) });
        }
    }

    // ---- The desk ---------------------------------------------------------------------------------------------------

    public string MachineName => _services.Library.MachineName;

    public RemoteAccessLevel Access => Settings.Access;

    public bool CanOpenTabs => Settings.OpenTabs;

    public RemoteDetail Detail => Settings.Detail;

    public Task<IReadOnlyList<RemoteTab>> GetTabsAsync(CancellationToken cancellationToken = default) =>
        OnUiAsync<IReadOnlyList<RemoteTab>>(() => [.. Tabs().Select(t => t.Tab.ToRemote(t.Project))], cancellationToken);

    public Task<RemoteResult> SendAsync(string tabId, string text, string client, CancellationToken cancellationToken = default) =>
        WithTabAsync(tabId, RemoteAction.Send, tab => tab.SendRemoteAsync(text, client), cancellationToken);

    public Task<RemoteResult> StopAsync(string tabId, string client, CancellationToken cancellationToken = default) =>
        WithTabAsync(tabId, RemoteAction.Stop, tab => tab.StopRemoteAsync(), cancellationToken);

    public Task<RemoteResult> AnswerAsync(string tabId, string promptId, string optionId, string? text, string client, CancellationToken cancellationToken = default) =>
        WithTabAsync(tabId, RemoteAction.Answer, tab => Task.FromResult(tab.AnswerRemote(promptId, optionId, text, client)), cancellationToken);

    public Task<RemoteResult> RestartAsync(string tabId, string client, CancellationToken cancellationToken = default) =>
        WithTabAsync(tabId, RemoteAction.Restart, tab => Task.FromResult(tab.RestartRemote()), cancellationToken);

    public Task<RemoteReply?> GetLastReplyAsync(string tabId, CancellationToken cancellationToken = default) =>
        OnUiAsync(() => Find(tabId)?.LastRemoteReply(), cancellationToken);

    public Task<IReadOnlyList<RemoteFileChange>?> GetChangedFilesAsync(string tabId, CancellationToken cancellationToken = default) =>
        OnUiAsync(() => Find(tabId)?.RemoteChangedFiles(), cancellationToken);

    public async Task<RemoteUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var (snapshot, accounts, tracker) = await OnUiAsync(() => (_services.Usage?.Current, _services.Accounts.All, _services.Usage), cancellationToken).ConfigureAwait(false);
        var meters = snapshot is null ? [] : snapshot.Limits.Select(l => new RemoteUsageMeter(l.Kind switch
        {
            LimitKind.Session => "Session",
            LimitKind.WeeklyAll => "Week",
            _ => $"Week, {l.Label}",
        }, l.Percent, l.ResetsAt)).ToList();
        var spend = new List<RemoteSpend>();
        if (tracker is not null)
        {
            var today = SpendPeriods.StartOfDay(_services.Time.GetLocalNow());
            foreach (var account in accounts)
            {
                try
                {
                    var spent = await Task.Run(() => tracker.Store.GetSpend(account.Id, today, account.Name), cancellationToken).ConfigureAwait(false);
                    spend.Add(new RemoteSpend(account.Name, Core.Formats.Dollars(spent)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogInformation("Couldn't read {Account}'s spend for a remote client: {Error}", account.Name, ex.Message);
                }
            }
        }
        return new RemoteUsage(meters, spend);
    }

    public Task<IReadOnlyList<RemoteFolder>> GetFoldersAsync(CancellationToken cancellationToken = default) =>
        OnUiAsync<IReadOnlyList<RemoteFolder>>(() =>
        {
            var favorites = _services.State.FavoriteFolders.Where(Directory.Exists).Select(f => new RemoteFolder(f, Core.Formats.FolderName(f), true));
            var recent = _services.State.RecentFolders.Select(r => r.Path)
                .Where(p => Directory.Exists(p) && !_services.State.FavoriteFolders.Contains(p, StringComparer.OrdinalIgnoreCase))
                .Select(p => new RemoteFolder(p, Core.Formats.FolderName(p), false));
            return [.. favorites.Concat(recent)];
        }, cancellationToken);

    public async Task<RemoteResult> OpenTabAsync(string folder, string client, CancellationToken cancellationToken = default)
    {
        if (!RemoteAccess.Allows(Access, CanOpenTabs, RemoteAction.OpenTab))
        {
            return RemoteResult.No(RemoteAccess.Refusal(RemoteAction.OpenTab));
        }
        var known = await GetFoldersAsync(cancellationToken).ConfigureAwait(false);
        // Only a favorite or recent folder: a client can't name any folder on the computer.
        if (!known.Any(f => string.Equals(f.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            return RemoteResult.No("That folder isn't a favorite or a recent one any more.");
        }
        return await OnUiAsync(() =>
        {
            if (_shell is not { } shell)
            {
                return RemoteResult.No("Deventry isn't ready yet.");
            }
            TabViewModel? opened = null;
            // As the folder picker opens one: it starts, and asks about trust at the desk if the folder needs it.
            _ = shell.OpenFolderAsync(folder, tab => opened = tab);
            return opened is null ? RemoteResult.No("That folder isn't there any more.") : new RemoteResult(true, null, opened.Id);
        }, cancellationToken).ConfigureAwait(false);
    }

    private TabViewModel? Find(string tabId) => _shell?.AllTabs.FirstOrDefault(t => t.Id == tabId);

    /// <summary>Does <paramref name="work"/> to a tab on the UI thread, if remote clients may, and says why not otherwise.</summary>
    private Task<RemoteResult> WithTabAsync(string tabId, RemoteAction action, Func<TabViewModel, Task<RemoteResult>> work, CancellationToken cancellationToken)
    {
        if (!RemoteAccess.Allows(Access, CanOpenTabs, action))
        {
            return Task.FromResult(RemoteResult.No(RemoteAccess.Refusal(action)));
        }
        return OnUiAsync(async () => Find(tabId) is { } tab ? await work(tab).ConfigureAwait(true) : RemoteResult.No(RemoteMessages.TabGone), cancellationToken);
    }

    private Task<T> OnUiAsync<T>(Func<T> work, CancellationToken cancellationToken) => OnUiAsync(() => Task.FromResult(work()), cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread and gives back its result. A client that stops waiting is let go,
    /// though the work still runs once the UI thread comes to it.
    /// </summary>
    private Task<T> OnUiAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
            _ = done.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        _services.Dispatcher.Post(async () =>
        {
            try
            {
                done.TrySetResult(await work().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return done.Task;
    }

    // ---- Diagnostics ------------------------------------------------------------------------------------------------

    /// <summary>For Settings → Advanced → Diagnostics: each client's state and accounts, without a secret or any message.</summary>
    public string Describe()
    {
        var on = Types.Where(t => Settings.IsEnabled(t.Kind)).ToList();
        if (on.Count == 0)
        {
            return "Remote clients: none is on.";
        }
        var clients = on.Select(type =>
        {
            var state = StateOf(type.Kind);
            var accounts = PairingOf(type.Kind).Accounts.Count switch
            {
                0 => "no account paired",
                1 => "1 account paired",
                var n => $"{n} accounts paired",
            };
            var identity = IdentityOf(type.Kind) is { } name ? type.ShowIdentity(name) : "its account not yet known";
            var reason = state.Reason is { } why ? $": {why}" : "";
            return $"{type.Name}, {identity}, {state.Status}{reason}; {accounts}";
        });
        return $"Remote clients: {string.Join(". ", clients)}; {(_away ? "away" : "at the desk")}.";
    }
}
