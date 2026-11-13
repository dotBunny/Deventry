using Deventry.Core.Remote;

namespace Deventry.App.Tests.Support;

/// <summary>Whether the user is at the computer, as the test says: never the machine's own.</summary>
internal sealed class FakeUserPresence : IUserPresence
{
    public PresenceSignals Signals { get; set; } = PresenceSignals.Locked | PresenceSignals.ScreenSaver | PresenceSignals.DisplayAsleep | PresenceSignals.Idle;

    /// <summary>What the next read says.</summary>
    public PresenceReading Reading { get; set; }

    public bool IsListening { get; private set; }

    public Task<PresenceReading> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Reading);

    public event Action? Changed;

    /// <summary>The OS says something changed at once, as the display going to sleep.</summary>
    public void Change(PresenceReading reading)
    {
        Reading = reading;
        Changed?.Invoke();
    }

    public void Start() => IsListening = true;

    public void Stop() => IsListening = false;

    public void Dispose()
    {
    }
}
