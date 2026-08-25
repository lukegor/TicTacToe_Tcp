using System.Windows;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;

namespace ClientServer.App;

/// <summary>
/// Tic-tac-toe board driven by a <see cref="PlayerSession"/>. Works for players
/// and spectators; handles the reconnecting banner and the Leave flow.
/// </summary>
public partial class GameWindow : Window
{
    private readonly PlayerSession _session;

    internal GameViewModel ViewModel => (GameViewModel)DataContext;

    internal GameWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        DataContext = new GameViewModel(session,
            new SynchronizationContextDispatcher(SynchronizationContext.Current!));
        session.ReturnedToLobby += _ => Dispatcher.BeginInvoke(Close);
    }

    protected override void OnClosed(EventArgs e)
    {
        _ = _session.LeaveRoomAsync();
        base.OnClosed(e);
    }
}
