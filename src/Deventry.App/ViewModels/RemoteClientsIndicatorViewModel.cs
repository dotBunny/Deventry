using System.Globalization;
using Deventry.App.Services;
using Deventry.Core.Remote;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>What the header's remote clients icon says, the state that most needs the user winning.</summary>
public enum RemoteIndicatorLevel
{
    /// <summary>Connected, and the user is at the desk or messages are quiet: messages wait.</summary>
    Held,

    /// <summary>Connected, and the user is away: messages go to the phone.</summary>
    Away,

    /// <summary>A client is connecting, or trying again.</summary>
    Connecting,

    /// <summary>A client stopped and needs the user, or has no account paired.</summary>
    Trouble,
}

/// <summary>One client that's on, in the indicator's tooltip and menu.</summary>
/// <param name="SettingsTarget">Its page and section in Settings, for <b>Telegram settings…</b>: <c>Telegram#telegram</c>.</param>
public sealed record RemoteIndicatorClient(string Name, string IconKey, string Line, string Status, string SettingsTarget)
{
    public string SettingsText => $"{Name} settings…";
}

/// <summary>
/// The header's remote clients icon (DESIGN.md §18, "Remote clients", "The header's indicator"), beside the cup: shown
/// while any client is turned on, one broadcast glyph whatever the clients, colored by the state that most needs the
/// user, its arcs lighting up outward when a message goes or comes, with each client's state and the quiet in its
/// tooltip and menu. Use on the UI thread.
/// </summary>
public sealed partial class RemoteClientsIndicatorViewModel : ObservableObject, IDisposable
{
    /// <summary>How long each step of the arcs lighting up lasts: the inner on, the outer on, the inner off, the outer off.</summary>
    public static readonly TimeSpan FlickStep = TimeSpan.FromMilliseconds(130);

    /// <summary>How long the arcs stay lit with motion reduced: no wave, just the accent for a moment.</summary>
    public static readonly TimeSpan StillFlick = TimeSpan.FromMilliseconds(600);

    /// <summary>How long <b>Quiet for an hour</b> holds messages.</summary>
    public static readonly TimeSpan QuietFor = TimeSpan.FromHours(1);

    public const string AllClientsTarget = "All clients#messages";

    private readonly AppServices _services;
    private readonly RemoteClientsService _remote;
    private readonly UiTimeout _flick;
    private int _flickStep;

    public RemoteClientsIndicatorViewModel(AppServices services)
    {
        _services = services;
        _remote = services.RemoteClients;
        _flick = new UiTimeout(services.Time, services.Dispatcher);
        _remote.Changed += Refresh;
        _remote.ActivityHappened += OnActivity;
        services.SettingsChanged += OnSettingsChanged;
    }

    public void Dispose()
    {
        _remote.Changed -= Refresh;
        _remote.ActivityHappened -= OnActivity;
        _services.SettingsChanged -= OnSettingsChanged;
        _flick.Dispose();
    }

    private RemoteClientSettings Settings => _services.Settings.RemoteClients;

    private IReadOnlyList<IRemoteClientType> Enabled => [.. _remote.Types.Where(t => Settings.IsEnabled(t.Kind))];

    /// <summary>A client is turned on: the icon shows.</summary>
    public bool IsVisible => Enabled.Count > 0;

    public RemoteIndicatorLevel Level
    {
        get
        {
            var levels = Enabled.Select(LevelOf).ToList();
            return levels.Contains(RemoteIndicatorLevel.Trouble) ? RemoteIndicatorLevel.Trouble
                : levels.Contains(RemoteIndicatorLevel.Connecting) ? RemoteIndicatorLevel.Connecting
                : _remote.IsAway && _remote.QuietUntil is null ? RemoteIndicatorLevel.Away
                : RemoteIndicatorLevel.Held;
        }
    }

    public bool IsTrouble => Level == RemoteIndicatorLevel.Trouble;

    public bool IsConnecting => Level == RemoteIndicatorLevel.Connecting;

    public bool IsAway => Level == RemoteIndicatorLevel.Away;

    /// <summary>One client's own level: away or held are for all of them, so a healthy client is <see cref="RemoteIndicatorLevel.Held"/>.</summary>
    private RemoteIndicatorLevel LevelOf(IRemoteClientType type) => _remote.StateOf(type.Kind) switch
    {
        { Status: RemoteClientStatus.Failed } => RemoteIndicatorLevel.Trouble,
        { Status: RemoteClientStatus.Connected } when _remote.PairingOf(type.Kind).Accounts.Count == 0 => RemoteIndicatorLevel.Trouble,
        { Status: RemoteClientStatus.Connected } => RemoteIndicatorLevel.Held,
        // Connecting, or not started yet: Deventry starts the clients once the tabs are up.
        _ => RemoteIndicatorLevel.Connecting,
    };

