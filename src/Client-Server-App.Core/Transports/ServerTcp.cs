using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ClientServer.Core.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientServer.Core.Transports;

/// <summary>
/// Asynchronous TCP server that tags every accepted connection with a GUID and
/// exchanges newline-delimited UTF-8 lines: broadcast or targeted.
/// </summary>
internal sealed class ServerTcp : IServerTransport, IDisposable
{
    private readonly ConcurrentDictionary<Guid, StreamWriter> _clients = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _writeLock = new(initialCount: 1, maxCount: 1);
    private readonly TcpListener _listener;
    private readonly ILogger<ServerTcp> _logger;
    private bool _disposed;

    /// <summary>Raised (on a worker thread) with the new connection's id.</summary>
    public event Action<Guid>? ClientConnected;

    /// <summary>Raised (on a worker thread) with the departed connection's id.</summary>
    public event Action<Guid>? ClientDisconnected;

    /// <summary>Raised (on a worker thread) for every line a connection sends.</summary>
    public event Action<Guid, string>? MessageReceived;

    /// <summary>The bound port; only meaningful after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ServerTcp(int port, ILogger<ServerTcp>? logger = null)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _logger = logger ?? NullLogger<ServerTcp>.Instance;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener.Start();
        _logger.LogDebug("Referee listening on port {Port}.", Port);
        _ = AcceptLoopAsync(_cancellation.Token);
    }

    public async Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        Guid[] recipients;
        lock (_clients)
        {
            recipients = [.. _clients.Keys];
        }

        await WriteToRecipients(recipients, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendToAsync(Guid id, string message, CancellationToken cancellationToken = default)
    {
        await WriteToRecipients([id], message, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteToRecipients(Guid[] recipients, string message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            char[] buffer = (message + "\n").ToCharArray();
            foreach (Guid recipient in recipients)
            {
                if (_clients.TryGetValue(recipient, out StreamWriter? writer))
                {
                    await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _writeLock.Release();
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
        Guid id = Guid.NewGuid();
        _clients[id] = new StreamWriter(client.GetStream(), Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        _logger.LogDebug("Client {Id} connected.", id);
        _ = HandleClientAsync(id, client);
        ClientConnected?.Invoke(id);
    }

    private async Task HandleClientAsync(Guid id, TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream());
            while (await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false) is { } line)
            {
                MessageReceived?.Invoke(id, line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            // Expected when a client disconnects or the server shuts down.
        }
        finally
        {
            RemoveClient(id, client);
        }
    }

    private void RemoveClient(Guid id, TcpClient client)
    {
        if (_clients.TryRemove(id, out StreamWriter? writer))
        {
            writer.Dispose();
        }

        client.Dispose();
        _logger.LogDebug("Client {Id} disconnected.", id);
        ClientDisconnected?.Invoke(id);
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

            foreach (KeyValuePair<Guid, StreamWriter> entry in _clients)
            {
                entry.Value.Dispose();
            }

            _clients.Clear();
            _writeLock.Dispose();
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
