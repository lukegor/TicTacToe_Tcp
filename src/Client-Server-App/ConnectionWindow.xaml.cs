using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using Microsoft.Extensions.Logging;

namespace ClientServer.App;

/// <summary>
/// Interaction logic for ConnectionWindow.xaml
/// </summary>
public partial class ConnectionWindow : Window
{
    private ClientTcp? _client;
    private ServerTcp? _server;
    private readonly Func<LobbyWindow, bool>? _lobbyProbe;
    private readonly Func<ServerWindow, bool>? _refereeProbe;

    public ConnectionWindow() : this(null, null)
    {
    }

    internal ConnectionWindow(Func<LobbyWindow, bool>? lobbyProbe, Func<ServerWindow, bool>? refereeProbe)
    {
        _lobbyProbe = lobbyProbe;
        _refereeProbe = refereeProbe;
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        string host = AddressTextBox.Text.Trim();
        if (host.Length == 0)
        {
            AppendLog("Enter an IP address.");
            return;
        }

        if (!TryParsePort(PortTextBox.Text, out int port))
        {
            AppendLog($"'{PortTextBox.Text}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        _client?.Dispose();
        ClientTcp client = new(App.LoggerFactory.CreateLogger<ClientTcp>());
        _client = client;
        ConnectButton.IsEnabled = false;
        try
        {
            await client.ConnectAsync(host, port);
            AppendLog($"Connected to {host}:{port}.");

            // First factory call reuses the already-connected socket; later calls
            // (reconnects) open fresh ones.
            ClientTcp? initialTransport = client;
            async Task<IClientTransport> ConnectFactory()
            {
                ClientTcp? transport = Interlocked.Exchange(ref initialTransport, null);
                if (transport is not null)
                {
                    return transport;
                }

                ClientTcp fresh = new(App.LoggerFactory.CreateLogger<ClientTcp>());
                await fresh.ConnectAsync(host, port);
                return fresh;
            }

            PlayerSession session = new(
                ConnectFactory,
                displayName: NameTextBox.Text,
                logger: App.LoggerFactory.CreateLogger<PlayerSession>());
            await session.ConnectAsync();

            OpenLobbyWindow(
                () => new LobbyWindow(session),
                onClose: () =>
                {
                    session.Dispose();
                    if (ReferenceEquals(_client, client))
                    {
                        _client = null;
                    }
                });
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            AppendLog($"Connection failed: {ex.Message}");
            client.Dispose();
            if (ReferenceEquals(_client, client))
            {
                _client = null;
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void OpenLobbyWindow(Func<LobbyWindow> createWindow, Action onClose)
    {
        LobbyWindow window = createWindow();
        if (_lobbyProbe?.Invoke(window) == true)
        {
            window.Closed += (_, _) => onClose();
            return;
        }

        window.Owner = this;
        window.Closed += (_, _) => onClose();
        window.Show();
    }

    private void HostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePort(HostPortTextBox.Text, out int port))
        {
            AppendLog($"'{HostPortTextBox.Text}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        _server?.Dispose();
        ServerTcp server = new(port, App.LoggerFactory.CreateLogger<ServerTcp>());
        try
        {
            server.Start();
            _server = server;
            AppendLog($"Listening on port {port}.");

            LobbyService lobby = new(server, logger: App.LoggerFactory.CreateLogger<LobbyService>());
            lobby.Start();
            OpenRefereeWindow(
                () => new ServerWindow(lobby, port),
                onClose: () =>
                {
                    lobby.Dispose();
                    server.Dispose();
                    if (ReferenceEquals(_server, server))
                    {
                        _server = null;
                    }
                });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            AppendLog($"Could not start the host: {ex.Message}");
            server.Dispose();
        }
    }

    private void OpenRefereeWindow(Func<ServerWindow> createWindow, Action onClose)
    {
        ServerWindow window = createWindow();
        if (_refereeProbe?.Invoke(window) == true)
        {
            window.Closed += (_, _) => onClose();
            return;
        }

        window.Owner = this;
        window.Closed += (_, _) => onClose();
        window.Show();
    }

    private static bool TryParsePort(string text, out int port)
    {
        return int.TryParse(text.Trim(), CultureInfo.InvariantCulture, out port)
            && port is > 0 and <= IPEndPoint.MaxPort;
    }

    private void AppendLog(string message) =>
        _ = Dispatcher.BeginInvoke(() => OutputTextBox.AppendText(message + Environment.NewLine));

    protected override void OnClosed(EventArgs e)
    {
        _client?.Dispose();
        _server?.Dispose();
        base.OnClosed(e);
    }
}
