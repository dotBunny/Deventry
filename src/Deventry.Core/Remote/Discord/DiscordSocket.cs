using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace Deventry.Core.Remote.Discord;

/// <summary>
/// A WebSocket to Discord's Gateway, as text messages: <see cref="DiscordWebSocket"/>, which tests replace. Sending is
/// safe from any thread, alongside one receive.
/// </summary>
public interface IDiscordSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri address, CancellationToken cancellationToken);

    Task SendAsync(string text, CancellationToken cancellationToken);

    /// <summary>The next message; null once the socket closed, when <see cref="CloseStatus"/> says why.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>The code it closed with: Discord's own, such as <c>4004</c> for a refused token; null before it closed, or when it broke off.</summary>
    int? CloseStatus { get; }

    string? CloseDescription { get; }

    /// <summary>Closes it with <paramref name="status"/>: <c>1000</c> ends the session, and anything else keeps it to resume.</summary>
    Task CloseAsync(int status, CancellationToken cancellationToken);
}

/// <summary>The Gateway's WebSocket, over .NET's <see cref="ClientWebSocket"/>.</summary>
public sealed class DiscordWebSocket(string userAgent) : IDiscordSocket
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sending = new(1, 1);

    public int? CloseStatus => (int?)_socket.CloseStatus;

    public string? CloseDescription => _socket.CloseStatusDescription;

    public Task ConnectAsync(Uri address, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("User-Agent", userAgent);
        return _socket.ConnectAsync(address, cancellationToken);
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task CloseAsync(int status, CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.CloseOutputAsync((WebSocketCloseStatus)status, null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sending.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _sending.Dispose();
        return ValueTask.CompletedTask;
    }
}
