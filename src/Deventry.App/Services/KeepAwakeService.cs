using Deventry.App.ViewModels;
using Deventry.Core.RemoteControl;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.Services;

/// <summary>
/// What Claude has on across the tabs, for <b>Keep this computer awake</b> (DESIGN.md §9, "Keeping the computer awake").
/// </summary>
/// <param name="Working">Tabs running a turn, not waiting on the user.</param>
/// <param name="Background">Tasks and agents Claude Code keeps running after a turn, in every tab.</param>
/// <param name="WaitingForLimit">Tabs waiting for a usage limit to reset, to carry on by themselves.</param>
public readonly record struct ClaudeWork(int Working, int Background, int WaitingForLimit)
{
    public bool Any => Working + Background + WaitingForLimit > 0;
}

/// <summary>
/// Keeps the computer from sleeping (DESIGN.md §9, "Keeping the computer awake"): while Settings → General's <b>Keep
/// this computer awake</b> is on, only while Claude has work to finish or whenever Deventry runs, and while tabs are
/// connected to the Claude app (DESIGN.md §18). Only system sleep: the screen can still turn off. The header's cup
/// shows it and turns the setting on and off. Owns the <see cref="ISleepBlocker"/>. Use on the UI thread.
/// </summary>
public sealed partial class KeepAwakeService : ObservableObject, IDisposable
{
    /// <summary>
    /// How long sleep stays held off after Claude's work ends, while only that held it: the next turn often starts
    /// straight after (a queued message, a check-in), and holding on is cheaper than letting go and taking it back.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly AppServices _services;
    private readonly ISleepBlocker _sleep;
    private readonly UiTimeout _grace;
    private ClaudeWork _work;
    private int _connectedTabs;
    private bool _remoteClientOn;

    public KeepAwakeService(AppServices services, ISleepBlocker sleep)
    {
        _services = services;
        _sleep = sleep;
        _grace = new UiTimeout(services.Time, services.Dispatcher);
        Update();
    }

    private GeneralSettings Settings => _services.Settings.General;

    /// <summary>
    /// <b>Keep this computer awake</b>, which the header's cup turns on and off. Saved, so it holds at the next launch
    /// too.
    /// </summary>
    public bool IsOn
    {
        get => Settings.KeepAwake;
        set
        {
            if (value != Settings.KeepAwake)
            {
                Settings.KeepAwake = value;
                _services.SaveSettings();
            }
        }
    }

    /// <summary>When it holds sleep off, while it's on.</summary>
    public KeepAwakeWhen When => Settings.KeepAwakeWhen;

    public bool WhileClaudeHasWork => When == KeepAwakeWhen.ClaudeHasWork;

    public bool WhileDeventryRuns => When == KeepAwakeWhen.DeventryRuns;

    /// <summary>The cup's menu: when to keep the computer awake, which turns it on too.</summary>
    [RelayCommand]
    private void Choose(KeepAwakeWhen when)
    {
        Settings.KeepAwakeWhen = when;
        Settings.KeepAwake = true;
        _services.SaveSettings();
    }

    /// <summary>Sleep is held off now, for whatever reason: the cup steams.</summary>
    public bool IsKeepingAwake => _sleep.IsBlocking;

    /// <summary>This machine can be kept awake. When it can't, the cup is disabled and says why.</summary>
    public bool IsAvailable => _sleep.UnavailableReason is null;

    /// <summary>What the cup's tip says: what's happening and why, and what a click does.</summary>
    public string Tooltip => string.Join("\n", TooltipLines());

    /// <summary>The cup's name for screen readers, with its state.</summary>
    public string AccessibleName => IsKeepingAwake ? "Keep this computer awake: keeping it awake" : "Keep this computer awake";

    private IEnumerable<string> TooltipLines()
    {
        if (_sleep.UnavailableReason is { } reason)
        {
            yield return $"Deventry can't keep this computer awake here: {reason}";
            yield break;
        }
        if (IsKeepingAwake)
        {
            yield return "Keeping this computer awake. The screen can still turn off.";
            foreach (var why in Reasons())
            {
                yield return why;
            }
        }
        else
        {
            yield return IsOn ? "This computer can sleep: Claude has no work to finish." : "This computer can sleep.";
        }
        yield return IsOn
            ? "Click to let it sleep."
            : WhileClaudeHasWork ? "Click to keep it awake while Claude has work to finish." : "Click to keep it awake while Deventry runs.";
        yield return "Right-click to choose when.";
    }

