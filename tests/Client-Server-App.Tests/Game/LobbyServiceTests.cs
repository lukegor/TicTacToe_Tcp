using ClientServer.Core.Game;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.Tests.Game;

public sealed class LobbyServiceTests
{
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(50);

    private readonly FakeServerTransport _transport = new();

    private LobbyService CreateLobby(TimeSpan? grace = null)
    {
        LobbyService lobby = new(_transport, grace ?? ShortGrace);
        lobby.Start();
        return lobby;
    }

    private GameStateRecord LastState(Guid id) =>
        _transport.Inbox(id).Select(line => GameJson.TryParse(line))
            .OfType<GameStateRecord>()
            .Last();

    private JoinedRecord LastJoined(Guid id) =>
        _transport.Inbox(id).Select(line => GameJson.TryParse(line))
            .OfType<JoinedRecord>()
            .Last();

    private ErrorRecord LastError(Guid id) =>
        _transport.Inbox(id).Select(line => GameJson.TryParse(line))
            .OfType<ErrorRecord>()
            .Last();

    private RoomListRecord LastRoomList(Guid id) =>
        _transport.Inbox(id).Select(line => GameJson.TryParse(line))
            .OfType<RoomListRecord>()
            .Last();

    [Fact]
    public void Connect_PushesEmptyRoomList()
    {
        using LobbyService lobby = CreateLobby();
        Guid id = _transport.SimulateClientConnected();

        Assert.Empty(LastRoomList(id).Rooms);
    }

