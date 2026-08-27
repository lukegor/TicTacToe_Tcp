using System.Windows;
using TicTacToe.ViewModels;
using TicTacToe.Core.Game;

namespace TicTacToe;

public partial class LobbyWindow : Window
{
    private readonly PlayerSession _session;
    private GameWindow? _gameWindow;

    internal LobbyWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        Title = $"Lobby — {session.DisplayName}";
        var viewModel = new LobbyViewModel(session,
            new SynchronizationContextDispatcher(SynchronizationContext.Current!));
        DataContext = viewModel;
        viewModel.Seated += _ => Dispatcher.BeginInvoke(OpenGameWindow);
        viewModel.ReturnedToLobby += _ => Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible && _gameWindow is null)
            {
                Show();
            }
        });
    }

    private void OpenGameWindow()
    {
        if (_gameWindow is not null)
        {
            return; // rematch re-seating: the existing board window already refreshed itself
        }

        _gameWindow = new GameWindow(_session);
        _gameWindow.Owner = Owner;
        _gameWindow.Closed += (_, _) =>
        {
            _gameWindow = null;
            Show();
        };
        _gameWindow.Show();
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _session.Dispose();
        base.OnClosed(e);
    }
}
