using System.Collections.Concurrent;
using Deventry.Core;
using Deventry.Core.Files;
using Deventry.Core.Git;
using Deventry.Core.Library;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using Deventry.Usage;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>
/// App-wide plan usage (DESIGN.md §6): polls <c>get_usage</c> through the utility session, takes
/// <c>rate_limit_event</c> updates from any tab, records samples and per-turn tokens in the usage history, and prunes
/// it at launch and once a day. With sharing on, it also shares the samples through the session library and imports
/// the other machines' ("Sharing across machines").
/// </summary>
public sealed class UsageTracker : IAsyncDisposable
{
    /// <summary>How often the other machines' shared usage is read, as settings sync reads its file.</summary>
    public static readonly TimeSpan ShareInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan UsageCommandTimeout = TimeSpan.FromSeconds(30);

    private readonly AppServices _services;
    private readonly UsagePoller _poller;
    private readonly ITimer _pruneTimer;
    private readonly ITimer _shareTimer;
    private readonly ILogger _logger;
    private readonly Lock _writeLock = new();
    private readonly SemaphoreSlim _sharing = new(1, 1);
    private readonly string _machineId;
    private Task _historyWrites = Task.CompletedTask;
    private bool _shares;
    private string? _sharedTo;
    // Machines whose file a later Deventry wrote in a format this one can't read, logged once each. Used under _sharing.
    private readonly HashSet<string> _newerFormats = new(StringComparer.Ordinal);
    // Synced sessions' usage files read since the library was chosen, as they were then, so unchanged ones aren't read
    // again. Used under _sharing.
    private readonly Dictionary<string, (long Length, DateTime LastWrite)> _sessionFilesRead = new(StringComparer.Ordinal);
    private string? _sessionFilesOf;
    // Each folder's project key and name, read from its git files once.
    private readonly ConcurrentDictionary<string, (string Key, string Name)> _projects = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    public UsageTracker(AppServices services, UsageStore store)
    {
        _services = services;
        Store = store;
        _logger = services.Loggers.CreateLogger<UsageTracker>();
        _poller = new UsagePoller(GetUsageAsync, services.Time, _logger, RunUsageCommandAsync)
        {
            UseUsageCommandFallback = services.Settings.Usage.UseUsageCommandFallback,
        };
        _poller.Updated += OnPolled;
        _pruneTimer = services.Time.CreateTimer(_ => Prune(), null, TimeSpan.Zero, TimeSpan.FromDays(1));
        if (Store.GetLatestSample() is { } latest)
        {
            // The header shows the last known values straight away after a restart, and new readings merge with them.
            Current = latest.ToSnapshot();
            _poller.Restore(Current);
        }
        _machineId = services.MachineId;
        // Turns recorded before projects had keys take their folder's, so a project's turns are one row.
        WriteHistory(() =>
        {
            try
            {
                Store.BackfillProjectKeys(folder => Directory.Exists(folder) ? ProjectOf(folder) : null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't give earlier turns their project's key.");
            }
        });
        _shares = services.Settings.Usage.ShareThroughLibrary;
        services.SettingsChanged += OnServicesSettingsChanged;
        // At launch, then once a minute.
        _shareTimer = services.Time.CreateTimer(_ => _ = ShareAsync(), null, TimeSpan.Zero, ShareInterval);
    }

    public UsageStore Store { get; }

    /// <summary>The latest plan usage. Only read on the UI thread.</summary>
    public UsageSnapshot? Current { get; private set; }

    /// <summary>False when <c>get_usage</c> doesn't work and the <c>/usage</c> fallback is off: no model-specific meters.</summary>
    public bool ModelLimitsAvailable => _poller.ModelLimitsAvailable;

    /// <summary>Raised on the UI thread when plan usage changes.</summary>
    public event Action<UsageSnapshot>? Updated;

    /// <summary>Raised on the UI thread after a tab's turn was recorded, with the tab's id.</summary>
    public event Action<string>? TurnRecorded;

    /// <summary>
    /// Raised on the UI thread after a turn of an account other than Default was recorded, with the account's id: its
    /// spend changed (DESIGN.md §4, "Accounts").
    /// </summary>
    public event Action<string>? SpendRecorded;

    /// <summary>Raised on the UI thread after readings another machine shared were added to the history.</summary>
    public event Action? SamplesImported;

    /// <summary>
    /// Raised on the UI thread after turns other machines ran in synced sessions were added to the history (DESIGN.md §6,
    /// "Sharing across machines"), with the names of the API-key accounts they ran under, whose spend changed.
    /// </summary>
    public event Action<IReadOnlyCollection<string>>? TurnsImported;

    public void Start() => _poller.Start();

    public void OnSettingsChanged()
    {
        _poller.UseUsageCommandFallback = _services.Settings.Usage.UseUsageCommandFallback;
        // Turned on: share straight away rather than at the next reading.
        var shares = _services.Settings.Usage.ShareThroughLibrary;
        if (shares && !_shares)
        {
            _ = Task.Run(() => ShareAsync());
        }
        _shares = shares;
    }

    private void OnServicesSettingsChanged(object? sender, EventArgs e) => OnSettingsChanged();

    /// <summary>
    /// Settings → Usage → Share usage with my other machines (DESIGN.md §6, "Sharing across machines"): writes this
    /// machine's readings of the last week to its file in the library when they changed or the library moved, and adds
    /// the readings other machines signed in to the same account shared to the history, and the turns they ran in synced
    /// sessions.
    /// </summary>
    /// <param name="publish">This machine has a new reading to share.</param>
    public async Task ShareAsync(bool publish = false)
    {
        var account = _services.RemoteControl.Account;
        if (_disposed || !_services.Settings.Usage.ShareThroughLibrary || UsageSharing.AccountKey(account?.Email, account?.OrganizationName) is not { } key)
        {
            return;
        }
        var library = _services.Library.Library;
        var machineName = _services.Library.MachineName;
        var keep = _services.Settings.Usage.KeepHistory.ToTimeSpan();
        await _sharing.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }
            await ApplyClearedEverywhereAsync(library).ConfigureAwait(false);
            var now = _services.Time.GetUtcNow();
            if (publish || _sharedTo != library.LibraryFolder)
            {
                var json = UsageSharing.Write(machineName, _services.AppVersion.ToString(), key, now, Store.GetOwnSamples(now - UsageSharing.SharedPeriod));
                await library.WriteSharedUsageAsync(_machineId, json).ConfigureAwait(false);
                _sharedTo = library.LibraryFolder;
            }
            var imported = 0;
            foreach (var (machineId, text) in await Task.Run(() => library.ReadSharedUsage(_machineId)).ConfigureAwait(false))
            {
                // Another account's readings would say nothing about this one's limits.
                if (UsageSharing.Read(text) is not { } shared || shared.Account != key)
                {
                    continue;
                }
                try
                {
                    imported += Import(machineId, shared, keep, now);
                }
                catch (Exception ex) when (ex is not ObjectDisposedException)
                {
                    // One machine's file must not keep the others' out.
                    _logger.LogWarning(ex, "Couldn't add the usage {Machine} shared.", shared.MachineName);
                }
            }
            if (imported > 0)
            {
                var latest = Store.GetLatestSample()?.ToSnapshot();
                _services.Dispatcher.Post(() =>
                {
                    // The header shows the newest reading any machine took, as it does after a restart. Samples don't
                    // keep usage credits, so this machine's last word on them stays.
                    if (latest is not null && (Current is null || latest.AsOf > Current.AsOf))
                    {
                        Current = latest with { Credits = Current?.Credits, CreditsReport = Current?.CreditsReport };
                        _poller.Restore(latest);
                    }
                    SamplesImported?.Invoke();
                });
            }
            var turns = await Task.Run(() => ImportSessionTurns(library, key, keep)).ConfigureAwait(false);
            if (turns.Count > 0)
            {
                IReadOnlyCollection<string> accounts = turns.Where(t => t.AccountId is not null && t.Account is not null)
                    .Select(t => t.Account!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _services.Dispatcher.Post(() => TurnsImported?.Invoke(accounts));
            }
        }
        catch (Exception ex)
        {
            // The library folder may be unavailable for now (a sync client not running, a drive not mounted).
            _logger.LogWarning(ex, "Couldn't share usage through the session library.");
        }
        finally
        {
            _sharing.Release();
        }
    }

