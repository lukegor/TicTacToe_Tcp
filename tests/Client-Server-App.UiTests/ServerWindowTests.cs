using System.Windows.Controls;
using ClientServer.App;
using ClientServer.Core.Game;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.UiTests;

public sealed class ServerWindowTests : IDisposable
{
    private readonly FakeServerTransport _transport = new();
    private readonly LobbyService _lobby;

    public ServerWindowTests()
    {
        _lobby = new LobbyService(_transport);
        _lobby.Start();
    }

    public void Dispose() => _lobby.Dispose();

    private static void Hello(FakeServerTransport t, Guid id, string name) =>
        t.ReceiveLine(id, GameJson.Serialize(new HelloRecord(name)));

    [WpfFact]
    public async Task TwoPlayersSeated_RendersSingleRoomRow_WithCounts()
    {
        Guid a = _transport.SimulateClientConnected();
        Hello(_transport, a, "Alice");
        _transport.ReceiveLine(a, GameJson.Serialize(new CreateRoomRecord("duel")));
        Guid b = _transport.SimulateClientConnected();
        Hello(_transport, b, "Bob");
        _transport.ReceiveLine(b, GameJson.Serialize(new JoinRoomRecord("duel")));

        ServerWindow window = HeadlessWindow.Prepare(new ServerWindow(_lobby, 1234));
        await TestDispatcher.FlushAsync();

        Assert.Single(window.RoomsPanel.Children);
        var texts = ((Grid)window.RoomsPanel.Children[0]).Children
            .OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(["duel", "2/2", "0"], texts);
    }

    [WpfFact]
    public async Task MembershipChange_RerendersRows()
    {
        ServerWindow window = HeadlessWindow.Prepare(new ServerWindow(_lobby, 1234));
        Assert.Empty(window.RoomsPanel.Children);

        Guid a = _transport.SimulateClientConnected();
        Hello(_transport, a, "Alice");
        _transport.ReceiveLine(a, GameJson.Serialize(new CreateRoomRecord("duel")));
        await TestDispatcher.FlushAsync();
        Assert.Single(window.RoomsPanel.Children);

        _transport.SimulateClientDisconnected(a); // lone host drop closes the room
        await TestDispatcher.FlushAsync();
        Assert.Empty(window.RoomsPanel.Children);
    }

    [WpfFact]
    public async Task LogEvent_FromWorkerThread_AppendsToOutputBox()
    {
        ServerWindow window = HeadlessWindow.Prepare(new ServerWindow(_lobby, 1234));
        Guid a = _transport.SimulateClientConnected();
        Hello(_transport, a, "Alice"); // raises LogReceived off-thread
        await TestDispatcher.FlushAsync();

        Assert.Contains("connected.", window.OutputTextBox.Text);
        Assert.Contains("Alice", window.OutputTextBox.Text);
    }

    [WpfFact]
    public async Task EmptyLobby_RendersZeroRows_NoCrash()
    {
        ServerWindow window = HeadlessWindow.Prepare(new ServerWindow(_lobby, 1234));
        await TestDispatcher.FlushAsync();

        Assert.Empty(window.RoomsPanel.Children);
        Assert.Equal(string.Empty, window.OutputTextBox.Text);
    }
}
