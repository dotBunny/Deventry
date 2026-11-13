using Deventry.Core.LoginItems;
using Deventry.Core.Settings;
using Deventry.Core.Updates;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → General (DESIGN.md §14): starting at login and restoring tabs, closing a working tab, keeping the computer
/// awake, Claude's service status and Deventry's own updates. Cleaning up worktrees is on Git (<see cref="GitPage"/>).
/// Dispose it with the window.
/// </summary>
public sealed partial class GeneralPage : SettingsPage, IDisposable
{
    private LoginItemStatus? _loginItem;
    private bool _changingLoginItem;

    public GeneralPage(SettingsContext context) : base(context, SettingsCategory.General)
    {
        Section("starting", "Starting and closing",
        [
            // The OS's entry, not settings.json: never reset, and never synced.
            Setting("state.startAtLogin", "Start Deventry when I log in", null, null, keywords: ["login item", "startup", "launch at login"], machineOnly: true),
            Setting("sessions.restoreUnpinnedTabs", "Also restore unpinned tabs when Deventry starts", s => s.Sessions.RestoreUnpinnedTabs, (s, v) => s.Sessions.RestoreUnpinnedTabs = v,
                "Pinned tabs always come back.", keywords: ["reopen", "session restore"]),
            Setting("sessions.unloadIdleTabs", "Unload idle tabs to save memory", s => s.Sessions.UnloadIdleTabs, (s, v) => s.Sessions.UnloadIdleTabs = v,
                "A tab left idle stops its Claude Code. Its conversation stays, and your next message starts it again where it was.",
                "A tab isn't unloaded while Claude works or waits for you, while tasks, subagents or programs Claude started run in it, or while it's connected to the Claude app.",
                ["memory", "ram", "sleep", "suspend", "idle", "unload"]),
            Setting("sessions.unloadIdleTabsAfterMinutes", "After being idle for (minutes)", s => s.Sessions.UnloadIdleTabsAfterMinutes, (s, v) => s.Sessions.UnloadIdleTabsAfterMinutes = v,
                keywords: ["memory", "idle", "unload"]),
            Setting("general.confirmCloseWorkingTab", "Confirm before closing a tab where Claude is working",
                s => s.General.ConfirmCloseWorkingTab, (s, v) => s.General.ConfirmCloseWorkingTab = v,
                "Closing one stops Claude mid-turn."),
        ]);
        Section("keepAwake", "Keeping the computer awake",
        [
            Setting("general.keepAwake", "Keep this computer awake", s => s.General.KeepAwake, (s, v) => s.General.KeepAwake = v,
                "So a task a usage limit stopped can carry on by itself when the limit resets.",
                "Only sleep is held off: the screen can still turn off, and the screensaver can still start. Closing a laptop's lid may still put it to sleep. The cup in the header turns this on and off, and steams while the computer is kept awake.",
                ["sleep", "caffeinate", "cup", "insomnia"]),
            Setting("general.keepAwakeWhen", "When", s => s.General.KeepAwakeWhen, (s, v) => s.General.KeepAwakeWhen = v,
                more: "Claude has work to finish while a turn runs, while Claude Code keeps tasks running in the background, or while a task waits for a usage limit to reset so it can carry on. Once there's none, the computer can sleep again a couple of minutes later."),
            Setting("claudeCode.keepAwakeWhileConnected", "Also while tabs are connected to the Claude app",
                s => s.ClaudeCode.KeepAwakeWhileConnected, (s, v) => s.ClaudeCode.KeepAwakeWhileConnected = v,
                "So the Claude app can still reach them. The screen can still turn off.", keywords: ["remote control", "phone"]),
            Setting("remoteClients.keepAwake", "Also while a remote client is on",
                s => s.RemoteClients.KeepAwake, (s, v) => s.RemoteClients.KeepAwake = v,
                "A sleeping computer can't answer the Telegram bot. The screen can still turn off.", keywords: ["telegram", "bot", "phone"]),
        ]);
        Section("updates", "Updates and status",
        [
            Setting("general.showServiceStatus", "Show Claude's service status", s => s.General.ShowServiceStatus, (s, v) => s.General.ShowServiceStatus = v,
                "A dot in the header, and a banner while Claude has an incident.",
                "Read from status.claude.com every 5 minutes: Claude Code, the Claude API and claude.ai. Off, it isn't checked at all.",
                ["outage", "incident", "status page"]),
            Setting("general.checkForAppUpdates", "Check for Deventry updates automatically", s => s.General.CheckForAppUpdates, (s, v) => s.General.CheckForAppUpdates = v,
                "From Deventry's releases on GitHub, at launch and every few hours.", keywords: ["version", "upgrade"]),
            Setting("general.includePrereleases", "Include pre-releases", s => s.General.IncludePrereleases, (s, v) => s.General.IncludePrereleases = v,
                "Try new versions before they're released.", keywords: ["beta", "preview"]),
            Info("general.version", "Deventry version", keywords: ["update now", "check now", "release notes", "restart into new builds"]),
        ]);
        _ = RefreshStartAtLoginAsync();
        Services.KeepAwake.PropertyChanged += OnKeepAwakeChanged;
    }

