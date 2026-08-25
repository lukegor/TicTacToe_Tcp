using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientServer.App.ViewModels;

internal sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly Func<string, int, CancellationToken, Task<PlayerSession>> _connectSessionFactory;
    private readonly Func<int, ServerTcp> _hostFactory;
    private readonly Func<ServerTcp, LobbyService> _lobbyFactory;
    private readonly IUiDispatcher _ui;
    private readonly CancellationTokenSource _closed = new();

    public event Action<PlayerSession>? LobbyReady;
    public event Action<int, ServerTcp, LobbyService>? RefereeReady;

    [ObservableProperty] public partial string Address { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial string Port { get; set; } = "1111";
    [ObservableProperty] public partial string PlayerName { get; set; } = "";
    [ObservableProperty] public partial string HostPort { get; set; } = "1111";
    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ConnectionViewModel(
        Func<string, int, CancellationToken, Task<PlayerSession>> connectSessionFactory,
        Func<int, ServerTcp> hostFactory,
        Func<ServerTcp, LobbyService> lobbyFactory,
        IUiDispatcher ui)
    {
        _connectSessionFactory = connectSessionFactory;
        _hostFactory = hostFactory;
        _lobbyFactory = lobbyFactory;
        _ui = ui;
    }

    /// <summary>Called by the window when it closes; pending connects stop
    /// before raising LobbyReady into a dead view.</summary>
    public void Cancel() => _closed.Cancel();

    public void Dispose() => _closed.Dispose();

    /// <summary>Appends a line to the connection log (used by the window's
    /// connect-factory for transport-level failure messages).</summary>
    public void AppendLog(string message) => LogText += message + Environment.NewLine;

    private static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text?.Trim(), CultureInfo.InvariantCulture, out port)
        && port is > 0 and <= IPEndPoint.MaxPort;

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            AppendLog("Enter an IP address.");
            return;
        }

        if (!TryParsePort(Port, out int port))
        {
            AppendLog($"'{Port}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        PlayerSession? session = null;
        try
        {
            session = await _connectSessionFactory(Address.Trim(), port, cancellationToken)
                .ConfigureAwait(true);
            AppendLog($"Connected to {Address.Trim()}:{port}.");
            await session.ConnectAsync().ConfigureAwait(true);
            LobbyReady?.Invoke(session);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                        or IOException or SocketException or InvalidOperationException)
        {
            AppendLog($"Connection failed: {ex.Message}");
            session?.Dispose();
        }
    }

    [RelayCommand]
    private void Host()
    {
        if (!TryParsePort(HostPort, out int port))
        {
            AppendLog($"'{HostPort}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        ServerTcp server = _hostFactory(port);
        try
        {
            server.Start();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            AppendLog($"Could not start the host: {ex.Message}");
            server.Dispose();
            return;
        }

        AppendLog($"Listening on port {port}.");
        LobbyService lobby = _lobbyFactory(server);
        lobby.Start();
        RefereeReady?.Invoke(port, server, lobby);
    }
}
