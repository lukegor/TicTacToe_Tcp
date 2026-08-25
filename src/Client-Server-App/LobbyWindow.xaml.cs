using System.Windows;
using System.Windows.Controls;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>Room browser: lists rooms pushed by the referee, creates or joins one.</summary>
public partial class LobbyWindow : Window
{
    private readonly PlayerSession _session;
    private GameWindow? _gameWindow;

    internal LobbyWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        Title = $"Lobby — {session.DisplayName}";
        session.RoomsUpdated += OnRoomsUpdated;
        session.Seated += OnSeated;
        session.ReturnedToLobby += OnReturnedToLobby;
        session.ErrorReceived += AppendNotice;
        session.LogReceived += AppendNotice;
        RoomsList.ItemsSource = _session.LatestRooms.ToList();
    }

    private void OnRoomsUpdated(IReadOnlyList<RoomInfoRecord> rooms) =>
        Dispatcher.BeginInvoke(() => RoomsList.ItemsSource = rooms.ToList());

    private void OnSeated(JoinedRecord joined) =>
        Dispatcher.BeginInvoke(OpenGameWindow);

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

    private void OnReturnedToLobby(string reason) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible && _gameWindow is null)
            {
                Show();
            }

            RoomsList.ItemsSource = _session.LatestRooms.ToList();
        });

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string name = RoomNameBox.Text.Trim();
        if (name.Length == 0)
        {
            AppendNotice("Enter a room name first.");
            return;
        }

        try
        {
            await _session.CreateRoomAsync(name);
            RoomNameBox.Text = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    private async void JoinButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not RoomInfoRecord room)
        {
            return;
        }

        try
        {
            await _session.JoinRoomAsync(room.Name);
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    private void AppendNotice(string message) =>
        Dispatcher.BeginInvoke(() => OutputTextBox.AppendText(message + Environment.NewLine));

    protected override void OnClosed(EventArgs e)
    {
        _session.Dispose();
        base.OnClosed(e);
    }
}
