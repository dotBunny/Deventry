using System.Text.Json;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Diffs;
using Deventry.Core.Library;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>The tab's part in the session library (DESIGN.md §9).</summary>
public sealed partial class TabViewModel
{
    // ---- Syncing (DESIGN.md §9, "Session library") ------------------------------------------------------------

    /// <summary>
    /// The tab copies its session to the library after each turn, so another machine can open it from History. Off
    /// unless the tab opted in; a tab that doesn't sync never writes to the library and ignores leases.
    /// </summary>
    public bool SyncToLibrary => State.SyncToLibrary;

    /// <summary>
    /// This tab's Claude Code was started as a copy (<b>Open a copy</b>, a branch, or a conflict copy) and hasn't had its
    /// first turn yet, so it still has the original's id. <see cref="TabState.ForkOnNextStart"/> stays set until then too.
    /// </summary>
    private bool _forkAwaitingId;

    /// <summary><b>Sync to other machines</b> in the tab's menu.</summary>
    [RelayCommand]
    private void ToggleSyncToLibrary() => SetSyncToLibrary(!State.SyncToLibrary);

    /// <summary>
    /// Turns syncing on or off. On copies the session to the library straight away, in the background, and takes its
    /// lease, unless a turn is running: then it's copied when the turn ends. Off stops copying and releases the lease,
    /// leaving the copy already in the library as it was, so other machines still see it.
    /// </summary>
    public void SetSyncToLibrary(bool on)
    {
        if (on == State.SyncToLibrary)
        {
            return;
        }
        if (!on)
        {
            State.SyncToLibrary = false;
            SyncChanged();
            ReleaseLease();
            return;
        }
        // A copy still has the original's id until its first turn: it syncs under its own id from then on.
        var copyPending = State.ForkOnNextStart || _forkAwaitingId;
        if (!copyPending && !IsReadOnly && State.SessionId is { } sessionId && _services.Library.Library.GetTranscriptPath(sessionId) is not null
            && _services.Library.CheckLease(sessionId) is LeaseStatus.HeldByOther other)
        {
            // Another machine carries this session on: copying it from here would overwrite what it wrote.
            _conversation.AddNote($"{other.Machine} has this session open, so this tab can't sync it too. Sync stays off.", NoteKind.Warning);
            // The menu item ticked itself as it was clicked: reading the value again unticks it.
            OnPropertyChanged(nameof(SyncToLibrary));
            return;
        }
        State.SyncToLibrary = true;
        SyncChanged();
        if (!copyPending && !IsWorking)
        {
            CopyToLibrary(turningOn: true);
        }
    }

    private void SyncChanged()
    {
        OnPropertyChanged(nameof(SyncToLibrary));
        OnSyncNowChanged();
        OnSyncSizeChanged();
        LibraryCheckTicker.Run(State.SyncToLibrary && _session is not null);
        _services.SaveState();
    }

    // ---- What it takes in the library (DESIGN.md §9, "Writing") ---------------------------------------------------

    /// <summary>
    /// What the session takes in the session library as of the tab's last copy there, "312 MB", while the tab syncs: on
    /// the info card, the row's sync icon and in Tab settings. Null before the session's first copy.
    /// </summary>
    public string? SyncSize => State.SyncToLibrary && State.SyncedSize is { } size && size.SessionId == State.SessionId ? Formats.Size(size.Bytes) : null;

    /// <summary>The tip of the row's sync icon, with the size once there is one.</summary>
    public string SyncTip => SyncSize is { } size ? $"Synced to the session library · {size}" : "Synced to the session library";

    /// <summary>Keeps what a copy found the session takes, saved with the tab so it shows from launch.</summary>
    private void SetSyncedSize(string sessionId, long? bytes)
    {
        if (bytes is not { } size || State.SyncedSize is { } known && known.SessionId == sessionId && known.Bytes == size)
        {
            return;
        }
        State.SyncedSize = new SyncedSize { SessionId = sessionId, Bytes = size };
        OnSyncSizeChanged();
        _services.SaveState();
    }

    /// <summary>The size shows for the tab's session while it syncs: when either changes, so may what's shown.</summary>
    private void OnSyncSizeChanged()
    {
        OnPropertyChanged(nameof(SyncSize));
        OnPropertyChanged(nameof(SyncTip));
        OnPropertyChanged(nameof(InfoRows));
    }