    /// <summary>
    /// <b>Clear on every machine</b> (DESIGN.md §6, "Clear usage history"), after this machine's history was cleared at
    /// <paramref name="at"/>: deletes the usage every machine shared in the library, and leaves a note there that the
    /// others clear theirs by. Returns false when the library couldn't be reached.
    /// </summary>
    public async Task<bool> ClearEverywhereAsync(DateTimeOffset at, bool resetTabTotals)
    {
        var library = _services.Library.Library;
        var note = UsageSharing.WriteCleared(new UsageCleared(at, _services.Library.MachineName, resetTabTotals));
        await _sharing.WaitAsync().ConfigureAwait(false);
        try
        {
            var deleted = await library.ClearSharedUsageAsync(note).ConfigureAwait(false);
            _logger.LogInformation("Cleared usage history on every machine: deleted {Count} shared usage files from the session library.", deleted);
            // This machine's own file is written again, with only what comes after.
            _sharedTo = null;
            _sessionFilesRead.Clear();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't clear the usage shared in the session library.");
            return false;
        }
        finally
        {
            _sharing.Release();
        }
    }

    /// <summary>
    /// Another machine cleared usage history on every machine since this one last cleared its own: clears this machine's
    /// up to the same moment, keeping what came after, and its tabs' token totals if that machine reset its own. Called
    /// under _sharing.
    /// </summary>
    private async Task ApplyClearedEverywhereAsync(SessionLibrary library)
    {
        if (await Task.Run(library.ReadUsageCleared).ConfigureAwait(false) is not { } text || UsageSharing.ReadCleared(text) is not { } cleared
            || cleared.At <= (Store.ClearedAt ?? DateTimeOffset.MinValue))
        {
            return;
        }
        var deleted = await Task.Run(() => Store.ClearThrough(cleared.At)).ConfigureAwait(false);
        _logger.LogInformation("{Machine} cleared usage history on every machine at {At}: deleted {Count} samples and turn records here.", cleared.MachineName, cleared.At, deleted);
        _sharedTo = null;
        _sessionFilesRead.Clear();
        var latest = Store.GetLatestSample()?.ToSnapshot();
        _services.Dispatcher.Post(() =>
        {
            if (latest is not null)
            {
                Current = latest with { Credits = Current?.Credits, CreditsReport = Current?.CreditsReport };
                _poller.Restore(latest);
            }
            _services.NotifyUsageHistoryCleared(cleared.ResetTabTotals);
        });
    }