    /// <summary>The clients that are on, each with its line for the tooltip and its part of the menu.</summary>
    public IReadOnlyList<RemoteIndicatorClient> Clients => [.. Enabled.Select(type =>
    {
        var status = StatusOf(type);
        var name = _remote.IdentityOf(type.Kind) is { } identity ? $"{type.Name} ({type.ShowIdentity(identity)})" : type.Name;
        return new RemoteIndicatorClient(type.Name, RemoteClientIcons.For(type.Kind), $"{name}: {status}", status, $"{type.Name}#{type.Kind}");
    })];

    private string StatusOf(IRemoteClientType type)
    {
        var state = _remote.StateOf(type.Kind);
        return state.Status switch
        {
            RemoteClientStatus.Connected when _remote.PairingOf(type.Kind).Accounts.Count == 0 => "connected, but no account is paired",
            RemoteClientStatus.Connected => _remote.LastSent(type.Kind) is { } sent ? $"connected, last sent {Time(sent)}" : "connected",
            RemoteClientStatus.Connecting => state.Reason is { } why ? $"connecting. {why}" : "connecting…",
            RemoteClientStatus.Failed => state.Reason is { } reason ? $"stopped. {reason}" : "stopped",
            _ => "starting…",
        };
    }

    /// <summary>What every client shares: away, at the desk, or quiet.</summary>
    public string SharedLine => _remote.QuietUntil is { } until
        ? $"Quiet until {Time(until)}: messages wait, but answers to what you do from your phone still come."
        : _remote.IsAway ? "You're away, so messages go to your phone."
        : Settings.When == RemoteSendWhen.Always ? "At the desk: messages come silently."
        : "At the desk: messages wait until you're away.";

    public string Tooltip => string.Join("\n", [.. Clients.Select(c => c.Line), SharedLine, "Click for its menu."]);

    /// <summary>Its name for screen readers, with what it says now.</summary>
    public string AccessibleName => $"Remote clients: {Level switch
    {
        RemoteIndicatorLevel.Trouble => "one needs you",
        RemoteIndicatorLevel.Connecting => "connecting",
        RemoteIndicatorLevel.Away => "messages go to your phone",
        _ => _remote.QuietUntil is not null ? "messages are quiet" : "messages wait until you're away",
    }}";

    // ---- Quiet ------------------------------------------------------------------------------------------------------

    public bool IsQuiet => _remote.QuietUntil is not null;

    public bool IsNotQuiet => !IsQuiet;

    /// <summary>"Messages are quiet until 15:30".</summary>
    public string QuietText => _remote.QuietUntil is { } until ? $"Messages are quiet until {Time(until)}" : "";

    /// <summary><b>Quiet for an hour</b>: the same as <c>/quiet</c>, for every client.</summary>
    [RelayCommand]
    private void QuietForAnHour() => _remote.SetQuiet(_services.Time.GetUtcNow() + QuietFor);

    /// <summary><b>Resume messages</b>.</summary>
    [RelayCommand]
    private void Resume() => _remote.SetQuiet(null);

    private string Time(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, _services.Time.LocalTimeZone).ToString("t", CultureInfo.CurrentCulture);

    // ---- The arcs lighting up -------------------------------------------------------------------------------------

    /// <summary>The arcs are lighting up: the ones not lit dim, so the lit ones read as a wave.</summary>
    [ObservableProperty]
    public partial bool IsFlicking { get; private set; }

    [ObservableProperty]
    public partial bool InnerLit { get; private set; }

    [ObservableProperty]
    public partial bool OuterLit { get; private set; }

    /// <summary>A client sent a message or got one: the arcs light up outward once, or with motion reduced, for a moment.</summary>
    private void OnActivity(string kind, RemoteActivity activity)
    {
        // Its last message's time is in the tooltip.
        OnPropertyChanged(nameof(Clients));
        OnPropertyChanged(nameof(Tooltip));
        IsFlicking = true;
        if (_services.ReduceMotion)
        {
            InnerLit = OuterLit = true;
            _flick.Restart(StillFlick, EndFlick);
            return;
        }
        _flickStep = 0;
        InnerLit = true;
        OuterLit = false;
        _flick.Restart(FlickStep, NextFlickStep);
    }

    private void NextFlickStep()
    {
        switch (++_flickStep)
        {
            case 1:
                OuterLit = true;
                break;
            case 2:
                InnerLit = false;
                break;
            default:
                EndFlick();
                return;
        }
        _flick.Restart(FlickStep, NextFlickStep);
    }

    private void EndFlick()
    {
        InnerLit = OuterLit = false;
        IsFlicking = false;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => OnPropertyChanged(string.Empty);
}
