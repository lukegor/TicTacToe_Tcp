using System.Windows.Controls;
using System.Windows.Media;
using ClientServer.App;
using ClientServer.Core.Game;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.UiTests;

public sealed class GameWindowTests : IDisposable
{
    private readonly PlayerSession _session;
    private readonly FakeClientTransport _transport;
    private readonly GameWindow _window;

    public GameWindowTests()
    {
        (_session, _transport) = UiTestSession.ConnectSeatedAsync(mark: "X").GetAwaiter().GetResult();
        _window = HeadlessWindow.Prepare(new GameWindow(_session));
        _window.Show(); // off-screen: activates bindings and item generation
    }

    public void Dispose() => _session.Dispose();

    private IReadOnlyList<Button> Cells => VisualTreeEx.FindChildren<System.Windows.Controls.Button>(_window.BoardGrid).ToList();

    private Button Cell(int index) => Cells.ElementAt(index);

    private void ServerState(string turn, string status,
        string? winner = null, int[]? winningLine = null, string? offeredBy = null)
    {
        string[] board = ["", "", "", "", "", "", "", "", ""];
        _transport.ReceiveLine(GameJson.Serialize(new GameStateRecord(
            board, turn, status, winner, winningLine, 1, Room: "duel",
            XName: "Alice", OName: "Bob", RematchOfferedBy: offeredBy)));
    }

    [WpfFact]
    public async Task FreshBoard_RendersEmpty_MyTurn_EnablesMyCells()
    {
        await TestDispatcher.FlushAsync();

        Assert.All(Cells, c => Assert.Equal("", c.Content));
        Assert.All(Cells, c => Assert.True(c.IsEnabled));
        Assert.Equal("Your move (X).", _window.StatusText.Text);
    }

    [WpfFact]
    public async Task MyTurn_CellClick_SendsExactlyOneMoveRequest()
    {
        UiAssert.Press(Cell(4));
        await TestDispatcher.FlushAsync();

        Assert.Equal(1, _transport.SentLines.Count(l =>
            l.Contains("\"moveRequest\"") && l.Contains("\"cell\":4")));
    }

    [WpfFact]
    public async Task NotMyTurn_CellsDisabled_ClickSendsNothing()
    {
        ServerState(turn: "O", status: "inProgress");
        await TestDispatcher.FlushAsync();
        int before = _transport.SentLines.Count;

        Assert.False(UiAssert.TryPress(Cell(4)));
        await TestDispatcher.FlushAsync();

        Assert.All(Cells, c => Assert.False(c.IsEnabled));
        Assert.Contains("Opponent's move.", _window.StatusText.Text);
        Assert.Equal(before, _transport.SentLines.Count);
    }

    [WpfFact]
    public async Task Spectator_ShowsIndicator_AndNeverSends()
    {
        var (session, transport) = UiTestSession.ConnectSeatedAsync(mark: null).GetAwaiter().GetResult();
        try
        {
            GameWindow window = HeadlessWindow.Prepare(new GameWindow(session));
            window.Show(); // off-screen: activates bindings and item generation
            await TestDispatcher.FlushAsync();

            Assert.True(session.IsSpectator);
            Assert.StartsWith("[Spectating] ", window.StatusText.Text);

            var firstCell = VisualTreeEx.FindChildren<System.Windows.Controls.Button>(window.BoardGrid).First();
            Assert.False(UiAssert.TryPress(firstCell));
            await TestDispatcher.FlushAsync();

            Assert.DoesNotContain(transport.SentLines, l => l.Contains("moveRequest"));
        }
        finally
        {
            session.Dispose();
        }
    }

    [WpfFact]
    public async Task StateEcho_RendersMark_AndDisablesFilledCell()
    {
        string[] withX = ["", "", "", "", "X", "", "", "", ""];
        _transport.ReceiveLine(GameJson.Serialize(new GameStateRecord(
            withX, "O", "inProgress", null, null, 1, Room: "duel")));
        await TestDispatcher.FlushAsync();

        Assert.Equal("X", Cell(4).Content);
        Assert.False(Cell(4).IsEnabled);
    }

