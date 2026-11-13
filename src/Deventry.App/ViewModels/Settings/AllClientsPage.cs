using Deventry.App.Services;
using Deventry.Core.Remote;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → Remote clients → All clients (DESIGN.md §18, "Remote clients"): what every remote client can do, and
/// when they send messages and what they say, one rule for all of them. Each client has a page of its own after it
/// (<see cref="RemoteClientPage"/>). All of it this computer's.
/// </summary>
public sealed class AllClientsPage : SettingsPage, IDisposable
{
    public AllClientsPage(SettingsContext context) : base(context, SettingsCategory.AllClients)
    {
        Section("access", "What they can do",
        [
            Setting("remoteClients.access", "Remote clients can", s => s.RemoteClients.Access, (s, v) => s.RemoteClients.Access = v,
                more: "Only at the desk, at any level: saving an Always allow rule, changing a tab's mode, model, account or settings, rewinding, closing a tab, and an MCP server's form.",
                keywords: ["permission", "answer", "stop", "send"]),
            Setting("remoteClients.openTabs", "Open new tabs", s => s.RemoteClients.OpenTabs, (s, v) => s.RemoteClients.OpenTabs = v,
                "With /new, in a favorite or recent folder. A folder that asks about trust asks here.", keywords: ["new tab", "folder"]),
        ]);
        Section("messages", "Messages",
        [
            Setting("remoteClients.needsInput", "A tab needs permission or an answer", s => s.RemoteClients.NeedsInput, (s, v) => s.RemoteClients.NeedsInput = v,
                keywords: ["prompt", "question"]),
            Setting("remoteClients.turnFinished", "A tab finishes its turn", s => s.RemoteClients.TurnFinished, (s, v) => s.RemoteClients.TurnFinished = v),
            Setting("remoteClients.processErrors", "A tab's Claude Code stops with an error", s => s.RemoteClients.ProcessErrors, (s, v) => s.RemoteClients.ProcessErrors = v),
            Setting("remoteClients.usageAlerts", "Usage alerts and accounts' spend warnings", s => s.RemoteClients.UsageAlerts, (s, v) => s.RemoteClients.UsageAlerts = v),
            Setting("remoteClients.projectActions", "A project action finishes", s => s.RemoteClients.ProjectActions, (s, v) => s.RemoteClients.ProjectActions = v),
            Setting("remoteClients.when", "When", s => s.RemoteClients.When, (s, v) => s.RemoteClients.When = v, keywords: ["away", "idle", "locked"]),
            Setting("remoteClients.awayAfterMinutes", "Count me away after (minutes)", s => s.RemoteClients.AwayAfterMinutes, (s, v) => s.RemoteClients.AwayAfterMinutes = v,
                "Without the keyboard or mouse.", keywords: ["idle"]),
            Setting("remoteClients.detail", "What they say", s => s.RemoteClients.Detail, (s, v) => s.RemoteClients.Detail = v,
                keywords: ["privacy", "detail"]),
        ], description: "A message answering what you did from your phone always comes.");
        Remote.Changed += OnRemoteChanged;
    }

    private RemoteClientsService Remote => Services.RemoteClients;

    public override string IconKey => RemoteClientIcons.Any;

    public override string Description => "What every remote client can do, and when they send messages.";

    public void Dispose() => Remote.Changed -= OnRemoteChanged;

    private void OnRemoteChanged() => OnPropertyChanged(nameof(AwayText));

    // ---- What they can do -------------------------------------------------------------------------------------------

    public bool AccessWatch
    {
        get => Settings.RemoteClients.Access == RemoteAccessLevel.Watch;
        set => SetAccess(value, RemoteAccessLevel.Watch);
    }

    public bool AccessAnswer
    {
        get => Settings.RemoteClients.Access == RemoteAccessLevel.Answer;
        set => SetAccess(value, RemoteAccessLevel.Answer);
    }

    public bool AccessFull
    {
        get => Settings.RemoteClients.Access == RemoteAccessLevel.Full;
        set => SetAccess(value, RemoteAccessLevel.Full);
    }

    private void SetAccess(bool on, RemoteAccessLevel level)
    {
        if (!on || Settings.RemoteClients.Access == level)
        {
            return;
        }
        Settings.RemoteClients.Access = level;
        Save();
        OnPropertyChanged(nameof(AccessWatch));
        OnPropertyChanged(nameof(AccessAnswer));
        OnPropertyChanged(nameof(AccessFull));
    }

    public bool OpenTabs
    {
        get => Settings.RemoteClients.OpenTabs;
        set => Set(value, v => Settings.RemoteClients.OpenTabs = v);
    }

    // ---- Messages ---------------------------------------------------------------------------------------------------

    public bool SendNeedsInput
    {
        get => Settings.RemoteClients.NeedsInput;
        set => Set(value, v => Settings.RemoteClients.NeedsInput = v);
    }

    public bool SendTurnFinished
    {
        get => Settings.RemoteClients.TurnFinished;
        set => Set(value, v => Settings.RemoteClients.TurnFinished = v);
    }

    public bool SendProcessErrors
    {
        get => Settings.RemoteClients.ProcessErrors;
        set => Set(value, v => Settings.RemoteClients.ProcessErrors = v);
    }

    public bool SendUsageAlerts
    {
        get => Settings.RemoteClients.UsageAlerts;
        set => Set(value, v => Settings.RemoteClients.UsageAlerts = v);
    }

    public bool SendProjectActions
    {
        get => Settings.RemoteClients.ProjectActions;
        set => Set(value, v => Settings.RemoteClients.ProjectActions = v);
    }

    public bool WhenAway
    {
        get => Settings.RemoteClients.When == RemoteSendWhen.Away;
        set => SetWhen(value, RemoteSendWhen.Away);
    }

    public bool WhenAlways
    {
        get => Settings.RemoteClients.When == RemoteSendWhen.Always;
        set => SetWhen(value, RemoteSendWhen.Always);
    }

    private void SetWhen(bool on, RemoteSendWhen when)
    {
        if (!on || Settings.RemoteClients.When == when)
        {
            return;
        }
        Settings.RemoteClients.When = when;
        Save();
        OnPropertyChanged(nameof(WhenAway));
        OnPropertyChanged(nameof(WhenAlways));
    }

    /// <summary>What counts as away on this desktop.</summary>
    public string AwayText => RemoteAway.Describe(Remote.PresenceSignals, Settings.RemoteClients.AwayAfterMinutes);

    public decimal AwayAfterMinutes
    {
        get => Settings.RemoteClients.AwayAfterMinutes;
        set
        {
            Set(Math.Clamp((int)value, RemoteAway.MinMinutes, RemoteAway.MaxMinutes), v => Settings.RemoteClients.AwayAfterMinutes = v);
            OnPropertyChanged(nameof(AwayText));
        }
    }

    public bool SayEverything
    {
        get => Settings.RemoteClients.Detail == RemoteDetail.Everything;
        set => SetDetail(value, RemoteDetail.Everything);
    }

    public bool SayJustWhatHappened
    {
        get => Settings.RemoteClients.Detail == RemoteDetail.JustWhatHappened;
        set => SetDetail(value, RemoteDetail.JustWhatHappened);
    }

    private void SetDetail(bool on, RemoteDetail detail)
    {
        if (!on || Settings.RemoteClients.Detail == detail)
        {
            return;
        }
        Settings.RemoteClients.Detail = detail;
        Save();
        OnPropertyChanged(nameof(SayEverything));
        OnPropertyChanged(nameof(SayJustWhatHappened));
    }

    protected override void OnReset()
    {
        OnPropertyChanged(string.Empty);
    }
}
