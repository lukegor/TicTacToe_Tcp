using System.Collections.ObjectModel;
using ClientServer.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientServer.App.ViewModels;

internal sealed partial class LobbyViewModel : ObservableObject
{
    private readonly PlayerSession _session;
    private readonly IUiDispatcher _ui;

    public event Action<JoinedRecord>? Seated;
    public event Action<string>? ReturnedToLobby;

    public ObservableCollection<RoomInfoRecord> Rooms { get; } = [];

    [ObservableProperty] public partial string RoomNameInput { get; set; } = "";
    [ObservableProperty] public partial string LogText { get; set; } = "";

    public LobbyViewModel(PlayerSession session, IUiDispatcher ui)
    {
        _session = session;
        _ui = ui;
        RefreshRooms(session.LatestRooms);

        session.RoomsUpdated += rooms => _ui.Post(() => RefreshRooms(rooms));
        session.Seated += joined => _ui.Post(() => Seated?.Invoke(joined));
        session.ReturnedToLobby += reason =>
        {
            _ui.Post(() =>
            {
                RefreshRooms(_session.LatestRooms);
                ReturnedToLobby?.Invoke(reason);
            });
        };
        session.ErrorReceived += message => AppendNotice(message);
        session.LogReceived += message => AppendNotice(message);
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        string name = RoomNameInput.Trim();
        if (name.Length == 0)
        {
            AppendNotice("Enter a room name first.");
            return;
        }

        try
        {
            await _session.CreateRoomAsync(name);
            RoomNameInput = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    [RelayCommand]
    private async Task JoinRoomAsync(RoomInfoRecord? room)
    {
        if (room is null)
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

    private void RefreshRooms(IReadOnlyList<RoomInfoRecord> rooms)
    {
        Rooms.Clear();
        foreach (RoomInfoRecord room in rooms)
        {
            Rooms.Add(room);
        }
    }

    private void AppendNotice(string message) => LogText += message + Environment.NewLine;
}
