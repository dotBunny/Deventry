using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Git;
using Deventry.Core.History;
using Deventry.Core.Library;
using Deventry.Core.Settings;
using Deventry.Core.Transcripts;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>How <see cref="LibraryService.CopyToLibraryAsync"/> went, for <b>Sync now</b> to report (DESIGN.md §9).</summary>
public abstract record LibraryCopyResult
{
    private LibraryCopyResult()
    {
    }

    /// <summary>The transcript, the subagent transcripts and the record are in the library.</summary>
    /// <param name="Size">What the session takes in the library now, in bytes; null when its folder couldn't be measured.</param>
    public sealed record Copied(long? Size) : LibraryCopyResult;

    /// <summary>The tab stopped syncing before the copy began, so nothing was written.</summary>
    public sealed record NotSyncing : LibraryCopyResult;

    /// <summary>This machine has no transcript for the session: Claude Code cleaned it up, or it was never written.</summary>
    public sealed record NoTranscript : LibraryCopyResult;

    /// <summary>Writing to the library failed, for example because its folder isn't available.</summary>
    public sealed record Failed(string Reason) : LibraryCopyResult;

    /// <summary>
    /// Another machine holds the session, or its lease couldn't be read, so nothing was written. When the session was
    /// taken over from this machine, <see cref="LibraryService.LeaseLost"/> says so too.
    /// </summary>
    public sealed record NotHeld : LibraryCopyResult;

    /// <summary>
    /// The library's transcript has turns this machine's doesn't, and none of this machine's are missing from it: another
    /// machine carried the session on. Nothing was written; the tab picks those turns up (DESIGN.md §9, "One machine at a
    /// time").
    /// </summary>
    public sealed record Ahead(LibraryComparison Comparison) : LibraryCopyResult;

    /// <summary>
    /// The session went on here and on another machine, each with turns the other doesn't have. Nothing was written, so
    /// the library keeps the other machine's.
    /// </summary>
    public sealed record Diverged(LibraryComparison Comparison) : LibraryCopyResult;
}

/// <summary>How the library's copy of a session stands to this machine's transcript (DESIGN.md §9, "One machine at a time").</summary>
/// <param name="Machine">
/// The machine the library's record names, when it isn't this one; null when it is, since a sync client can bring the
/// transcript before the record.
/// </param>
/// <param name="LastUsed">When <paramref name="Machine"/> last used the session.</param>
public sealed record LibraryComparison(LibraryStanding Standing, string? Machine, DateTimeOffset? LastUsed);

