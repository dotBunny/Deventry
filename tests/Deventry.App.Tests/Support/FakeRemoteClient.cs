using Deventry.Core.Remote;

namespace Deventry.App.Tests.Support;

/// <summary>A remote client that keeps what the desk tells it, and acts through the desk as the test says.</summary>
internal sealed class FakeRemoteClient : IRemoteClient
{
    private readonly List<RemoteEvent> _events = [];

    public string Kind => "telegram";

    public string Name => "Telegram";

    public RemoteClientState State { get; private set; } = RemoteClientState.Off;

    public event Action<RemoteClientState>? StateChanged;

    public string? Identity { get; private set; }

    public event Action<string>? Identified;

    /// <summary>Its service says what its account is called, as a bot's <c>getMe</c> does.</summary>
    public void Identify(string name)
    {
        Identity = name;
        Identified?.Invoke(name);
    }

    public IRemoteDesk? Desk { get; private set; }

    public bool Stopped { get; private set; }

    public IReadOnlyList<RemoteEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<T> Of<T>() where T : RemoteEvent => [.. Events.OfType<T>()];

    public Task StartAsync(IRemoteDesk desk, CancellationToken cancellationToken = default)
    {
        Desk = desk;
        desk.EventRaised += OnEvent;
        State = RemoteClientState.Connected;
        StateChanged?.Invoke(State);
        return Task.CompletedTask;
    }

    private void OnEvent(RemoteEvent raised)
    {
        lock (_events)
        {
            _events.Add(raised);
        }
    }

    public Task StopAsync()
    {
        if (Desk is { } desk)
        {
            desk.EventRaised -= OnEvent;
        }
        Stopped = true;
        State = RemoteClientState.Off;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
