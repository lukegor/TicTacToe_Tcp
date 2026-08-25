using System.Collections.ObjectModel;
using ClientServer.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientServer.App.ViewModels;

internal sealed partial class ServerViewModel : ObservableObject
{
    private readonly LobbyService _lobby;
    private readonly IUiDispatcher _ui;

    public ObservableCollection<RoomInfoRecord> Rooms { get; } = [];

    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ServerViewModel(LobbyService lobby, IUiDispatcher ui)
    {
        _lobby = lobby;
        _ui = ui;
        RefreshRooms();
        lobby.RoomsChanged += () => _ui.Post(RefreshRooms);
        lobby.LogReceived += message => _ui.Post(() => LogText += message + Environment.NewLine);
    }

    private void RefreshRooms()
    {
        var snapshot = _lobby.GetRooms();
        System.IO.File.AppendAllText(
            Path.Combine(AppContext.BaseDirectory, "artifacts", "vmdiag.log"),
            $"REFRESH count={snapshot.Count} [{string.Join(",", snapshot.Select(r => r.Name))}]{Environment.NewLine}");
        Rooms.Clear();
        foreach (RoomInfoRecord room in snapshot)
        {
            Rooms.Add(room);
        }
    }
}