    /// <summary>
    /// Adds the turns other machines ran in synced sessions to the history, from each session's usage file that changed
    /// since it was last read (DESIGN.md §6, "Sharing across machines"). The Default account's turns count only from
    /// machines signed in to the same plan account (<paramref name="plan"/>), as its readings do; an API-key account's
    /// always do, and match this machine's account of the same name. Returns the turns added. Called under _sharing.
    /// </summary>
    private List<SharedTurn> ImportSessionTurns(SessionLibrary library, string plan, TimeSpan? keep)
    {
        if (_sessionFilesOf != library.LibraryFolder)
        {
            _sessionFilesRead.Clear();
            _sessionFilesOf = library.LibraryFolder;
        }
        var added = new List<SharedTurn>();
        foreach (var file in library.SessionUsageFiles())
        {
            if (_sessionFilesRead.TryGetValue(file.Path, out var read) && read == (file.Length, file.LastWriteUtc))
            {
                continue;
            }
            try
            {
                if (SessionUsageFile.Read(AtomicFile.ReadAllText(file.Path)) is not { } usage)
                {
                    _logger.LogWarning("Skipped {Path}: it isn't a session's usage file.", file.Path);
                }
                else if (usage.IsNewerFormat)
                {
                    _logger.LogWarning("Left out the turns in {Path}: a later Deventry wrote it in format {Version}, and this version reads up to {Known}. Update Deventry here to include them.",
                        file.Path, usage.Version, SessionUsageFile.FileVersion);
                }
                else
                {
                    var theirs = usage.Turns.Where(t => t.Machine != _machineId && (t.AccountId is not null || t.Plan == plan));
                    added.AddRange(Store.ImportTurns(file.SessionId, theirs, usage.Machines, usage.Tabs, keep));
                }
                _sessionFilesRead[file.Path] = (file.Length, file.LastWriteUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Mid-sync, perhaps: read it again next time.
            }
        }
        return added;
    }

    /// <summary>
    /// What a synced session's usage file in the library should hold (DESIGN.md §6, "Sharing across machines"): the turns
    /// this machine has for it merged with the library's, so no machine's are lost. Null while sharing is off, when the
    /// library's file already has them all, or when a later Deventry wrote it in a format this one can't read.
    /// </summary>
    /// <remarks>
    /// After the turns waiting to be recorded, so the turn that just ended is in it. Nothing from before the last clear goes
    /// in, and nothing at all while another machine's <b>Clear on every machine</b> is still to be applied here.
    /// </remarks>
    public Task<string?> SessionUsageForLibraryAsync(string sessionId, SessionUsageTexts library)
    {
        if (!_services.Settings.Usage.ShareThroughLibrary)
        {
            return Task.FromResult<string?>(null);
        }
        var machineName = _services.Library.MachineName;
        var notes = _services.Library.Library;
        return AfterHistoryWrites(() =>
        {
            var floor = Store.ClearedAt ?? DateTimeOffset.MinValue;
            if (notes.ReadUsageCleared() is { } note && UsageSharing.ReadCleared(note) is { } cleared && cleared.At > floor)
            {
                return null;
            }
            var main = library.Main is { } text ? SessionUsageFile.Read(text) : null;
            var copies = library.Copies.Select(SessionUsageFile.Read).OfType<SessionUsage>().ToArray();
            if (main?.IsNewerFormat == true || copies.Any(c => c.IsNewerFormat))
            {
                return null;
            }
            var inMain = (main?.Turns ?? []).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var merged = copies.Aggregate(main ?? SessionUsage.Empty, SessionUsageFile.Merge);
            merged = SessionUsageFile.Merge(merged, Store.GetSessionUsage(sessionId, _machineId, machineName));
            merged = merged with { Turns = [.. merged.Turns.Where(t => t.At > floor)] };
            return merged.Turns.All(t => inMain.Contains(t.Id)) && merged.Turns.Count == (main?.Turns.Count ?? 0) ? null : SessionUsageFile.Write(merged);
        });
    }

    /// <summary>Adds one machine's shared readings to the history; returns how many were new.</summary>
    private int Import(string machineId, SharedUsage shared, TimeSpan? keep, DateTimeOffset now)
    {
        if (shared.IsNewerFormat)
        {
            if (_newerFormats.Add(machineId))
            {
                _logger.LogWarning("Left out the usage {Machine} shared: Deventry {WrittenBy} wrote it in format {Version}, and this version reads up to {Known}. Update Deventry here to include it.",
                    shared.MachineName, shared.WrittenBy ?? "(unknown)", shared.Version, UsageSharing.FileVersion);
            }
            return 0;
        }
        _newerFormats.Remove(machineId);
        return Store.ImportSamples(machineId, keep is { } k ? shared.Samples.Where(sample => sample.Timestamp >= now - k) : shared.Samples);
    }

    /// <summary>A tab finished a turn: record its tokens and its name, and poll soon (at most once a minute).</summary>
    /// <param name="turn">The turn, with what it spent (<see cref="TurnCompleted.Usage"/>).</param>
    /// <param name="project">The tab's folder, for usage by project.</param>
    /// <param name="account">The account the tab ran under (DESIGN.md §4, "Accounts"); null for the Default account.</param>
    /// <param name="accountName">That account's name, which other machines match their own accounts by.</param>
    public void OnTurnCompleted(string tabId, string tabName, TurnCompleted turn, string? project = null, string? account = null, string? accountName = null)
    {
        var records = TurnRecord.From(turn.Usage, turn.Result.SessionId, tabId, _services.Time.GetUtcNow(), project, account);
        // Which plan the Default account's turn used, so only machines on the same one count it (DESIGN.md §6).
        var plan = account is null && _services.RemoteControl.Account is { } signedIn ? UsageSharing.AccountKey(signedIn.Email, signedIn.OrganizationName) : null;
        if (records.Count > 0)
        {
            WriteHistory(() =>
            {
                try
                {
                    var (projectKey, projectName) = project is null ? default : ProjectOf(project);
                    Store.AddTurns(records.Select(r => r with { ProjectKey = projectKey, ProjectName = projectName, AccountName = accountName?.Trim(), Plan = plan }));
                    Store.SetTabName(tabId, tabName);
                    _services.Dispatcher.Post(() =>
                    {
                        TurnRecorded?.Invoke(tabId);
                        if (account is not null)
                        {
                            SpendRecorded?.Invoke(account);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Couldn't record a turn's tokens.");
                }
            });
        }
        _poller.NotifyTurnCompleted();
    }

    /// <summary>A tab's name changed: the history keeps the latest, so the tab's row is still named once it's closed.</summary>
    public void OnTabRenamed(string tabId, string tabName) =>
        WriteHistory(() =>
        {
            try
            {
                Store.SetTabName(tabId, tabName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't record a tab's name.");
            }
        });

    /// <summary>
    /// Writes turns and names off the UI thread, one at a time in the order they came, so a name can't land before the
    /// turn it belongs to, or an older name after a newer one.
    /// </summary>
    private void WriteHistory(Action write)
    {
        lock (_writeLock)
        {
            _historyWrites = _historyWrites.ContinueWith(_ => write(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Reads the history after the writes waiting before it, so it sees the turn that just ended.</summary>
    private Task<T> AfterHistoryWrites<T>(Func<T> read)
    {
        lock (_writeLock)
        {
            var reading = _historyWrites.ContinueWith(_ => read(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            // What comes next waits for the read, but not on whether it failed.
            _historyWrites = reading.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return reading;
        }
    }

    /// <summary>
    /// A folder's project key, the same on other machines with a clone of it, and the name it goes by: a worktree's main
    /// checkout's folder name (DESIGN.md §6, "Usage history").
    /// </summary>
    private (string Key, string Name) ProjectOf(string folder) =>
        _projects.GetOrAdd(folder, f => (ProjectKey.For(f).Key, Formats.FolderName(GitWorktrees.MainCheckoutOf(f) ?? f)));

    /// <summary>A <c>rate_limit_event</c> from any tab updates the header at once, between polls.</summary>
    public void OnRateLimitEvent(RateLimitEventMessage message) => _poller.NotifyRateLimitEvent(message.Info);

    private void OnPolled(UsageSnapshot snapshot)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (Store.AddSample(snapshot))
                {
                    await ShareAsync(publish: true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't save a usage sample.");
            }
        });
        _services.Dispatcher.Post(() =>
        {
            LogCredits(Current, snapshot);
            Current = snapshot;
            Updated?.Invoke(snapshot);
        });
    }

    /// <summary>Logs usage credits starting or stopping to pay, with what said so (DESIGN.md §6, "Usage credits").</summary>
    private void LogCredits(UsageSnapshot? before, UsageSnapshot after)
    {
        var now = _services.Time.GetUtcNow();
        var paying = CreditUse.From(after, now);
        if ((CreditUse.From(before, now) is not null) == (paying is not null))
        {
            return;
        }
        var report = after.CreditsReport;
        _logger.LogInformation(
            "Usage credits {Change} ({Source}): Claude Code last said paying={Paying} stillUsedUp={StillUsedUp} limit={Limit} at {At}; the poll said enabled={Enabled} at {PolledAt}; session {Session}%.",
            paying is null ? "stopped paying" : $"pay until {paying.Until?.ToString("O") ?? "unknown"}",
            after.Source, report?.IsUsing, report?.StillUsedUp, report?.LimitType, report?.At.ToString("O"),
            after.Credits?.IsEnabled, after.Credits?.At.ToString("O"), after.Session?.Percent);
    }

    private async Task<System.Text.Json.Nodes.JsonObject> GetUsageAsync(CancellationToken cancellationToken)
    {
        var utility = await _services.GetUtilitySessionAsync(cancellationToken).ConfigureAwait(false);
        return await utility.GetUsageAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RunUsageCommandAsync(CancellationToken cancellationToken)
    {
        var utility = await _services.GetUtilitySessionAsync(cancellationToken).ConfigureAwait(false);
        return await utility.RunLocalCommandAsync("/usage", UsageCommandTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Settings → Usage → Keep usage history.</summary>
    private void Prune()
    {
        try
        {
            Store.Prune(_services.Settings.Usage.KeepHistory.ToTimeSpan());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't prune usage history.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _services.SettingsChanged -= OnServicesSettingsChanged;
        await _pruneTimer.DisposeAsync().ConfigureAwait(false);
        await _shareTimer.DisposeAsync().ConfigureAwait(false);
        await _poller.DisposeAsync().ConfigureAwait(false);
        // A share under way finishes before the history closes; one waiting its turn then sees it's closing.
        _disposed = true;
        await _sharing.WaitAsync().ConfigureAwait(false);
        _sharing.Release();
        // The last turns and names are written before the history closes. Each write catches its own failure.
        Task writes;
        lock (_writeLock)
        {
            writes = _historyWrites;
        }
        await writes.ConfigureAwait(false);
    }
}
