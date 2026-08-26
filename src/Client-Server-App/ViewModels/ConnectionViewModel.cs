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
    private readonly IConnectionInfrastructure _infra;
    private readonly IUiDispatcher _ui;

    public event Action<PlayerSession>? LobbyReady;
    public event Action<RefereeHandle>? RefereeReady;

    [ObservableProperty] public partial string Address { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial string Port { get; set; } = "1111";
    [ObservableProperty] public partial string PlayerName { get; set; } = "";
    [ObservableProperty] public partial string HostPort { get; set; } = "1111";
    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ConnectionViewModel(IConnectionInfrastructure infra, IUiDispatcher ui)
    {
        _infra = infra;
        _ui = ui;
    }

    internal void AppendLog(string message) => LogText += message + Environment.NewLine;

    private static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text?.Trim(), CultureInfo.InvariantCulture, out port)
        && port is > 0 and <= IPEndPoint.MaxPort;

    [RelayCommand]
    private async Task ConnectAsync()
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
            session = await _infra.ConnectAsync(
                Address.Trim(), port, PlayerName.Trim(), CancellationToken.None);
            await session.ConnectAsync();
            LobbyReady?.Invoke(session);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                        or IOException or SocketException)
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

        try
        {
            RefereeReady?.Invoke(_infra.StartHost(port));
            AppendLog($"Listening on port {port}.");
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            AppendLog($"Could not start the host: {ex.Message}");
        }
    }
}
