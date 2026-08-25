using System.IO;
using System.Net.Sockets;
using System.Text;
using ClientServer.Core.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientServer.Core.Transports;

/// <summary>
/// Asynchronous TCP client that exchanges newline-delimited UTF-8 messages with a server.
/// </summary>
internal sealed class ClientTcp : IClientTransport, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ILogger<ClientTcp> _logger;

    private TcpClient? _client;
    private StreamWriter? _writer;
    private int _receiving;
    private bool _disposed;

    /// <summary>Raised (on a worker thread) whenever a message arrives from the server.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Raised (on a worker thread) when the connection to the server is lost.</summary>
    public event Action? Disconnected;

    public bool IsConnected => _client is { Connected: true };

    public ClientTcp(ILogger<ClientTcp>? logger = null)
    {
        _logger = logger ?? NullLogger<ClientTcp>.Instance;
    }

    /// <summary>Connects to <paramref name="host"/>:<paramref name="port"/> and starts listening for messages.</summary>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TcpClient client = new();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Connect to {Host}:{Port} failed: {Reason}", host, port, ex.Message);
            throw;
        }

        _client = client;
        _writer = new StreamWriter(client.GetStream(), Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        File.AppendAllText(
            Path.Combine(AppContext.BaseDirectory, "artifacts", "vmdiag.log"),
            $"ASSIGN hash={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this)}" +
            $" sockConnected={client.Connected}{Environment.NewLine}");
        _logger.LogDebug("Connected to {Host}:{Port}.", host, port);
    }

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TcpClient? client = _client ?? throw new InvalidOperationException($"The client is not connected. [diag hash={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this)} assigned={_client is not null} connected={_client?.Connected}]");
        if (Interlocked.Exchange(ref _receiving, 1) != 0)
        {
            return;
        }

        _ = ReceiveLoopAsync(client);
    }

    /// <summary>Sends <paramref name="message"/> followed by a newline.</summary>
    public async Task SendLineAsync(string message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StreamWriter? writer = _writer;
        if (writer is null)
        {
            throw new InvalidOperationException("The client is not connected.");
        }

        await writer.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream());
            while (await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false) is { } message)
            {
                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Expected during cancellation, shutdown, or a lost connection.
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException;

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellation.Cancel();
            _writer?.Dispose();
            _client?.Dispose();
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