    [WpfFact]
    public async Task WinningLine_HighlightsCells_WhiteElsewhere_AnnouncesWin()
    {
        string[] won = ["X", "X", "X", "", "O", "", "", "", ""];
        _transport.ReceiveLine(GameJson.Serialize(new GameStateRecord(
            won, "O", "won", "X", [0, 1, 2], 1, Room: "duel", XName: "Alice", OName: "Bob")));
        await TestDispatcher.FlushAsync();

        Assert.Equal(Brushes.LightGoldenrodYellow, Cell(0).Background);
        Assert.Equal(Brushes.LightGoldenrodYellow, Cell(2).Background);
        Assert.Equal(Brushes.White, Cell(8).Background);
        Assert.Contains("You win!", _window.StatusText.Text);
    }

    [WpfFact]
    public async Task Draw_StatusShown_ClicksSendNothing()
    {
        ServerState(turn: "O", status: "draw");
        await TestDispatcher.FlushAsync();

        Assert.Equal("It's a draw.", _window.StatusText.Text);

        int before = _transport.SentLines.Count;
        Assert.False(UiAssert.TryPress(Cell(0)));
        await TestDispatcher.FlushAsync();
        Assert.Equal(before, _transport.SentLines.Count);
    }

    [WpfFact]
    public async Task OfferRematch_DecidedGame_SendsOffer()
    {
        ServerState(turn: "O", status: "won", winner: "X", winningLine: [0, 1, 2]);
        await TestDispatcher.FlushAsync();

        Assert.Equal("Offer Rematch", _window.ViewModel.RematchLabel);
        Assert.True(_window.RematchButton.IsEnabled);

        UiAssert.Press(_window.RematchButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains(_transport.SentLines, l => l.Contains("\"rematchOffer\""));
    }

    [WpfFact]
    public async Task OpponentOffers_ButtonAccepts_NamesChallenger_SendsOnPress()
    {
        ServerState(turn: "O", status: "won", winner: "X", winningLine: [0, 1, 2], offeredBy: "O");
        await TestDispatcher.FlushAsync();

        Assert.Equal("Accept Rematch", _window.ViewModel.RematchLabel);
        Assert.True(_window.RematchButton.IsEnabled);
        Assert.Contains("Bob offers a rematch.", _window.StatusText.Text);

        UiAssert.Press(_window.RematchButton);
        await TestDispatcher.FlushAsync();
        Assert.Contains(_transport.SentLines, l => l.Contains("\"rematchOffer\""));
    }

    [WpfFact]
    public async Task OwnOfferOutstanding_ButtonDisabled_Labelled()
    {
        ServerState(turn: "O", status: "won", winner: "X", winningLine: [0, 1, 2], offeredBy: "X");
        await TestDispatcher.FlushAsync();

        Assert.Equal("Rematch offered...", _window.ViewModel.RematchLabel);
        Assert.False(_window.RematchButton.IsEnabled);
    }

    [WpfFact]
    public async Task Disconnect_ShowsBanner_DisablesEverything()
    {
        _transport.SimulateDisconnect();
        await TestDispatcher.FlushAsync();

        Assert.Equal("Connection lost — rejoining...", _window.StatusText.Text);
        Assert.All(Cells, c => Assert.False(c.IsEnabled));
        Assert.False(_window.RematchButton.IsEnabled);
    }

    [WpfFact]
    public async Task RestoredSeat_LogsRestore_RerendersFreshBoard()
    {
        _transport.SimulateDisconnect();
        await TestDispatcher.FlushAsync();

        _transport.ReceiveLine(GameJson.Serialize(new JoinedRecord(
            "duel", "X", true,
            new GameStateRecord(["", "", "", "", "", "", "", "", ""],
                "X", "inProgress", null, null, 2, Room: "duel"))));
        await TestDispatcher.FlushAsync();

        Assert.Contains("your seat was restored", _window.OutputTextBox.Text);
        Assert.NotEqual("Connection lost — rejoining...", _window.StatusText.Text);
        Assert.All(Cells, c => Assert.True(c.IsEnabled));
    }

    [WpfFact]
    public async Task ErrorEnvelope_AppendsToOutputBox()
    {
        _transport.ReceiveLine(GameJson.Serialize(new ErrorRecord("That cell is taken.")));
        await TestDispatcher.FlushAsync();

        Assert.Contains("That cell is taken.", _window.OutputTextBox.Text);
    }

    [WpfFact]
    public async Task LeaveButton_SendsLeaveRoom()
    {
        UiAssert.Press(_window.LeaveButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains(_transport.SentLines, l => l.Contains("\"leaveRoom\""));
    }
}
