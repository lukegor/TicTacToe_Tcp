using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Asynchronous TCP server that accepts multiple clients and broadcasts
/// newline-delimited UTF-8 messages to every connected client.
/// </summary>
internal sealed class ServerTcp : IServerTransport, IDisposable
{
    private readonly ConcurrentDictionary<TcpClient, StreamWriter> _clients = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _broadcastLock = new(initialCount: 1, maxCount: 1);
    private readonly TcpListener _listener;
    private bool _disposed;

    /// <summary>Raised (on a worker thread) whenever a client sends a message.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Raised (on a worker thread) when any client completes the TCP handshake.</summary>
    public event Action? ClientConnected;

    /// <summary>Raised (on a worker thread) when a client leaves.</summary>
    public event Action? ClientDisconnected;

    /// <summary>The bound port; only meaningful after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ServerTcp(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    /// <summary>Starts listening and accepting clients asynchronously.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener.Start();
        _ = AcceptLoopAsync(_cancellation.Token);
    }

    /// <summary>Sends <paramref name="message"/> followed by a newline to all connected clients.</summary>
    public async Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        await _broadcastLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (KeyValuePair<TcpClient, StreamWriter> entry in _clients)
            {
                await entry.Value.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _broadcastLock.Release();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                RegisterClient(client);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Expected when the server stops or the listener faults.
        }
    }

    private void RegisterClient(TcpClient client)
    {
        _clients[client] = new StreamWriter(client.GetStream(), Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        _ = HandleClientAsync(client);
        ClientConnected?.Invoke();
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream());
            while (await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false) is { } message)
            {
                MessageReceived?.Invoke(message);
                await BroadcastLineAsync(message, _cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            // Expected when a client disconnects or the server shuts down.
        }
        finally
        {
            RemoveClient(client);
        }
    }

    private void RemoveClient(TcpClient client)
    {
        if (_clients.TryRemove(client, out StreamWriter? writer))
        {
            writer.Dispose();
        }

        client.Dispose();
        ClientDisconnected?.Invoke();
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellation.Cancel();
            _listener.Stop();

            foreach (KeyValuePair<TcpClient, StreamWriter> entry in _clients)
            {
                entry.Value.Dispose();
                entry.Key.Dispose();
            }

            _clients.Clear();
            _broadcastLock.Dispose();
            _cancellation.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