    [Fact]
    public void CreateRoom_SeatsCreatorAsX_AndListsRoom()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("Friday")));

        JoinedRecord joined = LastJoined(creator);
        Assert.Equal("Friday", joined.Room);
        Assert.Equal("X", joined.Mark);

        RoomInfoRecord info = LastRoomList(creator).Rooms.Single();
        Assert.Equal(1, info.Players);
    }

    [Fact]
    public void DuplicateName_IsRejected_Targeted()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("Friday")));
        _transport.ClearInbox(second);

        _transport.ReceiveLine(second, GameJson.Serialize(new CreateRoomRecord("FRIDAY")));

        Assert.Contains("already exists", LastError(second).Message);
        Assert.DoesNotContain(_transport.Inbox(second), l => l.Contains("\"type\":\"joined\"", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidNames_AreRejected()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();

        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("   ")));
        Assert.Contains("1-30", LastError(creator).Message);

        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord(new string('x', 31))));
        Assert.Contains("1-30", LastError(creator).Message);
    }

    [Fact]
    public void JoinUnknownRoom_IsRejected()
    {
        using LobbyService lobby = CreateLobby();
        Guid id = _transport.SimulateClientConnected();

        _transport.ReceiveLine(id, GameJson.Serialize(new JoinRoomRecord("ghost")));

        Assert.Contains("does not exist", LastError(id).Message);
    }

    [Fact]
    public void SecondPlayer_AutoSeatedO_ThirdBecomesSpectator()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        Guid third = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));

        Assert.Equal("X", LastJoined(creator).Mark);
        Assert.Equal("O", LastJoined(second).Mark);
        Assert.Equal("X", LastState(second).Turn); // auto-start

        _transport.ReceiveLine(third, GameJson.Serialize(new JoinRoomRecord("duel")));
        Assert.Null(LastJoined(third).Mark);

        RoomInfoRecord info = LastRoomList(creator).Rooms.Single();
        Assert.Equal(2, info.Players);
        Assert.Equal(1, info.Spectators);
    }

    [Fact]
    public void Hello_AnnouncesName_InConnectedAndDisconnectedLogs()
    {
        using LobbyService lobby = CreateLobby();
        List<string> logs = [];
        lobby.LogReceived += logs.Add;
        Guid id = _transport.SimulateClientConnected();

        _transport.ReceiveLine(id, GameJson.Serialize(new HelloRecord("Alice")));
        _transport.SimulateClientDisconnected(id);

        Assert.Contains(logs, l => l.StartsWith("Alice (", StringComparison.Ordinal) && l.EndsWith(") connected.", StringComparison.Ordinal));
        Assert.Contains(logs, l => l.StartsWith("Alice (", StringComparison.Ordinal) && l.EndsWith(") disconnected.", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, l => l.Contains($"Client {id.ToString()[..8]} connected", StringComparison.Ordinal));
    }

    [Fact]
    public void PlayerNames_AreResolvedAndFlowIntoGameState()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new HelloRecord("  Alice  ")));
        _transport.ReceiveLine(second, GameJson.Serialize(new HelloRecord("Bob")));
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));

        GameStateRecord state = LastState(second);
        Assert.Equal("Alice", state.XName); // trimmed
        Assert.Equal("Bob", state.OName);
        Assert.Equal("Alice", LastJoined(creator).State.XName);
    }

    [Fact]
    public void BlankOrMissingHello_FallsBackToGuestLabel()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected(); // never says hello
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(second, GameJson.Serialize(new HelloRecord("   ")));
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));

        GameStateRecord state = LastState(second);
        Assert.Matches("^Guest-.+", state.XName!);
        Assert.Matches("^Guest-.+", state.OName!);
    }

    [Fact]
    public void OversizedPlayerName_IsTruncatedTo30Characters()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new HelloRecord(new string('n', 40))));
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));

        Assert.Equal(30, LastJoined(creator).State.XName!.Length);
    }

    [Fact]
    public void MoveRequests_RouteThroughRoom_SpectatorMoveIgnored()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        Guid third = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));
        _transport.ReceiveLine(third, GameJson.Serialize(new JoinRoomRecord("duel")));

        _transport.ReceiveLine(creator, GameJson.Serialize(new MoveRequestRecord(0)));
        Assert.Equal("X", LastState(third).Board[0]); // spectator observes

        _transport.ReceiveLine(third, GameJson.Serialize(new MoveRequestRecord(1)));
        GameStateRecord unchanged = LastState(third); // spectator's move ignored
        Assert.Equal("", unchanged.Board[1]);

        _transport.ReceiveLine(second, GameJson.Serialize(new MoveRequestRecord(3)));
        Assert.Equal("O", LastState(second).Board[3]);
    }

    [Fact]
    public void Chat_Unseated_IsIgnored_Seated_ReachesMembers()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        Guid outsider = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));

        _transport.ReceiveLine(outsider, "spam");
        _transport.ReceiveLine(creator, "gl hf");

        Assert.DoesNotContain("spam", _transport.Inbox(second));
        Assert.Contains("gl hf", _transport.Inbox(second));
    }

    [Fact]
    public async Task Disconnect_TriggersGrace_ExpiryAwardsForfeit()
    {
        using LobbyService lobby = CreateLobby(ShortGrace);
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));
        _transport.ReceiveLine(creator, GameJson.Serialize(new MoveRequestRecord(0)));

        _transport.SimulateClientDisconnected(second);

        await Task.Delay(250, TestContext.Current.CancellationToken);
        GameStateRecord state = LastState(creator);
        Assert.Equal("won", state.Status);
        Assert.Equal("X", state.Winner);
        Assert.Equal("forfeit", state.WinnerReason);
    }

    [Fact]
    public void BothPlayersDisconnecting_ClosesRoom()
    {
        using LobbyService lobby = CreateLobby(TimeSpan.FromSeconds(10));
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));

        _transport.SimulateClientDisconnected(creator);
        _transport.SimulateClientDisconnected(second);

        Assert.Empty(lobby.GetRooms());
    }

    [Fact]
    public void LeaveRoom_SendsLeftEnvelope()
    {
        using LobbyService lobby = CreateLobby(TimeSpan.FromSeconds(10));
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));

        _transport.ReceiveLine(second, GameJson.Serialize(new LeaveRoomRecord()));

        Assert.Contains(_transport.Inbox(second), l => l.Contains("\"type\":\"left\"", StringComparison.Ordinal));
    }

    [Fact]
    public void GetRooms_ReflectsLiveState()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));

        RoomInfoRecord info = Assert.Single(lobby.GetRooms());
        Assert.Equal("duel", info.Name);
        Assert.Equal(1, info.Players);
        Assert.Equal(0, info.Spectators);
    }
}
