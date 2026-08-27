using System.Windows;
using TicTacToe.ViewModels;
using TicTacToe.Core.Game;

namespace TicTacToe;

/// <summary>Read-only referee view: live room table plus an event log.</summary>
public partial class ServerWindow : Window
{
    internal ServerWindow(LobbyService lobby, int port)
    {
        InitializeComponent();
        Title = $"Referee — port {port}";
        DataContext = new ServerViewModel(lobby,
            new SynchronizationContextDispatcher(SynchronizationContext.Current!));
    }
}
