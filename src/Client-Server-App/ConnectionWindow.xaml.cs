using System.IO;
using System.Net.Sockets;
using System.Windows;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using Microsoft.Extensions.Logging;

namespace ClientServer.App;

public partial class ConnectionWindow : Window
{
    private readonly Func<LobbyWindow, bool>? _lobbyProbe;
    private readonly Func<ServerWindow, bool>? _refereeProbe;
    private readonly ConnectionViewModel _viewModel;

    public ConnectionWindow() : this(null, null, null)
    {
    }

    internal ConnectionWindow(
        Func<LobbyWindow, bool>? lobbyProbe,
        Func<ServerWindow, bool>? refereeProbe,
        IUiDispatcher? dispatcher)
    {
        _lobbyProbe = lobbyProbe;
        _refereeProbe = refereeProbe;
        SynchronizationContext context = SynchronizationContext.Current
            ?? new SynchronizationContext();
        InitializeComponent();
        _viewModel = new ConnectionViewModel(
            ConnectSessionAsync,
            port => new ServerTcp(port, App.LoggerFactory.CreateLogger<ServerTcp>()),
            server => new LobbyService(server, logger: App.LoggerFactory.CreateLogger<LobbyService>()),
            dispatcher ?? new SynchronizationContextDispatcher(context));
        DataContext = _viewModel;
        _viewModel.LobbyReady += session =>
            OpenLobbyWindow(() => new LobbyWindow(session), () => session.Dispose());
        _viewModel.RefereeReady += (port, server, lobby) =>
            OpenRefereeWindow(() => new ServerWindow(lobby, port), () =>
            {
                lobby.Dispose();
                server.Dispose();
            });
    }

    /// <summary>Creates the transport, reconnect-factory and session — the exact
    /// wiring the pre-MVVM click handler performed.</summary>
    private async Task<PlayerSession> ConnectSessionAsync(
        string host, int port, CancellationToken cancellationToken)
    {
        ClientTcp client = new(App.LoggerFactory.CreateLogger<ClientTcp>());
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(true);
            _viewModel.AppendLog($"Connected to {host}:{port}.");

            ClientTcp? initialTransport = client;
            async Task<IClientTransport> ReconnectFactory()
            {
                ClientTcp? reused = Interlocked.Exchange(ref initialTransport, null);
                if (reused is not null)
                {
                    return reused;
                }

                ClientTcp fresh = new(App.LoggerFactory.CreateLogger<ClientTcp>());
                await fresh.ConnectAsync(host, port, CancellationToken.None).ConfigureAwait(true);
                return fresh;
            }

            PlayerSession session = new(ReconnectFactory, displayName: NameFromViewModel(),
                logger: App.LoggerFactory.CreateLogger<PlayerSession>());
            return session;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                        or IOException or SocketException)
        {
            client.Dispose();
            _viewModel.AppendLog($"Connection failed: {ex.Message}");
            throw;
        }
    }

    private string NameFromViewModel() => _viewModel.PlayerName;

    private void OpenLobbyWindow(Func<LobbyWindow> createWindow, Action onClose)
    {
        LobbyWindow window = createWindow();
        if (_lobbyProbe?.Invoke(window) == true)
        {
            window.Closed += (_, _) => onClose();
            return;
        }

        if (IsVisible)
        {
            window.Owner = this;
        }

        window.Closed += (_, _) => onClose();
        window.Show();
    }

    private void OpenRefereeWindow(Func<ServerWindow> createWindow, Action onClose)
    {
        ServerWindow window = createWindow();
        if (_refereeProbe?.Invoke(window) == true)
        {
            window.Closed += (_, _) => onClose();
            return;
        }

        if (IsVisible)
        {
            window.Owner = this;
        }

        window.Closed += (_, _) => onClose();
        window.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cancel();
        base.OnClosed(e);
    }
}
