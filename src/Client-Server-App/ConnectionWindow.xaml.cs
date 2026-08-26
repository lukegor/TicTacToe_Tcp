using System.Windows;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;

namespace ClientServer.App;

public partial class ConnectionWindow : Window
{
    private readonly Func<LobbyWindow, bool>? _lobbyProbe;
    private readonly Func<ServerWindow, bool>? _refereeProbe;
    private readonly ConnectionViewModel _viewModel;

    public ConnectionWindow() : this(null, null)
    {
    }

    internal ConnectionWindow(
        Func<LobbyWindow, bool>? lobbyProbe,
        Func<ServerWindow, bool>? refereeProbe)
    {
        _lobbyProbe = lobbyProbe;
        _refereeProbe = refereeProbe;
        SynchronizationContext context = SynchronizationContext.Current
            ?? new SynchronizationContext();
        InitializeComponent();
        _viewModel = new ConnectionViewModel(
            BuildInfrastructure(),
            new SynchronizationContextDispatcher(context));
        DataContext = _viewModel;
        _viewModel.LobbyReady += session =>
            OpenLobbyWindow(() => new LobbyWindow(session), () => session.Dispose());
        _viewModel.RefereeReady += handle =>
            OpenRefereeWindow(() => new ServerWindow(handle.Lobby, handle.Port), () =>
            {
                handle.Lobby.Dispose();
                handle.Server.Dispose();
            });
    }

    private IConnectionInfrastructure BuildInfrastructure() =>
        new RealConnectionInfrastructure(App.LoggerFactory,
            message => _viewModel.AppendLog(message));

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
        base.OnClosed(e);
    }
}
