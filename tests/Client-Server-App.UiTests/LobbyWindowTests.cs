using System.Windows;
using System.Windows.Controls;
using ClientServer.App;
using ClientServer.Core.Game;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.UiTests;

public sealed class LobbyWindowTests : IDisposable
{
    private readonly PlayerSession _session;
    private readonly FakeClientTransport _transport;

    public LobbyWindowTests()
    {
        (_session, _transport) = UiTestSession.ConnectSeatedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _session.Dispose();

    [WpfFact]
    public async Task Construction_And_RoomListPush_RendersRooms()
    {
        LobbyWindow window = new(_session);

        _transport.ReceiveLine(GameJson.Serialize(new RoomListRecord(
            [new RoomInfoRecord("duel", 1, 0), new RoomInfoRecord("friday", 2, 3)])));
        await TestDispatcher.FlushAsync();

        var items = (IReadOnlyList<RoomInfoRecord>)window.RoomsList.ItemsSource;
        Assert.Equal(2, items.Count);
        Assert.Contains(items, r => r.Name == "friday" && r.Label == "2 player(s), 3 spectator(s)");
    }

    [WpfFact]
    public async Task CreateButton_ValidName_SendsEnvelope_ClearsInput()
    {
        LobbyWindow window = new(_session);
        window.RoomNameBox.Text = "  friday  ";

        UiAssert.Press(window.CreateButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains(_transport.SentLines, l => l.Contains("\"createRoom\"") && l.Contains("friday"));
        Assert.Equal(string.Empty, window.RoomNameBox.Text);
    }

    [WpfFact]
    public async Task CreateButton_EmptyName_ShowsNotice_SendsNothing()
    {
        LobbyWindow window = new(_session);
        window.RoomNameBox.Text = "   ";
        int sentBefore = _transport.SentLines.Count;

        UiAssert.Press(window.CreateButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains("Enter a room name first.", window.OutputTextBox.Text);
        Assert.Equal(sentBefore, _transport.SentLines.Count);
    }

    [WpfFact]
    public async Task JoinRow_PressGeneratedJoinButton_SendsJoinRoom()
    {
        LobbyWindow window = new(_session);
        _transport.ReceiveLine(GameJson.Serialize(new RoomListRecord([new RoomInfoRecord("duel", 1, 0)])));
        await TestDispatcher.FlushAsync();
        RealizeRoomsList(window);

        ListBoxItem container = (ListBoxItem)window.RoomsList.ItemContainerGenerator.ContainerFromIndex(0)!;
        Button joinButton = Assert.Single(VisualTreeEx.FindChildren<Button>(container), b => b.Content as string == "Join");

        int before = _transport.SentLines.Count;
        UiAssert.Press(joinButton);
        await TestDispatcher.FlushAsync();

        Assert.True(_transport.SentLines.Count > before);
        Assert.Contains(_transport.SentLines, l => l.Contains("\"joinRoom\"") && l.Contains("duel"));
    }

    [WpfFact]
    public async Task JoinRow_DisconnectedSession_SurfaceOperationError()
    {
        LobbyWindow window = new(_session);
        _transport.ReceiveLine(GameJson.Serialize(new RoomListRecord([new RoomInfoRecord("duel", 1, 0)])));
        await TestDispatcher.FlushAsync();
        RealizeRoomsList(window);
        _transport.SimulateDisconnect();

        ListBoxItem container = (ListBoxItem)window.RoomsList.ItemContainerGenerator.ContainerFromIndex(0)!;
        Button joinButton = Assert.Single(VisualTreeEx.FindChildren<Button>(container), b => b.Content as string == "Join");

        UiAssert.Press(joinButton);
        await TestDispatcher.FlushAsync();

        Assert.NotEqual(string.Empty, window.OutputTextBox.Text);
    }

    [WpfFact]
    public async Task ErrorEnvelope_AppendsNotice_And_ReturnedToLobby_RecoversList()
    {
        LobbyWindow window = new(_session);

        _transport.ReceiveLine(GameJson.Serialize(new ErrorRecord("Room 'x' does not exist.")));
        await TestDispatcher.FlushAsync();
        Assert.Contains("Room 'x' does not exist.", window.OutputTextBox.Text);

        // Seated client receives `left`: session returns to lobby; the next
        // roomList push must render again.
        _transport.ReceiveLine(GameJson.Serialize(new LeftRecord("room closed")));
        await TestDispatcher.FlushAsync();
        _transport.ReceiveLine(GameJson.Serialize(new RoomListRecord(
            [new RoomInfoRecord("friday", 2, 3)])));
        await TestDispatcher.FlushAsync();

        var items = (IReadOnlyList<RoomInfoRecord>)window.RoomsList.ItemsSource;
        _ = Assert.Single(items);
        Assert.Equal("friday", items[0].Name);
    }

    private static void RealizeRoomsList(LobbyWindow window)
    {
        window.RoomsList.Measure(new Size(400, 300));
        window.RoomsList.Arrange(new Rect(0, 0, 400, 300));
        window.RoomsList.UpdateLayout();
    }
}
