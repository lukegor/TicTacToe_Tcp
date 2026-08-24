using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class TicTacToeClientServiceTests
{
    private readonly FakeClientTransport _transport = new();
    private readonly TicTacToeClientService _service;

    public TicTacToeClientServiceTests() => _service = new TicTacToeClientService(_transport);

    [Fact]
    public async Task PlayCell_SendsMoveRequestEnvelope()
    {
        await _service.PlayCellAsync(4);

        string sent = Assert.Single(_transport.SentLines);
        Assert.Contains("\"type\":\"moveRequest\"", sent);
        Assert.Contains("\"cell\":4", sent);
    }

    [Fact]
    public async Task SendRematchOffer_SendsRematchEnvelope()
    {
        await _service.SendRematchOfferAsync();

        string sent = Assert.Single(_transport.SentLines);
        Assert.Contains("\"type\":\"rematchOffer\"", sent);
    }

    [Fact]
    public void StateMessage_RaisesStateChangedAndUpdatesCurrent()
    {
        GameStateRecord? seen = null;
        _service.StateChanged += s => seen = s;
        _service.Start();

        _transport.ReceiveLine(GameJson.Serialize(new GameStateRecord(
            ["X", "", "", "", "", "", "", "", ""], "O", "inProgress", null, null, 1)));

        Assert.NotNull(seen);
        Assert.Same(seen, _service.CurrentState);
        Assert.Equal(1, seen!.Round);
    }

    [Fact]
    public void StaleRoundState_IsIgnored()
    {
        _service.Start();
        _transport.ReceiveLine(GameJson.Serialize(NewState(round: 2)));
        int raises = 0;
        _service.StateChanged += _ => raises++;

        _transport.ReceiveLine(GameJson.Serialize(NewState(round: 1)));

        Assert.Equal(0, raises);
        Assert.Equal(2, _service.CurrentState!.Round);
    }

    [Fact]
    public void RematchOffer_RaisesRematchRequested()
    {
        bool requested = false;
        _service.RematchRequested += () => requested = true;
        _service.Start();

        _transport.ReceiveLine(GameJson.Serialize(new RematchOfferRecord()));

        Assert.True(requested);
    }

    [Fact]
    public void Disconnect_RaisesOpponentDisconnected()
    {
        bool disconnected = false;
        _service.OpponentDisconnected += () => disconnected = true;
        _service.Start();

        _transport.SimulateDisconnect();

        Assert.True(disconnected);
    }

    [Fact]
    public void ChatLine_GoesToLog()
    {
        string? logged = null;
        _service.LogReceived += l => logged = l;
        _service.Start();

        _transport.ReceiveLine("hello there");

        Assert.Equal("hello there", logged);
    }

    private static GameStateRecord NewState(int round) =>
        new(["", "", "", "", "", "", "", "", ""], "X", "inProgress", null, null, round);
}