    public override string IconKey => "IconSettings";

    public override string Description => "Starting Deventry, keeping the computer awake, and updates.";

    // ---- Starting at login (DESIGN.md §9, "Starting at login") ----------------------------------------------------

    /// <summary>
    /// Reads whether Deventry starts at login, as the window opens: Task Manager, System Settings or another copy of
    /// Deventry may have changed it since (DESIGN.md §9, "Starting at login").
    /// </summary>
    public async Task RefreshStartAtLoginAsync() => UseLoginItem(await Services.StartAtLogin.ReadAsync());

    /// <summary>
    /// <b>Start Deventry when I log in</b>. It's this machine's, and lives in the OS rather than in settings.json, so
    /// Reset to defaults leaves it.
    /// </summary>
    public bool StartsAtLogin
    {
        get => _loginItem?.IsOn == true;
        set
        {
            if (value != StartsAtLogin && CanChangeStartAtLogin)
            {
                _ = SetStartAtLoginAsync(value);
            }
        }
    }

    public bool CanChangeStartAtLogin => _loginItem?.CanChange == true && !_changingLoginItem;

    /// <summary>What happens at login, and which copy of Deventry starts when it isn't simply this one.</summary>
    public string StartAtLoginText
    {
        get
        {
            const string opens = "Deventry opens minimized when you log in to this computer.";
            var current = Services.ThisCopy;
            return _loginItem?.Starts switch
            {
                { } starts when !starts.IsSameCopy(current) => $"{opens} It starts {starts.Describe()} rather than this {current.KindName}.",
                { Kind: AppInstallKind.SourceBuild } => $"{opens} It starts this source build's newest build. Once Deventry is installed, the installed one starts instead.",
                _ => opens,
            };
        }
    }

    /// <summary>Why the switch can't be changed here, such as being turned off in Task Manager.</summary>
    public string? StartAtLoginNote => _loginItem?.Note;

    public bool HasStartAtLoginNote => StartAtLoginNote is not null;

    public string? StartAtLoginError => _loginItem?.Error;

    public bool HasStartAtLoginError => StartAtLoginError is not null;

    private async Task SetStartAtLoginAsync(bool on)
    {
        _changingLoginItem = true;
        OnPropertyChanged(nameof(CanChangeStartAtLogin));
        try
        {
            UseLoginItem(await Services.StartAtLogin.SetAsync(on));
        }
        finally
        {
            _changingLoginItem = false;
            OnPropertyChanged(nameof(CanChangeStartAtLogin));
        }
    }

    /// <summary>What the OS says now: everything the switch and its notes show follows it.</summary>
    private void UseLoginItem(LoginItemStatus status)
    {
        _loginItem = status;
        OnPropertyChanged(string.Empty);
    }

    // ---- Tabs ----------------------------------------------------------------------------------------------------------

    public bool RestoreUnpinnedTabs
    {
        get => Settings.Sessions.RestoreUnpinnedTabs;
        set => Set(value, v => Settings.Sessions.RestoreUnpinnedTabs = v);
    }

    public bool UnloadIdleTabs
    {
        get => Settings.Sessions.UnloadIdleTabs;
        set => Set(value, v => Settings.Sessions.UnloadIdleTabs = v);
    }

    public decimal? UnloadIdleTabsAfterMinutes
    {
        get => Settings.Sessions.UnloadIdleTabsAfterMinutes;
        set => Set(value, v => Settings.Sessions.UnloadIdleTabsAfterMinutes = Math.Clamp((int)(v ?? new SessionSettings().UnloadIdleTabsAfterMinutes), 1, 24 * 60));
    }

    public bool ConfirmCloseWorkingTab
    {
        get => Settings.General.ConfirmCloseWorkingTab;
        set => Set(value, v => Settings.General.ConfirmCloseWorkingTab = v);
    }

    /// <summary>
    /// The header's dot and the banner, from status.claude.com (DESIGN.md §18, "Service status"). Off stops the checks.
    /// </summary>
    public bool ShowServiceStatus
    {
        get => Settings.General.ShowServiceStatus;
        set => Set(value, v => Settings.General.ShowServiceStatus = v);
    }

