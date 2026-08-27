using System.Windows.Controls;
using TicTacToe;
using TicTacToe.Core.Game;
using TicTacToe.TestSupport;
using Xunit;

namespace TicTacToe.UiTests;

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

    private static void RealizeRooms(ServerWindow window)
    {
        window.RoomsPanel.Measure(new System.Windows.Size(400, 300));
        window.RoomsPanel.Arrange(new System.Windows.Rect(0, 0, 400, 300));
        window.RoomsPanel.UpdateLayout();
    }

    private static IReadOnlyList<string[]> RowTexts(ServerWindow window)
    {
        RealizeRooms(window);
        return VisualTreeEx.FindChildren<System.Windows.Controls.Grid>(window.RoomsPanel)
            .Select(g => g.Children.OfType<TextBlock>().Select(t => t.Text).ToArray())
            .Where(a => a.Length == 3)
            .ToList();
    }

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
        window.Show(); // off-screen: activates bindings and item generation
        await TestDispatcher.FlushAsync();

        var rows = RowTexts(window);
        _ = Assert.Single(rows);
        Assert.Equal(["duel", "2/2", "0"], rows[0]);
    }

    [WpfFact]
    public async Task MembershipChange_RerendersRows()
    {
        ServerWindow window = HeadlessWindow.Prepare(new ServerWindow(_lobby, 1234));
        window.Show(); // off-screen: activates bindings and item generation
        await TestDispatcher.FlushAsync();
        Assert.Empty(RowTexts(window));

        Guid a = _transport.SimulateClientConnected();
        Hello(_transport, a, "Alice");
        _transport.ReceiveLine(a, GameJson.Serialize(new CreateRoomRecord("duel")));
        await TestDispatcher.FlushAsync();
        _ = Assert.Single(RowTexts(window));

        _transport.SimulateClientDisconnected(a); // lone host drop closes the room
        await TestDispatcher.FlushAsync();
        Assert.Empty(RowTexts(window));
    }

    [WpfFact]
    public async Task LogEvent_FromWorkerThread_AppendsToOutputBox()
    {
        ServerWindow window = HeadlessWindow.Prepare(new ServerWindow(_lobby, 1234));
        window.Show(); // off-screen: activates bindings and item generation
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
        window.Show(); // off-screen: activates bindings and item generation
        await TestDispatcher.FlushAsync();

        Assert.Empty(window.RoomsPanel.Items);
        Assert.Equal(string.Empty, window.OutputTextBox.Text);
    }
}