    /// <summary>Why sleep is held off, a line each.</summary>
    private IEnumerable<string> Reasons()
    {
        if (IsOn && WhileDeventryRuns)
        {
            yield return "While Deventry runs, as Settings → General says.";
        }
        else if (IsOn && _work.Any)
        {
            yield return $"While Claude has work to finish: {DescribeWork(_work)}.";
        }
        else if (IsOn && _grace.IsPending)
        {
            yield return "Claude's work just finished: it can sleep again in a couple of minutes.";
        }
        if (_connectedTabs > 0 && _services.Settings.ClaudeCode.KeepAwakeWhileConnected)
        {
            yield return _connectedTabs == 1 ? "1 tab is connected to the Claude app." : $"{_connectedTabs} tabs are connected to the Claude app.";
        }
        if (WhileRemoteClient)
        {
            yield return "A remote client is on: a sleeping computer can't answer it.";
        }
    }

    /// <summary>"2 tabs working, 1 waiting for a usage limit to reset".</summary>
    private static string DescribeWork(ClaudeWork work)
    {
        var parts = new List<string>(3);
        if (work.Working > 0)
        {
            parts.Add(work.Working == 1 ? "1 tab working" : $"{work.Working} tabs working");
        }
        if (work.Background > 0)
        {
            parts.Add(work.Background == 1 ? "1 task running in the background" : $"{work.Background} tasks running in the background");
        }
        if (work.WaitingForLimit > 0)
        {
            parts.Add(work.WaitingForLimit == 1 ? "1 tab waiting for a usage limit to reset" : $"{work.WaitingForLimit} tabs waiting for a usage limit to reset");
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// What Claude has on, from the tabs. When the last of it ends, sleep is held off for <see cref="Grace"/> more, in
    /// case more starts.
    /// </summary>
    public void SetWork(ClaudeWork work)
    {
        if (work == _work)
        {
            return;
        }
        if (_work.Any && !work.Any)
        {
            _grace.Restart(Grace, Update);
        }
        else if (work.Any)
        {
            _grace.Cancel();
        }
        _work = work;
        Update();
    }

    /// <summary>How many tabs are connected to the Claude app (DESIGN.md §18, "Remote Control").</summary>
    public void SetConnectedTabs(int count)
    {
        if (count != _connectedTabs)
        {
            _connectedTabs = count;
            Update();
        }
    }

    /// <summary>
    /// A remote client is on, or not (DESIGN.md §18, "Remote clients"): while one is, sleep is held off, if Settings →
    /// General allows, since a sleeping computer can't answer it.
    /// </summary>
    public void SetRemoteClientOn(bool on)
    {
        if (on != _remoteClientOn)
        {
            _remoteClientOn = on;
            Update();
        }
    }

    private bool WhileRemoteClient => _remoteClientOn && _services.Settings.RemoteClients.KeepAwake;

    /// <summary>Settings → General, or Settings → Claude Code's <b>Keep this computer awake while tabs are connected</b>, may have changed.</summary>
    public void OnSettingsChanged() => Update();

    private bool Wanted =>
        (IsOn && (WhileDeventryRuns || _work.Any || _grace.IsPending))
        || (_connectedTabs > 0 && _services.Settings.ClaudeCode.KeepAwakeWhileConnected)
        || WhileRemoteClient;

    private void Update()
    {
        _sleep.SetBlocking(Wanted);
        // The cup follows: its state, its tip and its menu's checks.
        OnPropertyChanged(string.Empty);
    }

    /// <summary>For Settings → Advanced → Diagnostics.</summary>
    public string Describe()
    {
        var setting = !IsOn ? "Keep this computer awake is off."
            : WhileDeventryRuns ? "Keep this computer awake is on, while Deventry runs."
            : _work.Any ? $"Keep this computer awake is on, while Claude has work to finish: {DescribeWork(_work)}."
            : _grace.IsPending ? "Keep this computer awake is on, while Claude has work to finish: it just finished."
            : "Keep this computer awake is on, while Claude has work to finish: it has none.";
        var tabs = _connectedTabs switch
        {
            0 => "No tab is connected to the Claude app.",
            1 => "1 tab is connected to the Claude app.",
            var n => $"{n} tabs are connected to the Claude app.",
        };
        var connected = _services.Settings.ClaudeCode.KeepAwakeWhileConnected ? "" : " Keeping the computer awake for them is turned off in Settings → General.";
        var remote = !_remoteClientOn ? "" : _services.Settings.RemoteClients.KeepAwake
            ? " A remote client is on."
            : " A remote client is on; keeping the computer awake for it is turned off in Settings → General.";
        return $"{setting} {tabs}{connected}{remote} {_sleep.Describe()}";
    }

    /// <summary>At exit: the computer can sleep again.</summary>
    public void Dispose()
    {
        _grace.Dispose();
        _sleep.SetBlocking(false);
        _sleep.Dispose();
    }
}