    // ---- Keeping the computer awake (DESIGN.md §9, "Keeping the computer awake") ----------------------------------

    /// <summary><b>Keep this computer awake</b>: the same switch as the header's cup.</summary>
    public bool KeepAwake
    {
        get => Settings.General.KeepAwake;
        set => Set(value, v => Settings.General.KeepAwake = v);
    }

    /// <summary>Only while Claude has work to finish: a turn, background tasks, or a task waiting for a limit to reset.</summary>
    public bool KeepAwakeWhileClaudeHasWork
    {
        get => Settings.General.KeepAwakeWhen == KeepAwakeWhen.ClaudeHasWork;
        set
        {
            if (value)
            {
                SetKeepAwakeWhen(KeepAwakeWhen.ClaudeHasWork);
            }
        }
    }

    /// <summary>Whenever Deventry is running.</summary>
    public bool KeepAwakeWhileDeventryRuns
    {
        get => Settings.General.KeepAwakeWhen == KeepAwakeWhen.DeventryRuns;
        set
        {
            if (value)
            {
                SetKeepAwakeWhen(KeepAwakeWhen.DeventryRuns);
            }
        }
    }

    private void SetKeepAwakeWhen(KeepAwakeWhen when)
    {
        if (Settings.General.KeepAwakeWhen == when)
        {
            return;
        }
        Settings.General.KeepAwakeWhen = when;
        OnPropertyChanged(nameof(KeepAwakeWhileClaudeHasWork));
        OnPropertyChanged(nameof(KeepAwakeWhileDeventryRuns));
        Save();
    }

    /// <summary><b>Also while tabs are connected to the Claude app</b>: system sleep only; the display can still sleep.</summary>
    /// <summary><b>Also while a remote client is on</b> (DESIGN.md §18, "Remote clients").</summary>
    public bool KeepAwakeWhileRemoteClient
    {
        get => Settings.RemoteClients.KeepAwake;
        set => Set(value, v => Settings.RemoteClients.KeepAwake = v);
    }

    public bool KeepAwakeWhileConnected
    {
        get => Settings.ClaudeCode.KeepAwakeWhileConnected;
        set => Set(value, v => Settings.ClaudeCode.KeepAwakeWhileConnected = v);
    }

    /// <summary>Why this computer can't be kept awake, or null when it can.</summary>
    public string? KeepAwakeUnavailableText => Services.KeepAwake.IsAvailable ? null : Services.KeepAwake.Tooltip;

    /// <summary>The header's cup changed the switch while the window is open: the page follows.</summary>
    private void OnKeepAwakeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(KeepAwake));
        OnPropertyChanged(nameof(KeepAwakeWhileClaudeHasWork));
        OnPropertyChanged(nameof(KeepAwakeWhileDeventryRuns));
    }

    public void Dispose() => Services.KeepAwake.PropertyChanged -= OnKeepAwakeChanged;

    // ---- Deventry's own updates (DESIGN.md §2, "Updating Deventry") ---------------------------------------------

    /// <summary>Check GitHub for new Deventry releases.</summary>
    public bool CheckForAppUpdates
    {
        get => Settings.General.CheckForAppUpdates;
        set => Set(value, v => Settings.General.CheckForAppUpdates = v);
    }

    public bool IncludePrereleases
    {
        get => Settings.General.IncludePrereleases;
        set
        {
            Set(value, v => Settings.General.IncludePrereleases = v);
            // Takes effect with the next check: do that now.
            AppUpdates?.CheckNowCommand.Execute(null);
        }
    }

    /// <summary>Deventry's version, update status and actions, the same as the sidebar's badge. Null in some tests.</summary>
    public AppUpdateViewModel? AppUpdates { get; internal set; }

    public bool HasAppUpdates => AppUpdates is not null && SourceUpdates is null;

    /// <summary>For a source build: its checkout's new commits upstream, in place of releases (DESIGN.md §9, "New commits upstream").</summary>
    public SourceUpdateViewModel? SourceUpdates { get; internal set; }

    public bool HasSourceUpdates => SourceUpdates is not null;

    /// <summary>A copy from the Microsoft Store is the Store's to update, so the release settings don't apply to it.</summary>
    public bool ShowsUpdateChecks => AppUpdates?.IsFromStore != true;

    /// <summary>Pre-releases are GitHub's: not for a source build, which follows its checkout, or a copy from the Store.</summary>
    public bool ShowsPrereleases => ShowsUpdateChecks && !HasSourceUpdates;
}