/// <summary>
/// The session library (DESIGN.md §9): copies the transcript and record of each tab that syncs into the library folder
/// after its turns, keeps a lease on those sessions so two machines don't write the same one, and syncs Deventry's
/// settings through the library when that's turned on (DESIGN.md §14, "Settings sync").
/// </summary>
public sealed class LibraryService : IDisposable
{
    /// <summary>Claude Code finishes writing the transcript around the result message; give it a moment.</summary>
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);

    /// <summary>Library retention runs at launch, then this often while Deventry is open.</summary>
    public static readonly TimeSpan PruneInterval = TimeSpan.FromDays(1);

    /// <summary>Copies to the library under way, waiting out <see cref="SettleDelay"/> or writing.</summary>
    private int _copying;

    /// <summary>No copy to the library is under way: for tests, which can't await the copies turns start.</summary>
    internal bool IsIdle => Volatile.Read(ref _copying) == 0;

    private readonly AppServices _services;
    private readonly ILogger _logger;
    private readonly ITimer _syncTimer;
    private ITimer? _pruneTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _applyingSync;

    public LibraryService(AppServices services)
    {
        _services = services;
        _logger = services.Loggers.CreateLogger<LibraryService>();
        Library = new SessionLibrary(LibraryFolder, services.Time);
        Leases = new LeaseManager(MachineName, services.MachineId, services.Time);
        Leases.LeaseLost += (sessionId, machine) => services.Dispatcher.Post(() => LeaseLost?.Invoke(sessionId, machine));
        // Pick up settings changed on other machines.
        _syncTimer = services.Time.CreateTimer(_ => services.Dispatcher.Post(() => _ = SyncSettingsAsync()), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public SessionLibrary Library { get; private set; }

    /// <summary>Claude Code's sessions on this machine, for History. Kept, so rescans only read changed files.</summary>
    public HistoryIndex? History => _services.ProjectsDirectory is { } projects
        ? _history is { } index && index.ProjectsDirectory == projects ? index : _history = new HistoryIndex(projects)
        : null;

    private HistoryIndex? _history;

    public LeaseManager Leases { get; }

    public string LibraryFolder => _services.Settings.Sessions.LibraryFolder is { Length: > 0 } folder ? folder : _services.Paths.DefaultLibraryDirectory;

    /// <summary>This machine's name in History and leases.</summary>
    public string MachineName => _services.Settings.Sessions.MachineName is { Length: > 0 } name ? name : Environment.MachineName;

    /// <summary>Raised on the UI thread when another machine took over a session open here.</summary>
    public event Action<string, string>? LeaseLost;

    /// <summary>The library folder setting may have changed.</summary>
    public void OnSettingsChanged()
    {
        if (!FolderHistory.SamePath(Library.LibraryFolder, LibraryFolder))
        {
            Library = new SessionLibrary(LibraryFolder, _services.Time);
        }
        if (!_applyingSync)
        {
            _ = SyncSettingsAsync();
        }
    }

    /// <summary>
    /// A session's transcript on this machine: the local working copy of a library session, Claude Code's own copy, or
    /// null.
    /// </summary>
    public string? FindTranscript(string sessionId, string? localCopy)
    {
        if (localCopy is not null && File.Exists(localCopy))
        {
            return localCopy;
        }
        if (_services.ProjectsDirectory is { } projects && TranscriptReader.Find(projects, sessionId) is { } path)
        {
            return path;
        }
        var local = Path.Combine(_services.Paths.LocalSessionsDirectory, $"{sessionId}.jsonl");
        return File.Exists(local) ? local : null;
    }

    /// <summary>
    /// Brings a library session to this machine: copies its transcript to the local working folder, which Claude Code
    /// resumes from and keeps writing to (DESIGN.md §9, "Resume").
    /// </summary>
    public Task<string> CopyToLocalAsync(string sessionId) => Library.CopyToLocalAsync(sessionId, _services.Paths.LocalSessionsDirectory);

    /// <summary>
    /// How the library's copy of a session stands to this machine's transcript, the local working copy
    /// <paramref name="localCopy"/> or Claude Code's own (DESIGN.md §9, "One machine at a time"). Null when there's nothing
    /// to compare, this machine having no transcript, or the library can't be read just now. Off the UI thread: the
    /// library may be on a slow drive.
    /// </summary>
    public async Task<LibraryComparison?> CompareAsync(string sessionId, string? localCopy)
    {
        try
        {
            return await Task.Run(async () => FindTranscript(sessionId, localCopy) is { } transcript
                ? await CompareTranscriptAsync(sessionId, transcript).ConfigureAwait(false)
                : null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<LibraryComparison> CompareTranscriptAsync(string sessionId, string transcript)
    {
        var standing = await Library.CompareAsync(sessionId, transcript).ConfigureAwait(false);
        if (standing is not (LibraryStanding.Ahead or LibraryStanding.Diverged))
        {
            return new LibraryComparison(standing, null, null);
        }
        // Who carried it on, unless the record hasn't come yet and still names this machine.
        return Library.GetRecord(sessionId) is { Machine: { Length: > 0 } machine } record && machine != MachineName
            ? new LibraryComparison(standing, machine, record.LastUsed)
            : new LibraryComparison(standing, null, null);
    }

    /// <summary>
    /// Brings another machine's later turns here (DESIGN.md §9, "One machine at a time"): the library's transcript replaces
    /// the tab's working copy, <paramref name="workingCopy"/> or one made in the local working folder, which it returns.
    /// For a session <see cref="CompareAsync(string, string?)"/> found the library ahead of this machine in.
    /// </summary>
    public async Task<string> PickUpAsync(string sessionId, string? workingCopy)
    {
        var target = workingCopy ?? Path.Combine(_services.Paths.LocalSessionsDirectory, $"{sessionId}.jsonl");
        await Task.Run(() => Library.PickUpAsync(sessionId, target)).ConfigureAwait(false);
        return target;
    }

    /// <summary>
    /// Keeps this machine's transcript of a session that went on here and on another machine in the library, as a copy of
    /// its own beside the library's (DESIGN.md §9, "One machine at a time"). Returns what History calls the copy.
    /// </summary>
    /// <exception cref="FileNotFoundException">This machine no longer has the session's transcript.</exception>
    public Task<string> SaveConflictCopyAsync(string sessionId, string? localCopy) => Task.Run(() =>
        FindTranscript(sessionId, localCopy) is { } transcript
            ? Library.SaveConflictCopyAsync(sessionId, transcript, MachineName)
            : throw new FileNotFoundException("This machine no longer has the session's transcript."));

    /// <summary>
    /// Copies a tab's session into the library in the background, and takes its lease (DESIGN.md §9, "Writing"): after
    /// each turn of a tab that syncs, when a tab starts syncing, and for <b>Sync now</b>. Never throws.
    /// </summary>
    /// <param name="stillSyncing">
    /// Asked after the settle delay and again before the lease is taken, so a tab that stopped syncing meanwhile writes
    /// nothing and doesn't take its lease back.
    /// </param>
    /// <param name="force">Copies every file again, even ones that look unchanged (<b>Sync now</b>).</param>
    public async Task<LibraryCopyResult> CopyToLibraryAsync(SessionRecord record, string? localCopy, Func<bool> stillSyncing, bool force = false)
    {
        Interlocked.Increment(ref _copying);
        try
        {
            await Task.Delay(SettleDelay, _services.Time).ConfigureAwait(false);
            if (!stillSyncing())
            {
                return new LibraryCopyResult.NotSyncing();
            }
            if (FindTranscript(record.SessionId, localCopy) is not { } transcript)
            {
                return new LibraryCopyResult.NoTranscript();
            }
            record.Machine = MachineName;
            if (record.Folder is { } folder)
            {
                record.Project = ProjectIdentity.Read(folder);
                if (record.Project is not null)
                {
                    record.HadUncommittedChanges = await _services.Git.HasUncommittedChangesAsync(folder).ConfigureAwait(false) == true;
                }
            }
            var subagents = Path.Combine(Path.GetDirectoryName(transcript)!, record.SessionId, SessionLibrary.SubagentsFolderName);
            // Leases are refreshed once a minute, so another machine may have taken the session over since: check, or
            // this copy would overwrite what it wrote and take the session back.
            if (!stillSyncing())
            {
                return new LibraryCopyResult.NotSyncing();
            }
            var sessionFolder = Library.GetSessionFolder(record.SessionId);
            if (!Leases.MayWrite(record.SessionId, sessionFolder))
            {
                return new LibraryCopyResult.NotHeld();
            }
            // Nor over turns another machine added: a sync client can bring them after this machine started the session,
            // and the lease that machine released as Deventry closed there says nothing of them.
            var comparison = await CompareTranscriptAsync(record.SessionId, transcript).ConfigureAwait(false);
            switch (comparison.Standing)
            {
                case LibraryStanding.Ahead:
                    return new LibraryCopyResult.Ahead(comparison);
                case LibraryStanding.Diverged:
                    return new LibraryCopyResult.Diverged(comparison);
            }
            // Every machine's turns in the session, while machines share their usage (DESIGN.md §6, "Sharing across machines").
            var usage = _services.Usage is { } tracker
                ? await tracker.SessionUsageForLibraryAsync(record.SessionId, Library.ReadSessionUsage(record.SessionId)).ConfigureAwait(false)
                : null;
            await Library.SaveAsync(record, transcript, subagents, force, usage).ConfigureAwait(false);
            // The lease is taken last, so a lease of this run's says the copy is complete.
            if (stillSyncing())
            {
                Leases.Renew(record.SessionId, sessionFolder);
            }
            // What the sync client has to keep, for the tab to show (DESIGN.md §9, "Writing").
            return new LibraryCopyResult.Copied(Library.GetSessionSize(record.SessionId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't copy session {SessionId} to the library.", record.SessionId);
            return new LibraryCopyResult.Failed(ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _copying);
        }
    }

    public LeaseStatus CheckLease(string sessionId) => Leases.Check(Library.GetSessionFolder(sessionId));

    /// <summary>The sessions open here in tabs that sync, read on the UI thread: library retention keeps them. The shell sets it.</summary>
    public Func<IReadOnlySet<string>> OpenSessions { get; set; } = () => new HashSet<string>();

    /// <summary>
    /// Starts library retention (DESIGN.md §9, "Keeping sessions"): now, then every <see cref="PruneInterval"/>, so a
    /// Deventry left open for weeks still deletes what's old. Each pass does nothing while the setting is off.
    /// </summary>
    public void StartPruning() =>
        _pruneTimer ??= _services.Time.CreateTimer(_ => _services.Dispatcher.Post(() =>
        {
            var keep = OpenSessions();
            // The library may be on a slow or offline drive: not on the UI thread.
            _ = Task.Run(() => Prune(keep));
        }), null, TimeSpan.Zero, PruneInterval);

    /// <summary>
    /// Settings → Sessions → Delete sessions not used for a while. <paramref name="keep"/> is the sessions open here in
    /// tabs that sync.
    /// </summary>
    public void Prune(IReadOnlySet<string> keep)
    {
        try
        {
            Library.Prune(_services.Settings.Sessions.LibraryRetention(), keep);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't prune the session library.");
        }
    }

    // ---- Settings sync (DESIGN.md §14) ----------------------------------------------------------------------------

    /// <summary>Whether the library already holds settings from another machine: the first-time question.</summary>
    public bool HasSyncedSettings() => SettingsSync.HasRemoteValues(SettingsSync.ReadFile(Library.SettingsSyncFile));

    /// <summary>"Replace them with this machine's": publishes every synced setting from here.</summary>
    public async Task PublishAllSettingsAsync()
    {
        var local = FlattenSettings();
        var result = SettingsSync.PublishAll(local, _services.Time.GetUtcNow(), MachineName);
        await SettingsSync.WriteFileAsync(Library.SettingsSyncFile, result.Remote).ConfigureAwait(true);
        _services.State.SettingsSync = result.State;
        _services.SaveState();
    }

    /// <summary>
    /// Merges this machine's settings with the synced file: newer synced changes are applied here, local edits are
    /// published. Runs on the UI thread, which owns the settings. Does nothing while sync is off.
    /// </summary>
    public async Task SyncSettingsAsync()
    {
        if (!_services.Settings.Sessions.SyncSettings || !await _syncLock.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }
        try
        {
            if (await Task.Run(() => SettingsSync.TryReadFile(Library.SettingsSyncFile)).ConfigureAwait(true) is not { } remote)
            {
                _logger.LogInformation("Couldn't read the synced settings file just now; trying again at the next sync.");
                return;
            }
            var result = SettingsSync.Merge(FlattenSettings(), _services.State.SettingsSync ?? new SettingsSyncState(), remote, _services.Time.GetUtcNow(), MachineName);
            if (result.ToApply.Count > 0)
            {
                ApplySettings(result.ToApply);
            }
            if (result.RemoteChanged)
            {
                await SettingsSync.WriteFileAsync(Library.SettingsSyncFile, result.Remote).ConfigureAwait(true);
            }
            _services.State.SettingsSync = result.State;
            _services.SaveState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't sync settings through the library.");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private Dictionary<string, JsonNode?> FlattenSettings()
    {
        // What syncs (DESIGN.md §14), by section; ApplySettings copies each of them back.
        var values = SettingsSync.Flatten(SettingsJson(_services.Settings), SyncedSettings.Sections, SyncedSettings.Leaves);
        foreach (var path in SyncedSettings.PerMachine)
        {
            values.Remove(path);
        }
        return values;
    }

    private static JsonObject SettingsJson(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings, JsonFileStore<AppSettings>.Options)!.AsObject();

    private void ApplySettings(IReadOnlyDictionary<string, JsonNode?> values)
    {
        var json = SettingsJson(_services.Settings);
        foreach (var (path, value) in values)
        {
            if (!SyncedSettings.PerMachine.Contains(path, StringComparer.Ordinal))
            {
                SettingsSync.Apply(json, path, value);
            }
        }
        var updated = json.Deserialize<AppSettings>(JsonFileStore<AppSettings>.Options);
        if (updated is null)
        {
            return;
        }
        var live = _services.Settings;
        live.Appearance = updated.Appearance;
        live.NewTabs = updated.NewTabs;
        live.Usage = updated.Usage;
        live.CheckIns = updated.CheckIns;
        live.QuickSuffixes = updated.QuickSuffixes;
        live.Processes = updated.Processes;
        live.Notifications = updated.Notifications;
        live.Keyboard = updated.Keyboard;
        _applyingSync = true;
        try
        {
            _services.SaveSettings();
        }
        finally
        {
            _applyingSync = false;
        }
    }

    public void Dispose()
    {
        _syncTimer.Dispose();
        _pruneTimer?.Dispose();
        Leases.Dispose();
        _syncLock.Dispose();
    }
}
