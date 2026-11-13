using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Deventry.Core.Remote;
using Deventry.Platform.Notifications.Mac;

namespace Deventry.Platform.Presence.Mac;

/// <summary>
/// Whether the user is at a Mac (DESIGN.md §18, "When remote clients send messages"): locking and the screen saver from
/// the distributed notifications <c>com.apple.screenIsLocked</c>, <c>screenIsUnlocked</c>,
/// <c>com.apple.screensaver.didstart</c> and <c>didstop</c>; the display asleep from <c>NSWorkspace</c>'s
/// <c>screensDidSleep</c> and <c>screensDidWake</c>; and the time since the last input from
/// <c>CGEventSourceSecondsSinceLastEventType</c>. The notifications come on the main thread's run loop, which
/// Avalonia runs.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacUserPresence : IUserPresence
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary><c>kCGEventSourceStateCombinedSessionState</c>.</summary>
    private const int CombinedSessionState = 0;

    /// <summary><c>kCGAnyInputEventType</c>.</summary>
    private const uint AnyInputEvent = uint.MaxValue;

    private static readonly (string Selector, string Name, bool Workspace)[] Observed =
    [
        ("deventryLocked:", "com.apple.screenIsLocked", false),
        ("deventryUnlocked:", "com.apple.screenIsUnlocked", false),
        ("deventrySaverStarted:", "com.apple.screensaver.didstart", false),
        ("deventrySaverStopped:", "com.apple.screensaver.didstop", false),
        ("deventryScreensSlept:", "NSWorkspaceScreensDidSleepNotification", true),
        ("deventryScreensWoke:", "NSWorkspaceScreensDidWakeNotification", true),
    ];

    /// <summary>The one listening, which the observer's methods tell.</summary>
    private static MacUserPresence? _listening;

    private static nint _observerClass;

    private nint _observer;
    private volatile bool _locked;
    private volatile bool _screenSaver;
    private volatile bool _asleep;

    public PresenceSignals Signals => PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.DisplayAsleep | PresenceSignals.Idle;

    public event Action? Changed;

    public Task<PresenceReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        TimeSpan? idle;
        try
        {
            var seconds = CGEventSourceSecondsSinceLastEventType(CombinedSessionState, AnyInputEvent);
            idle = double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            idle = null;
        }
        return Task.FromResult(new PresenceReading(_locked, _screenSaver, _asleep, idle));
    }

    public void Start()
    {
        if (_observer != 0)
        {
            return;
        }
        try
        {
            _listening = this;
            _observer = ObjC.Send(ObjC.Send(ObserverClass(), "alloc"), "init");
            foreach (var (selector, name, workspace) in Observed)
            {
                var notification = ObjC.NSString(name);
                try
                {
                    ObjC.Send(Center(workspace), "addObserver:selector:name:object:", _observer, ObjC.Sel(selector), notification, 0);
                }
                finally
                {
                    ObjC.Release(notification);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Without AppKit there's nothing to listen to: idle time still counts.
            _observer = 0;
        }
    }

    public void Stop()
    {
        if (_observer == 0)
        {
            return;
        }
        ObjC.Send(Center(workspace: false), "removeObserver:", _observer);
        ObjC.Send(Center(workspace: true), "removeObserver:", _observer);
        ObjC.Release(_observer);
        _observer = 0;
        if (ReferenceEquals(_listening, this))
        {
            _listening = null;
        }
        _locked = _screenSaver = _asleep = false;
    }

    public void Dispose() => Stop();

    private static nint Center(bool workspace) => workspace
        ? ObjC.Send(ObjC.Send(ObjC.GetClass("NSWorkspace"), "sharedWorkspace"), "notificationCenter")
        : ObjC.Send(ObjC.GetClass("NSDistributedNotificationCenter"), "defaultCenter");

    /// <summary>The observer's class, made once: a method for each notification, telling the one listening.</summary>
    private static nint ObserverClass()
    {
        if (_observerClass != 0)
        {
            return _observerClass;
        }
        var cls = ObjC.GetClass("DeventryPresenceObserver");
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), "DeventryPresenceObserver", 0);
            ObjC.AddMethod(cls, ObjC.Sel("deventryLocked:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnLocked, "v@:@");
            ObjC.AddMethod(cls, ObjC.Sel("deventryUnlocked:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnUnlocked, "v@:@");
            ObjC.AddMethod(cls, ObjC.Sel("deventrySaverStarted:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnSaverStarted, "v@:@");
            ObjC.AddMethod(cls, ObjC.Sel("deventrySaverStopped:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnSaverStopped, "v@:@");
            ObjC.AddMethod(cls, ObjC.Sel("deventryScreensSlept:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScreensSlept, "v@:@");
            ObjC.AddMethod(cls, ObjC.Sel("deventryScreensWoke:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScreensWoke, "v@:@");
            ObjC.RegisterClassPair(cls);
        }
        return _observerClass = cls;
    }

    private static void Set(Action<MacUserPresence> change)
    {
        try
        {
            if (_listening is { } presence)
            {
                change(presence);
                presence.Changed?.Invoke();
            }
        }
        catch
        {
            // Nothing may escape into the Objective-C runtime.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLocked(nint self, nint selector, nint notification) => Set(p => p._locked = true);

    [UnmanagedCallersOnly]
    private static void OnUnlocked(nint self, nint selector, nint notification) => Set(p => p._locked = false);

    [UnmanagedCallersOnly]
    private static void OnSaverStarted(nint self, nint selector, nint notification) => Set(p => p._screenSaver = true);

    [UnmanagedCallersOnly]
    private static void OnSaverStopped(nint self, nint selector, nint notification) => Set(p => p._screenSaver = false);

    [UnmanagedCallersOnly]
    private static void OnScreensSlept(nint self, nint selector, nint notification) => Set(p => p._asleep = true);

    [UnmanagedCallersOnly]
    private static void OnScreensWoke(nint self, nint selector, nint notification) => Set(p => p._asleep = false);

    [LibraryImport(CoreGraphics)]
    private static partial double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
}
