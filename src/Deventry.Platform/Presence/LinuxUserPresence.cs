using System.Globalization;
using Deventry.Core.Processes;
using Deventry.Core.Remote;

namespace Deventry.Platform.Presence;

/// <summary>
/// Whether the user is at a Linux desktop (DESIGN.md §18, "When remote clients send messages"), asked over D-Bus with
/// <c>gdbus</c> at each read: logind's session <c>LockedHint</c> and <c>IdleHint</c>, the screen saver's
/// <c>GetActive</c> (freedesktop's, else GNOME's), and the idle time GNOME (<c>org.gnome.Mutter.IdleMonitor</c>) or KDE
/// (<c>org.freedesktop.ScreenSaver.GetSessionIdleTime</c>) gives. What a desktop doesn't answer is left out, and
/// <see cref="Signals"/> says what answered.
/// </summary>
internal sealed class LinuxUserPresence(IProcessLauncher launcher, TimeProvider time) : IUserPresence
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private const string Session = "/org/freedesktop/login1/session/auto";

    private PresenceSignals _signals = PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.Idle;

    /// <summary>What answered the last read; before the first, what most desktops give.</summary>
    public PresenceSignals Signals => _signals;

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

    public async Task<PresenceReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var locked = GdbusReply.Bool(await CallAsync(["--system", "--dest", "org.freedesktop.login1", "--object-path", Session,
            "--method", "org.freedesktop.DBus.Properties.Get", "org.freedesktop.login1.Session", "LockedHint"], cancellationToken).ConfigureAwait(false));
        var idleHint = GdbusReply.Bool(await CallAsync(["--system", "--dest", "org.freedesktop.login1", "--object-path", Session,
            "--method", "org.freedesktop.DBus.Properties.Get", "org.freedesktop.login1.Session", "IdleHint"], cancellationToken).ConfigureAwait(false));
        var screenSaver = GdbusReply.Bool(await CallAsync(["--session", "--dest", "org.freedesktop.ScreenSaver", "--object-path", "/org/freedesktop/ScreenSaver",
                "--method", "org.freedesktop.ScreenSaver.GetActive"], cancellationToken).ConfigureAwait(false))
            ?? GdbusReply.Bool(await CallAsync(["--session", "--dest", "org.gnome.ScreenSaver", "--object-path", "/org/gnome/ScreenSaver",
                "--method", "org.gnome.ScreenSaver.GetActive"], cancellationToken).ConfigureAwait(false));
        TimeSpan? idle = GdbusReply.Number(await CallAsync(["--session", "--dest", "org.gnome.Mutter.IdleMonitor", "--object-path", "/org/gnome/Mutter/IdleMonitor/Core",
            "--method", "org.gnome.Mutter.IdleMonitor.GetIdletime"], cancellationToken).ConfigureAwait(false)) is { } milliseconds
            ? TimeSpan.FromMilliseconds(milliseconds)
            : GdbusReply.Number(await CallAsync(["--session", "--dest", "org.freedesktop.ScreenSaver", "--object-path", "/org/freedesktop/ScreenSaver",
                "--method", "org.freedesktop.ScreenSaver.GetSessionIdleTime"], cancellationToken).ConfigureAwait(false)) is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : null;
        // logind's own idle hint stands in for the time when no desktop gives it.
        if (idle is null && idleHint == true)
        {
            idle = TimeSpan.MaxValue;
        }
        _signals = (locked is null ? 0 : PresenceSignals.Locked)
            | (screenSaver is null ? 0 : PresenceSignals.ScreenSaver)
            | (idle is null && idleHint is null ? 0 : PresenceSignals.Idle);
        return new PresenceReading(locked == true, screenSaver == true, false, idle);
    }

    /// <summary>One <c>gdbus call</c>: its output, or null when it failed or <c>gdbus</c> isn't there.</summary>
    private async Task<string?> CallAsync(string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(launcher, new ProcessStartSpec("gdbus", ["call", .. arguments]), Timeout, time, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StandardOutput : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>What <c>gdbus call</c> prints, read: <c>(&lt;true&gt;,)</c>, <c>(false,)</c>, <c>(uint64 12345,)</c>.</summary>
internal static class GdbusReply
{
    /// <summary>The reply's first value as a boolean; null when it isn't one.</summary>
    public static bool? Bool(string? output) => First(output) switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>The reply's first value as a number; null when it isn't one.</summary>
    public static double? Number(string? output) =>
        double.TryParse(First(output), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0 ? number : null;

    /// <summary>The first value, without the tuple's brackets, a variant's angle brackets or a type's name.</summary>
    private static string? First(string? output)
    {
        if (output?.Trim() is not { Length: > 0 } text)
        {
            return null;
        }
        var value = text.TrimStart('(').Split(',')[0].Trim().Trim('<', '>').Trim();
        var space = value.LastIndexOf(' ');
        return space >= 0 ? value[(space + 1)..] : value;
    }
}
