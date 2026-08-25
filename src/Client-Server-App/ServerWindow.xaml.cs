using System.Windows;
using System.Windows.Controls;
using ClientServer.Core.Game;

namespace ClientServer.App;

/// <summary>Read-only referee view: live room table plus an event log.</summary>
public partial class ServerWindow : Window
{
    private readonly LobbyService _lobby;

    internal ServerWindow(LobbyService lobby, int port)
    {
        _lobby = lobby;
        InitializeComponent();
        Title = $"Referee — port {port}";
        lobby.LogReceived += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        lobby.RoomsChanged += () => Dispatcher.BeginInvoke(RenderRooms);
        RenderRooms();
    }

    private void AppendLog(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    private void RenderRooms()
    {
        RoomsPanel.Children.Clear();
        foreach (RoomInfoRecord room in _lobby.GetRooms())
        {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            TextBlock name = new() { Text = room.Name };
            TextBlock players = new() { Text = $"{room.Players}/2" };
            TextBlock spectators = new() { Text = room.Spectators.ToString() };
            Grid.SetColumn(players, 1);
            Grid.SetColumn(spectators, 2);

            row.Children.Add(name);
            row.Children.Add(players);
            row.Children.Add(spectators);
            row.Margin = new Thickness(0, 2, 0, 2);

            RoomsPanel.Children.Add(row);
        }
    }
}
