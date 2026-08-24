using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Interaction logic for ConnectionWindow.xaml
/// </summary>
public partial class ConnectionWindow : Window
{
    private ClientTcp? _client;
    private ServerTcp? _server;

    public ConnectionWindow()
    {
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
        ClientTcp client = new();
        client.MessageReceived += message => AppendLog($"[server] {message}");
        client.Disconnected += () => AppendLog("Disconnected from the server.");
        _client = client;
        ConnectButton.IsEnabled = false;
        try
        {
            await client.ConnectAsync(host, port);
            AppendLog($"Connected to {host}:{port}.");

            TicTacToeClientService clientService = new(client);
            clientService.Start();
            OpenGameWindow(
                () => new GameWindow(clientService),
                onClose: () =>
                {
                    client.Dispose();
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

    private void HostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePort(HostPortTextBox.Text, out int port))
        {
            AppendLog($"'{HostPortTextBox.Text}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        _server?.Dispose();
        ServerTcp server = new(port);
        server.MessageReceived += message => AppendLog($"[client] {message}");
        try
        {
            server.Start();
            _server = server;
            AppendLog($"Listening on port {port}.");

            TicTacToeHostService hostService = new(server);
            hostService.Start();
            OpenGameWindow(
                () => new GameWindow(hostService),
                onClose: () =>
                {
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

    private void OpenGameWindow(Func<GameWindow> createWindow, Action onClose)
    {
        GameWindow gameWindow = createWindow();
        gameWindow.Owner = this;
        gameWindow.Closed += (_, _) => onClose();
        gameWindow.Show();
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
