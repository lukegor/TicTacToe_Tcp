using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class TicTacToeHostServiceTests
{
    private static readonly int[] ExpectedWinningLine = [0, 1, 2];

    private readonly FakeServerTransport _transport = new();
    private readonly TicTacToeHostService _service;

    public TicTacToeHostServiceTests() => _service = new TicTacToeHostService(_transport);

    [Fact]
    public void Start_PublishesInitialStateWithHostAsX()
    {
        _service.Start();

        GameStateRecord state = LastBroadcastState();
        Assert.Equal(1, state.Round);
        Assert.Equal("X", state.Turn);
        Assert.Equal("inProgress", state.Status);
        Assert.All(state.Board, cell => Assert.Equal("", cell));
    }

    [Fact]
    public async Task PlayMove_AppliesHostMarkAndBroadcasts()
    {
        _service.Start();
        _transport.BroadcastLines.Clear();

        await _service.PlayMoveAsync(4);

        GameStateRecord state = LastBroadcastState();
        Assert.Equal("X", state.Board[4]);
        Assert.Equal("O", state.Turn);
    }

    [Fact]
    public async Task RemoteMoveRequest_IsValidatedAgainstClientMark()
    {
        _service.Start();
        await _service.PlayMoveAsync(4); // X takes center; O's turn.

        _transport.ReceiveLine(GameJson.Serialize(new MoveRequestRecord(0)));

        GameStateRecord state = LastBroadcastState();
        Assert.Equal("O", state.Board[0]);
        Assert.Equal("X", state.Turn);
    }

    [Fact]
    public async Task IllegalRemoteMove_IsRejectedAndResynced()
    {
        _service.Start();
        await _service.PlayMoveAsync(4); // X center.
        int broadcastsBefore = _transport.BroadcastLines.Count;

        _transport.ReceiveLine(GameJson.Serialize(new MoveRequestRecord(4))); // occupied

        GameStateRecord state = LastBroadcastState();
        Assert.Equal("X", state.Board[4]);           // unchanged
        Assert.Equal("O", state.Turn);               // unchanged
        Assert.True(_transport.BroadcastLines.Count > broadcastsBefore); // resync happened
    }

    [Fact]
    public async Task WinningSequence_BroadcastsWonState()
    {
        _service.Start();

        await _service.PlayMoveAsync(0);              // X
        _transport.ReceiveLine(GameJson.Serialize(new MoveRequestRecord(3))); // O
        await _service.PlayMoveAsync(1);              // X
        _transport.ReceiveLine(GameJson.Serialize(new MoveRequestRecord(4))); // O
        await _service.PlayMoveAsync(2);              // X wins

        GameStateRecord state = LastBroadcastState();
        Assert.Equal("won", state.Status);
        Assert.Equal("X", state.Winner);
        Assert.Equal(ExpectedWinningLine, state.WinningLine!.ToArray());
    }

    [Fact]
    public async Task Rematch_RequiresBothVotes_SwapsMarks_AndIncrementsRound()
    {
        _service.Start();
        bool prompted = false;
        _service.RematchRequested += () => prompted = true;

        await _service.RequestRematchAsync(); // host votes alone -> offer broadcast, no new round
        Assert.Equal(1, LastBroadcastState().Round);

        _transport.ReceiveLine(GameJson.Serialize(new RematchOfferRecord())); // second vote

        GameStateRecord state = LastBroadcastState();
        Assert.Equal(2, state.Round);
        Assert.All(state.Board, cell => Assert.Equal("", cell));
        Assert.Equal("X", state.Turn);                    // X always starts
        Assert.Equal("O", GameRoles.HostMark(2));         // host mark swapped
        Assert.False(prompted);                           // votes were complete; no prompt needed
    }

    [Fact]
    public async Task ClientVotesFirst_HostGetsPrompted_ThenHostClickStartsRound2()
    {
        _service.Start();
        bool prompted = false;
        _service.RematchRequested += () => prompted = true;

        _transport.ReceiveLine(GameJson.Serialize(new RematchOfferRecord()));
        Assert.True(prompted);

        await _service.RequestRematchAsync();

        Assert.Equal(2, LastBroadcastState().Round);
    }

    [Fact]
    public void ChatLine_IsSurfacedViaLogReceived()
    {
        string? logged = null;
        _service.LogReceived += line => logged = line;

        _service.Start();
        _transport.ReceiveLine("plain hello");

        Assert.Equal("plain hello", logged);
    }

    [Fact]
    public void ClientJoin_TriggersStatePush()
    {
        _service.Start();

        _transport.SimulateClientConnected();

        Assert.Equal(1, LastBroadcastState().Round);
    }

    private GameStateRecord LastBroadcastState()
    {
        // Broadcasts may include non-state envelopes (e.g. rematch offers);
        // pick the most recent line that parses as a state.
        GameStateRecord? state = _transport.BroadcastLines
            .Select(line => GameJson.TryParse(line))
            .OfType<GameStateRecord>()
            .LastOrDefault();

        return state ?? throw new InvalidOperationException("No state was broadcast.");
    }
}
