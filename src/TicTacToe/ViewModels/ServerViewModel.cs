using System.Collections.ObjectModel;
using System.IO;
using TicTacToe.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TicTacToe.ViewModels;

internal sealed partial class ServerViewModel : ObservableObject
{
    private readonly LobbyService _lobby;
    private readonly IUiDispatcher _ui;
    private readonly string _logPath;

    public ObservableCollection<RoomInfoRecord> Rooms { get; } = [];

    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ServerViewModel(LobbyService lobby, IUiDispatcher ui, string? logPath = null)
    {
        _lobby = lobby;
        _ui = ui;
        _logPath = logPath ?? Path.Combine(AppContext.BaseDirectory, "artifacts", "vmdiag.log");
        RefreshRooms();
        lobby.RoomsChanged += () => _ui.Post(RefreshRooms);
        lobby.LogReceived += message => _ui.Post(() => LogText += message + Environment.NewLine);
    }

    private void RefreshRooms()
    {
        var snapshot = _lobby.GetRooms();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_logPath)!);
        System.IO.File.AppendAllText(
            _logPath,
            $"REFRESH count={snapshot.Count} [{string.Join(",", snapshot.Select(r => r.Name))}]{Environment.NewLine}");
        Rooms.Clear();
        foreach (RoomInfoRecord room in snapshot)
        {
            Rooms.Add(room);
        }
    }
}
