using Deventry.Core.Processes;
using Deventry.Core.Remote;
using Deventry.Platform.Presence.Mac;
using Deventry.Platform.Presence.Windows;

namespace Deventry.Platform.Presence;

/// <summary>
/// Whether the user is at the computer, for when the remote clients send messages (DESIGN.md §18, "When remote clients
/// send messages"): locking, the screen saver, the display asleep and the time since the last input, as each system
/// tells them.
/// </summary>
public static class UserPresences
{
    public static IUserPresence CreateForCurrentOS(IProcessLauncher launcher, TimeProvider time) =>
        OperatingSystem.IsWindows() ? new WindowsUserPresence()
        : OperatingSystem.IsMacOS() ? new MacUserPresence()
        : OperatingSystem.IsLinux() ? new LinuxUserPresence(launcher, time)
        : new NoUserPresence();
}
