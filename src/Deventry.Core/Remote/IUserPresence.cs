namespace Deventry.Core.Remote;

/// <summary>What a desktop can say about whether someone is at the computer.</summary>
[Flags]
public enum PresenceSignals
{
    None = 0,
    Locked = 1,
    ScreenSaver = 2,
    DisplayAsleep = 4,
    Idle = 8,
}

/// <summary>Whether someone is at the computer, as far as the OS says. A signal it can't read reads as false and null.</summary>
/// <param name="Idle">How long since the keyboard or mouse was last used; null when the OS doesn't say.</param>
public readonly record struct PresenceReading(bool Locked, bool ScreenSaver, bool DisplayAsleep, TimeSpan? Idle)
{
    public static PresenceReading Unknown => default;
}

/// <summary>
/// Whether the user is at the computer (DESIGN.md §18, "When remote clients send messages"): locked, the screen saver,
/// the display asleep, and the time since the last input. The implementations are in Deventry.Platform; each reads what
/// its desktop gives.
/// </summary>
public interface IUserPresence : IDisposable
{
    /// <summary>What this desktop can tell. With none, only Deventry not being in front counts as away, and Settings says so.</summary>
    PresenceSignals Signals { get; }

    /// <summary>Reads everything it can now. Never throws: what can't be read is unknown.</summary>
    Task<PresenceReading> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised on any thread when the OS says something changed at once, such as the display going to sleep.</summary>
    event Action? Changed;

    /// <summary>Starts listening to the OS, while a remote client is on.</summary>
    void Start();

    /// <summary>Stops listening.</summary>
    void Stop();
}

/// <summary>For a desktop Deventry can't ask, and tests: it can't tell anything.</summary>
public sealed class NoUserPresence : IUserPresence
{
    public PresenceSignals Signals => PresenceSignals.None;

    public Task<PresenceReading> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(PresenceReading.Unknown);

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>When remote clients send messages (Settings → Remote clients → <b>When</b>).</summary>
public enum RemoteSendWhen
{
    /// <summary>Only while the user is away. The default.</summary>
    Away,

    /// <summary>Always: silently while the user is at the desk, so the chat mirrors everything.</summary>
    Always,
}

/// <summary>
/// Whether the user is away from the desk (DESIGN.md §18, "When remote clients send messages"): any of Deventry not in
/// front, the computer locked, its screen saver on or its display asleep, or nobody using its keyboard or mouse for
/// <b>Count me away after</b>. Back once none holds.
/// </summary>
public static class RemoteAway
{
    public const int DefaultMinutes = 5;

    public const int MinMinutes = 1;

    public const int MaxMinutes = 60;

    /// <summary>How often the idle time is read while a client is on.</summary>
    public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(30);

    public static bool IsAway(bool deventryInFront, PresenceReading reading, TimeSpan awayAfter) =>
        !deventryInFront || reading.Locked || reading.ScreenSaver || reading.DisplayAsleep || reading.Idle >= awayAfter;

    /// <summary>The minutes a setting holds, kept between 1 and 60.</summary>
    public static TimeSpan After(int minutes) => TimeSpan.FromMinutes(Math.Clamp(minutes, MinMinutes, MaxMinutes));

    /// <summary>
    /// Whether, and how, a message goes for an event the user wants: always loud while they're away; silent at the desk
    /// with <see cref="RemoteSendWhen.Always"/>, and not at all with <see cref="RemoteSendWhen.Away"/>.
    /// </summary>
    public static RemoteNotice Notice(bool wanted, bool away, RemoteSendWhen when) =>
        !wanted ? RemoteNotice.None
        : away ? RemoteNotice.Sound
        : when == RemoteSendWhen.Always ? RemoteNotice.Silent
        : RemoteNotice.None;

    /// <summary>What counts as away on this desktop, for Settings under <b>While I'm away</b>.</summary>
    public static string Describe(PresenceSignals signals, int minutes)
    {
        var parts = new List<string> { "Deventry isn't in front" };
        if (signals.HasFlag(PresenceSignals.Locked))
        {
            parts.Add("the computer is locked");
        }
        if (signals.HasFlag(PresenceSignals.ScreenSaver))
        {
            parts.Add("its screen saver is on");
        }
        if (signals.HasFlag(PresenceSignals.DisplayAsleep))
        {
            parts.Add("its display is asleep");
        }
        if (signals.HasFlag(PresenceSignals.Idle))
        {
            parts.Add(minutes == 1 ? "nobody has used its keyboard or mouse for a minute" : $"nobody has used its keyboard or mouse for {minutes} minutes");
        }
        var list = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} or {parts[^1]}";
        var away = $"Away is when {list}.";
        return signals == PresenceSignals.None
            ? $"{away} This desktop doesn't say whether it's locked or idle, so that's all Deventry can tell."
            : away;
    }
}
