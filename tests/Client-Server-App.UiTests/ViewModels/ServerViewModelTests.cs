using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class ServerViewModelTests
{
    [Fact]
    public void RoomEvents_RefreshRooms_AndLogAppends()
    {
        FakeServerTransport transport = new();
        using LobbyService lobby = new(transport);
        lobby.Start();
        ServerViewModel vm = new(lobby, new InlineDispatcher());
        Assert.Empty(vm.Rooms);

        Guid a = transport.SimulateClientConnected();
        transport.ReceiveLine(a, GameJson.Serialize(new HelloRecord("Alice")));
        transport.ReceiveLine(a, GameJson.Serialize(new CreateRoomRecord("duel")));

        _ = Assert.Single(vm.Rooms);
        Assert.Contains("connected.", vm.LogText);
    }
}
