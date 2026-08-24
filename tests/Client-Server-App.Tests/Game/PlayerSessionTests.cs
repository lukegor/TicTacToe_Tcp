using System.Diagnostics;
using System.IO;
using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class PlayerSessionTests
{
    // Must exceed the session's 1-second retry interval so a second attempt fits.
    private static readonly TimeSpan TestBudget = TimeSpan.FromSeconds(3);

    private sealed class ScriptedConnections
    {
        private int _failuresRemaining;

        public List<FakeClientTransport> Created { get; } = [];
        public Func<Task<IClientTransport>> Factory => ConnectAsync;

        /// <summary>Makes the next N factory calls fail (reconnect attempts).</summary>
        public void QueueFailures(int count) => Interlocked.Add(ref _failuresRemaining, count);

        private async Task<IClientTransport> ConnectAsync()
        {
            if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
            {
                await Task.Yield();
                throw new IOException("simulated outage");
            }

            FakeClientTransport transport = new();
            Created.Add(transport);
            await Task.CompletedTask;
            return transport;
        }
    }

    private static GameStateRecord Snapshot(string room, int round = 1) =>
        new(["", "", "", "", "", "", "", "", ""], "X", "inProgress", null, null, round, room);

    private static void AssertEquivalent(GameStateRecord expected, GameStateRecord? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Board, actual.Board);
        Assert.Equal(expected.Turn, actual.Turn);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Winner, actual.Winner);
        Assert.Equal(expected.Round, actual.Round);
        Assert.Equal(expected.Room, actual.Room);
    }

    [Fact]
    public async Task Connect_TransitionsToLobby()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);

        await session.ConnectAsync();

        Assert.Equal(PlayerSessionState.Lobby, session.State);
    }

    [Fact]
    public async Task JoinFlow_SeatsAndTracksMark()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        await session.ConnectAsync();
        FakeClientTransport transport = connections.Created.Single();

        List<JoinedRecord> seated = [];
        session.Seated += j => seated.Add(j);
        await session.JoinRoomAsync("friday");
        transport.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "X", false, Snapshot("friday"))));

        Assert.Contains("\"type\":\"joinRoom\"", Assert.Single(transport.SentLines));
        Assert.Equal(PlayerSessionState.Seated, session.State);
        Assert.Equal("friday", session.CurrentRoomName);
        Assert.Equal("X", session.MyMark);
        Assert.False(session.IsSpectator);
        Assert.Single(seated);
        AssertEquivalent(Snapshot("friday"), session.CurrentState);
    }

    [Fact]
    public async Task SpectatorSeating_IsFlagged()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        await session.ConnectAsync();
        FakeClientTransport transport = connections.Created.Single();

        transport.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", null, false, Snapshot("friday"))));

        Assert.True(session.IsSpectator);
    }

    [Fact]
    public async Task PlayCell_OnlyWhenSeatedPlayer()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        await session.ConnectAsync();
        FakeClientTransport transport = connections.Created.Single();

        await session.PlayCellAsync(0); // lobby -> ignored
        Assert.Empty(transport.SentLines);

        transport.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "X", false, Snapshot("friday"))));
        await session.PlayCellAsync(4);

        Assert.Contains("\"cell\":4", transport.SentLines[^1]);
    }

    [Fact]
    public async Task RoomListPush_UpdatesLatestAndRaises()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        await session.ConnectAsync();
        FakeClientTransport transport = connections.Created.Single();
        IReadOnlyList<RoomInfoRecord>? seen = null;
        session.RoomsUpdated += rooms => seen = rooms;

        transport.ReceiveLine(GameJson.Serialize(new RoomListRecord([new RoomInfoRecord("duel", 1, 0)])));

        Assert.NotNull(seen);
        Assert.Equal("duel", session.LatestRooms.Single().Name);
    }

    [Fact]
    public async Task LeftRecord_ReturnsToLobby()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        await session.ConnectAsync();
        FakeClientTransport transport = connections.Created.Single();
        transport.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", false, Snapshot("friday"))));
        string? reason = null;
        session.ReturnedToLobby += r => reason = r;

        transport.ReceiveLine(GameJson.Serialize(new LeftRecord("roomClosed")));

        Assert.Equal("roomClosed", reason);
        Assert.Equal(PlayerSessionState.Lobby, session.State);
        Assert.Null(session.CurrentRoomName);
        Assert.Null(session.MyMark);
    }

    [Fact]
    public async Task LeaveRoom_SendsEnvelope_AndReturnsLocally()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        await session.ConnectAsync();
        FakeClientTransport transport = connections.Created.Single();
        transport.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", false, Snapshot("friday"))));

        await session.LeaveRoomAsync();

        string sent = Assert.Single(transport.SentLines);
        Assert.Contains("\"type\":\"leaveRoom\"", sent);
        Assert.Equal(PlayerSessionState.Lobby, session.State);
    }

    [Fact]
    public async Task UnexpectedDrop_WhileSeated_ReconnectsAndRestores()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory, TestBudget);
        await session.ConnectAsync();
        FakeClientTransport original = connections.Created.Single();
        original.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", false,
            new GameStateRecord(["X", "", "", "", "", "", "", "", ""], "O", "inProgress", null, null, 1, "friday"))));

        bool reconnectingRaised = false;
        session.ReconnectingStarted += () => reconnectingRaised = true;

        // Exactly one failed retry, then the replacement succeeds.
        connections.QueueFailures(1);
        original.SimulateDisconnect();

        Stopwatch clock = Stopwatch.StartNew();
        while (connections.Created.Count < 2 && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        Assert.True(reconnectingRaised);
        Assert.Equal(PlayerSessionState.Reconnecting, session.State); // awaiting server's joined ack
        Assert.Equal(2, connections.Created.Count);
        FakeClientTransport replacement = connections.Created[1];
        string joinLine = Assert.Single(replacement.SentLines);
        Assert.Contains("\"type\":\"joinRoom\"", joinLine);
        Assert.Contains("friday", joinLine);

        replacement.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", true,
            new GameStateRecord(["X", "", "", "", "", "", "", "", ""], "O", "inProgress", null, null, 1, "friday"))));
        Assert.Equal(PlayerSessionState.Seated, session.State);
        Assert.Equal("X", session.CurrentState!.Board[0]);
    }

    [Fact]
    public async Task ExhaustedReconnectBudget_ReturnsToLobbyWithError()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory, TestBudget);
        await session.ConnectAsync();
        connections.Created.Single().ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", false, Snapshot("friday"))));
        string? error = null;
        session.ErrorReceived += e => error = e;

        connections.QueueFailures(int.MaxValue);
        connections.Created.Single().SimulateDisconnect();

        Stopwatch clock = Stopwatch.StartNew();
        while (session.State != PlayerSessionState.Lobby && clock.Elapsed < TimeSpan.FromSeconds(8))
        {
            await Task.Delay(20);
        }

        Assert.Equal(PlayerSessionState.Lobby, session.State);
        Assert.Contains("Could not rejoin", error);
    }

    [Fact]
    public async Task DropWhileInLobby_ReportsDisconnection()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory, TestBudget);
        await session.ConnectAsync();
        string? error = null;
        session.ErrorReceived += e => error = e;

        connections.Created.Single().SimulateDisconnect();

        Assert.Equal(PlayerSessionState.Disconnected, session.State);
        Assert.Equal("Connection lost.", error);
    }
}