    /// <summary>
    /// <b>Sync now</b> can run: the tab syncs, has a session of its own, and Claude Code isn't writing its transcript. A
    /// copy that hasn't had its first turn still has the original's id, so it waits for its own.
    /// </summary>
    public bool CanSyncNow => State.SyncToLibrary && State.SessionId is not null && !IsWorking && !IsReadOnly && !State.ForkOnNextStart && !_forkAwaitingId;

    /// <summary><b>Sync now</b>'s tip: what it does, or why it's disabled.</summary>
    public string SyncNowTip =>
        IsReadOnly ? $"This session continued on {TakenOverBy}"
        : IsWorking ? "Claude is working: the session is copied when the turn ends"
        : State.SessionId is null || State.ForkOnNextStart || _forkAwaitingId ? "There's nothing to copy until the first message"
        : "Copy this session to the session library now, without waiting for the next turn";

    /// <summary>
    /// <b>Sync now</b> in the tab's menu: copies the session to the library straight away, every file again, rather than
    /// waiting for the next turn (DESIGN.md §9, "Writing"). It catches the library up after a copy that failed while its
    /// folder was unavailable, or a rename since the last turn, and says in the conversation how it went.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        var sessionId = State.SessionId!;
        var library = _services.Library;
        // Leases are only refreshed once a minute: another machine may have taken the session over since, and copying
        // from here would overwrite what it wrote.
        if (library.Library.GetTranscriptPath(sessionId) is not null && library.CheckLease(sessionId) is LeaseStatus.HeldByOther other)
        {
            await OnTakenOverAsync(other.Machine);
            return;
        }
        var result = await library.CopyToLibraryAsync(LibraryRecord(), State.TranscriptPath, () => State.SyncToLibrary && !IsReadOnly, force: true);
        switch (result)
        {
            case LibraryCopyResult.Copied copied:
                _libraryCopyFailed = false;
                SetSyncedSize(sessionId, copied.Size);
                _conversation.AddNote("Copied this session to the session library.");
                break;
            case LibraryCopyResult.NoTranscript:
                _conversation.AddNote("Couldn't copy this session to the session library: its transcript isn't on this machine.", NoteKind.Warning);
                break;
            case LibraryCopyResult.Failed failed:
                _conversation.AddNote($"Couldn't copy this session to the session library: {failed.Reason}", NoteKind.Warning);
                break;
            case LibraryCopyResult.NotHeld when !IsReadOnly:
                _conversation.AddNote("Didn't copy this session to the session library: its lease there couldn't be read, or another machine has it open. Try again in a moment.", NoteKind.Warning);
                break;
            case LibraryCopyResult.Ahead:
                await PickUpFromLibraryAsync();
                break;
            case LibraryCopyResult.Diverged diverged:
                await KeepApartAsync(diverged.Comparison.Machine);
                break;
        }
    }

    private void OnSyncNowChanged()
    {
        OnPropertyChanged(nameof(CanSyncNow));
        OnPropertyChanged(nameof(SyncNowTip));
        SyncNowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Copies the session to the library in the background, and takes its lease with the copy, if the tab syncs. A tab
    /// that doesn't sync never writes to the library.
    /// </summary>
    /// <param name="turningOn">The copy that turning sync on makes: if it can't be made, sync goes off again.</param>
    private void CopyToLibrary(bool turningOn = false)
    {
        if (State.SessionId is not null && State.SyncToLibrary && !IsReadOnly)
        {
            LibraryCopy = CopyToLibraryAsync(turningOn);
        }
    }

    /// <summary>The latest copy after a turn, for tests to wait on.</summary>
    internal Task LibraryCopy { get; private set; } = Task.CompletedTask;

    /// <summary>The last copy after a turn failed: the next one that works says so.</summary>
    private bool _libraryCopyFailed;

    /// <summary>
    /// A copy that fails says so once, until one works again: the library is missing turns another machine would need,
    /// and nothing else would show it (DESIGN.md §9, "Writing"). One that finds another machine's turns there instead
    /// brings them here, or keeps this machine's apart from them (DESIGN.md §9, "One machine at a time").
    /// </summary>
    private async Task CopyToLibraryAsync(bool turningOn)
    {
        var record = LibraryRecord();
        var result = await _services.Library.CopyToLibraryAsync(record, State.TranscriptPath, () => State.SyncToLibrary);
        var handled = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        _services.Dispatcher.Post(() => handled.SetResult(OnCopiedToLibrary(record.SessionId, result, turningOn)));
        // What the copy led to, so the copy ends once another machine's turns are here or this machine's are kept apart.
        await await handled.Task;
    }

    /// <summary>Tells how a copy after a turn, or on turning sync on, went. Returns what it led to.</summary>
    private Task OnCopiedToLibrary(string sessionId, LibraryCopyResult result, bool turningOn)
    {
        switch (result)
        {
            case LibraryCopyResult.Copied copied:
                SetSyncedSize(sessionId, copied.Size);
                if (_libraryCopyFailed)
                {
                    _libraryCopyFailed = false;
                    _conversation.AddNote("Copied this session to the session library again.");
                }
                break;
            case LibraryCopyResult.Failed failed when !_libraryCopyFailed:
                _libraryCopyFailed = true;
                _conversation.AddNote($"Couldn't copy this session to the session library: {failed.Reason} It's tried again after the next turn, or use Sync now.", NoteKind.Warning);
                break;
            case LibraryCopyResult.Diverged diverged when turningOn:
                // Nothing has gone to the library from here since: sync stays off, rather than the tab becoming read-only.
                State.SyncToLibrary = false;
                SyncChanged();
                _conversation.AddNote($"{Formats.Capitalize(diverged.Comparison.Machine ?? "another machine")} carried this session on in the session library since this tab last synced it, so syncing it from here would lose those turns. Sync stays off; open the session from History to go on with them.", NoteKind.Warning);
                break;
            case LibraryCopyResult.Diverged diverged:
                return KeepApartAsync(diverged.Comparison.Machine);
            case LibraryCopyResult.Ahead when !IsWorking:
                return PickUpFromLibraryAsync();
        }
        return Task.CompletedTask;
    }

    // ---- One machine at a time (DESIGN.md §9) ----------------------------------------------------------------------

    /// <summary>
    /// Another machine took this session over. The tab stops its process and becomes read-only, so the two machines
    /// never write the same session (DESIGN.md §9, "One machine at a time").
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly), nameof(CanSyncNow), nameof(SyncNowTip), nameof(CanRestart))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SyncNowCommand), nameof(RestartCommand))]
    public partial string? TakenOverBy { get; set; }

    public bool IsReadOnly => TakenOverBy is not null;

    /// <summary>
    /// The banner in place of the composer. History only selects a tab already open on the session, so carrying it on here
    /// takes closing this one first.
    /// </summary>
    public string ReadOnlyText => $"This session continued on {TakenOverBy}. The tab is read-only here; to continue on this machine, close it and open the session again from History.";

    public Task OnTakenOverAsync(string machine) => BecomeReadOnlyAsync(machine, $"This session was taken over by {machine}.");

    /// <summary>The session carries on on <paramref name="machine"/>: the tab stops its process and only shows the conversation.</summary>
    private async Task BecomeReadOnlyAsync(string machine, string note)
    {
        TakenOverBy = machine;
        OnPropertyChanged(nameof(ReadOnlyText));
        LibraryCheckTicker.Stop();
        _conversation.AddNote(note, NoteKind.Warning);
        await StopSessionAsync();
        Status = TabStatus.Exited;
    }

    // ---- Another machine's turns (DESIGN.md §9, "One machine at a time") --------------------------------------------

    /// <summary>How often a running tab that syncs looks for turns another machine added to the library since.</summary>
    internal static readonly TimeSpan LibraryCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Looks for another machine's turns while the tab's Claude Code runs: a sync client can bring them well after the
    /// tab started, from a machine that has since closed Deventry and released its lease.
    /// </summary>
    private UiTicker LibraryCheckTicker => field ??= new(_services.Time, _services.Dispatcher, LibraryCheckInterval, () => _ = CheckLibraryAsync());

    /// <summary>Something went to this tab's Claude Code since it started: the library is looked at before the first message only.</summary>
    private bool _sentSinceStart;

    private bool _checkingLibrary;

    /// <summary>
    /// How the library's copy of the session stands to this machine's, for a tab that syncs; null for one that doesn't,
    /// a copy that hasn't its own id yet, or when there's nothing to compare.
    /// </summary>
    private async Task<LibraryComparison?> CompareWithLibraryAsync() =>
        State.SyncToLibrary && !IsReadOnly && State.SessionId is { } sessionId && !State.ForkOnNextStart && !_forkAwaitingId
            ? await _services.Library.CompareAsync(sessionId, State.TranscriptPath)
            : null;

    /// <summary>
    /// Once a minute while Claude Code waits: another machine's turns that reached the library since are brought here,
    /// and a session that went on in both places is kept apart.
    /// </summary>
    private async Task CheckLibraryAsync()
    {
        if (_checkingLibrary || _session is null || _starting is not null || IsWorking || IsReadOnly)
        {
            return;
        }
        _checkingLibrary = true;
        try
        {
            await ActOnLibraryAsync(await CompareWithLibraryAsync());
        }
        finally
        {
            _checkingLibrary = false;
        }
    }

    /// <summary>
    /// Before the first message since Claude Code started, as for <see cref="CheckLibraryAsync"/>, and before the one that
    /// starts it once its conversation is read: a restored tab is read as it's shown, maybe long before its first message
    /// (DESIGN.md §9, "Starting fast"), and an unloaded one was read as it first started. When it brings another machine's
    /// turns here, the message isn't sent but stays in the composer, to be read again against them. Returns whether the
    /// message was held back.
    /// </summary>
    private async Task<bool> CheckLibraryBeforeSendingAsync()
    {
        // One still to read its conversation looks as its start reads it.
        var due = _session is null ? _restoredTranscript && _starting is null : !_sentSinceStart;
        if (!due || IsWorking || !State.SyncToLibrary)
        {
            return false;
        }
        if (_transcriptRead is { } read)
        {
            // What's compared is what the tab shows.
            await read;
        }
        if (!await ActOnLibraryAsync(await CompareWithLibraryAsync()))
        {
            return false;
        }
        if (!IsReadOnly)
        {
            _conversation.AddNote("Your message wasn't sent, so you can read it again against what's new. It's still in the composer.");
        }
        return true;
    }

    /// <summary>Brings another machine's turns here, or keeps this machine's apart from them. Returns whether it did either.</summary>
    private async Task<bool> ActOnLibraryAsync(LibraryComparison? comparison)
    {
        switch (comparison?.Standing)
        {
            case LibraryStanding.Ahead when !IsWorking && !IsReadOnly:
                await PickUpFromLibraryAsync();
                return true;
            case LibraryStanding.Diverged when !IsReadOnly:
                await KeepApartAsync(comparison.Machine);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Another machine carried the session on, and this machine has no turns the library lacks: Claude Code starts again
    /// from the library's copy, and the conversation is read again from it. What's typed in the composer stays.
    /// </summary>
    private async Task PickUpFromLibraryAsync()
    {
        await StopSessionAsync();
        _conversation.Clear();
        ChangedFiles.Reset();
        Status = TabStatus.NotStarted;
        // Read again as it starts, which brings the library's copy here (RestoreTranscriptAsync).
        _restoredTranscript = false;
        _transcriptRead = null;
        await EnsureStartedAsync();
    }

    /// <summary>
    /// The session went on here and on another machine, each with turns the other doesn't have. To lose neither, this
    /// machine's are kept in the library as a copy of their own, which History lists, and the tab becomes read-only as
    /// after a take-over: the session opens again from History, either machine's.
    /// </summary>
    private async Task KeepApartAsync(string? machine)
    {
        var where = machine ?? "another machine";
        var why = $"{Formats.Capitalize(where)} carried this session on too, so copying this tab's turns to the session library would lose the ones made there.";
        string note;
        try
        {
            var copy = await _services.Library.SaveConflictCopyAsync(State.SessionId!, State.TranscriptPath);
            note = $"{why} This tab's are kept there as a copy of their own, \"{copy}\" in History.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note = $"{why} Keeping this tab's there as a copy of their own didn't work: {ex.Message} They're still on this machine.";
        }
        // While it's still this tab's to release: the other machine needn't wait for the lease to go stale.
        ReleaseLease();
        await BecomeReadOnlyAsync(where, note);
    }

    /// <summary>"Picked up where LAPTOP-02 left off at 12:04.": the note a tab that brought another machine's turns here reads them with.</summary>
    private string PickedUpNote(LibraryComparison comparison) => comparison.Machine is { } machine
        ? comparison.LastUsed is { } at ? $"Picked up where {machine} left off {Formats.When(at, _services.Time)}." : $"Picked up where {machine} left off."
        : "Picked up this session's later turns from the session library.";

    /// <summary>What the library keeps about this session, as of now. Built on the UI thread, which owns the state.</summary>
    private SessionRecord LibraryRecord() => new()
    {
        SessionId = State.SessionId!,
        Name = DisplayName,
        AutoName = State.AutoName,
        UserName = State.UserName,
        Mark = State.Mark,
        Model = _modelId,
        Effort = Effort,
        PermissionMode = PermissionMode,
        Overrides = Copy(State.Overrides) ?? new TabOverrides(),
        // A copy: the tab keeps adding to its own totals while the record is saved.
        Tokens = Copy(State.Tokens) ?? new TokenTotals(),
        LastUsed = _services.Time.GetUtcNow(),
        Folder = Folder,
        FirstPrompt = _firstPrompt,
        ClaudeCodeVersion = _session?.ClaudeCodeVersion,
        ReviewedFiles = ReviewedFiles.ToRecord(State.ReviewedFiles, Folder),
    };

    /// <summary>
    /// Takes the session's library lease as a tab that syncs starts it, so another machine sees it's in use from the
    /// start (DESIGN.md §9, "One machine at a time"). A session that isn't in the library yet gets its lease with its
    /// first copy. Returns false when another machine holds a live lease: the session carried on there, so the tab
    /// becomes read-only instead of starting. Taking the lease notes in the record that the session was opened.
    /// </summary>
    private async Task<bool> ClaimLeaseAsync()
    {
        if (State.SessionId is not { } sessionId)
        {
            return true;
        }
        var library = _services.Library;
        try
        {
            // File reads on the library folder, which may be a slow or offline drive: not on the UI thread. What the session
            // takes there is read too, so a tab that hasn't copied it yet, opened from History, shows it from the start.
            var (status, size) = await Task.Run(() => library.Library.GetTranscriptPath(sessionId) is null
                ? ((LeaseStatus?)null, (long?)null)
                : (library.CheckLease(sessionId), library.Library.GetSessionSize(sessionId)));
            if (status is null)
            {
                return true;
            }
            if (status is LeaseStatus.Free or LeaseStatus.Stale)
            {
                // Taken here. The record notes the session was opened, even if no turn follows, so library retention counts
                // from now (DESIGN.md §9, "Keeping sessions"): as the lease is taken, not each time this run starts claude
                // again. That makes the record longer, so what the session takes is read again.
                size = await Task.Run(() =>
                {
                    library.Leases.TakeOver(sessionId, library.Library.GetSessionFolder(sessionId));
                    library.Library.MarkOpened(sessionId);
                    return library.Library.GetSessionSize(sessionId);
                });
            }
            SetSyncedSize(sessionId, size);
            switch (status)
            {
                case LeaseStatus.HeldByOther other:
                    await OnTakenOverAsync(other.Machine);
                    return false;
                default:
                    // Mine, or taken above. Unreadable: perhaps a lease mid-sync. The session still works here; the copy
                    // after the next turn takes the lease if it's free by then, and refuses if another machine has it.
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The library folder may be unavailable (a sync client offline): the session still works on this machine.
            return true;
        }
    }

    private static T? Copy<T>(T value) where T : class, new() => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonFileStore<T>.Options), JsonFileStore<T>.Options);

    private void ReleaseLease()
    {
        if (State.SessionId is { } sessionId && TakenOverBy is null)
        {
            // Deleting a file in the library folder: not on the UI thread.
            var leases = _services.Library.Leases;
            _ = Task.Run(() => leases.Release(sessionId));
        }
    }
}
