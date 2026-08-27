using System.IO;
using TicTacToe.ViewModels;
using TicTacToe.Core.Game;
using TicTacToe.TestSupport;
using Xunit;

namespace TicTacToe.UiTests.ViewModels;

public sealed class ServerViewModelTests : IDisposable
{
    private readonly string _logPath =
        Path.Combine(Path.GetTempPath(), "vmdiag-tests", Guid.NewGuid().ToString("N"), "vmdiag.log");

    [Fact]
    public void RoomEvents_RefreshRooms_AndLogAppends()
    {
        FakeServerTransport transport = new();
        using LobbyService lobby = new(transport);
        lobby.Start();
        ServerViewModel vm = new(lobby, new InlineDispatcher(), _logPath);
        Assert.Empty(vm.Rooms);

        Guid a = transport.SimulateClientConnected();
        transport.ReceiveLine(a, GameJson.Serialize(new HelloRecord("Alice")));
        transport.ReceiveLine(a, GameJson.Serialize(new CreateRoomRecord("duel")));

        _ = Assert.Single(vm.Rooms);
        Assert.Contains("connected.", vm.LogText);
    }

    public void Dispose()
    {
        string? dir = Path.GetDirectoryName(_logPath);
        if (dir != null && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
