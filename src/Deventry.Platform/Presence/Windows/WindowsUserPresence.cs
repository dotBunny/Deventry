using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Deventry.Core.Remote;

namespace Deventry.Platform.Presence.Windows;

/// <summary>
/// Whether the user is at a Windows computer (DESIGN.md §18, "When remote clients send messages"): the session locked,
/// from <c>WTSQuerySessionInformation</c>; the screen saver, from <c>SPI_GETSCREENSAVERRUNNING</c>; the time since the
/// last input, from <c>GetLastInputInfo</c>; and the display asleep, which <c>PowerSettingRegisterNotification</c> tells
/// as it happens, with no window to receive it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsUserPresence : IUserPresence
{
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private const uint DeviceNotifyCallback = 2;
    private const uint PowerSettingChange = 0x8013;
    private const uint ScreenSaverRunning = 0x0072;
    private const int CurrentSession = -1;
    private const int SessionInfoEx = 25;
    private const int SessionStateLocked = 0;

    private readonly Lock _lock = new();
    private nint _registration;
    private nint _parameters;
    private GCHandle _self;

    /// <summary>The display as last told: 0 off, 1 on, 2 dimmed. On until told otherwise.</summary>
    private int _display = 1;

    public PresenceSignals Signals => PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.DisplayAsleep | PresenceSignals.Idle;

    public event Action? Changed;

    public Task<PresenceReading> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PresenceReading(IsLocked(), IsScreenSaverRunning(), Volatile.Read(ref _display) == 0, Idle()));

    public void Start()
    {
        lock (_lock)
        {
            if (_registration != 0)
            {
                return;
            }
            _self = GCHandle.Alloc(this);
            // DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS: the callback, and what it's given back. Kept until unregistered.
            _parameters = Marshal.AllocHGlobal(2 * sizeof(nint));
            ((nint*)_parameters)[0] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, uint>)&OnPowerSetting;
            ((nint*)_parameters)[1] = GCHandle.ToIntPtr(_self);
            var setting = ConsoleDisplayState;
            if (PowerSettingRegisterNotification(&setting, DeviceNotifyCallback, _parameters, out var registration) == 0)
            {
                _registration = registration;
                return;
            }
            // Without it, the display isn't known asleep: idle time still counts.
            Free();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_registration != 0)
            {
                PowerSettingUnregisterNotification(_registration);
                _registration = 0;
            }
            Free();
            Volatile.Write(ref _display, 1);
        }
    }

    private void Free()
    {
        if (_parameters != 0)
        {
            Marshal.FreeHGlobal(_parameters);
            _parameters = 0;
        }
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    public void Dispose() => Stop();

    /// <summary>The display came on, went off or dimmed. Called by Windows on a thread of its own.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnPowerSetting(nint context, uint type, nint setting)
    {
        try
        {
            // POWERBROADCAST_SETTING: the setting's GUID, the data's length, then the data: a DWORD here.
            if (type == PowerSettingChange && setting != 0 && *(uint*)(setting + 16) >= 4
                && GCHandle.FromIntPtr(context).Target is WindowsUserPresence presence)
            {
                var state = (int)*(uint*)(setting + 20);
                if (Interlocked.Exchange(ref presence._display, state) != state)
                {
                    presence.Changed?.Invoke();
                }
            }
        }
        catch
        {
            // Nothing may escape into Windows.
        }
        return 0;
    }

    private static bool IsLocked()
    {
        if (!WTSQuerySessionInformation(0, CurrentSession, SessionInfoEx, out var buffer, out var bytes))
        {
            return false;
        }
        try
        {
            // WTSINFOEXW: its level, then WTSINFOEX_LEVEL1_W's session id, state and flags.
            return bytes >= 16 && *(int*)buffer == 1 && *(int*)(buffer + 12) == SessionStateLocked;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static bool IsScreenSaverRunning() => SystemParametersInfo(ScreenSaverRunning, 0, out var running, 0) && running != 0;

    private static TimeSpan? Idle()
    {
        var info = new LastInputInfo { Size = (uint)sizeof(LastInputInfo) };
        return GetLastInputInfo(ref info)
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time))
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingRegisterNotification(Guid* settingGuid, uint flags, nint recipient, out nint registration);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingUnregisterNotification(nint registration);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(nint server, int sessionId, int infoClass, out nint buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);
}
