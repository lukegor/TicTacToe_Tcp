using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class RoomTests
{
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(50);
    private static readonly int[] ExpectedWinningLine = [0, 1, 2];

    private readonly Dictionary<Guid, List<string>> _inbox = new();
    private readonly List<string> _logs = [];

    private Room CreateRoom(TimeSpan? grace = null) =>
        new(
            "friday",
            (id, line) =>
            {
                if (!_inbox.TryGetValue(id, out List<string>? list))
                {
                    list = [];
                    _inbox[id] = list;
                }

                list.Add(line);
            },
            line => _logs.Add(line),
            grace ?? ShortGrace);

    private GameStateRecord LastState(Guid id) =>
        _inbox[id].Select(line => GameJson.TryParse(line))
            .OfType<GameStateRecord>()
            .Last();

    private JoinedRecord LastJoined(Guid id) =>
        _inbox[id].Select(line => GameJson.TryParse(line))
            .OfType<JoinedRecord>()
            .Last();

    private bool HasLine(Guid id, string fragment) =>
        _inbox[id].Any(line => line.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public void Seat_FirstBecomesX_SecondBecomesO_AndStartsGame()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        room.Seat(first);
        Assert.Equal(1, room.PlayerCount);
        Assert.True(room.HasVacancy);
        Assert.Equal("X", LastJoined(first).Mark);
        Assert.False(LastJoined(first).Restored);

        room.Seat(second);
        Assert.Equal("O", LastJoined(second).Mark);
        Assert.False(room.HasVacancy);

        GameStateRecord started = LastState(second);
        Assert.Equal("inProgress", started.Status);
        Assert.Equal("X", started.Turn);
        Assert.Equal("friday", started.Room);
    }

    [Fact]
    public void Moves_AreValidatedPerMark_AndBroadcastToBothSeats()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);

        room.HandleMove(x, 4);
        room.HandleMove(x, 0); // wrong turn -> ignored, resync only
        room.HandleMove(o, 4); // occupied -> ignored, resync only

        GameStateRecord state = LastState(x);
        Assert.Equal("X", state.Board[4]);
        Assert.Equal("", state.Board[0]);
        Assert.Equal("O", state.Turn); // X's legal move advanced the turn
        Assert.Equal(LastState(o).Board, state.Board);
    }

    [Fact]
    public void WinningSequence_BroadcastsWonStateWithLine()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);

        room.HandleMove(x, 0);
        room.HandleMove(o, 3);
        room.HandleMove(x, 1);
        room.HandleMove(o, 4);
        room.HandleMove(x, 2);

        GameStateRecord state = LastState(x);
        Assert.Equal("won", state.Status);
        Assert.Equal("X", state.Winner);
        Assert.Null(state.WinnerReason);
        Assert.Equal(ExpectedWinningLine, state.WinningLine!.ToArray());
    }

    [Fact]
    public async Task PlayerDrop_StartsGrace_RestoreCancelsIt()
    {
        using Room room = CreateRoom(TimeSpan.FromMilliseconds(250));
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);
        room.HandleMove(x, 4);

        room.HandleDisconnect(o);
        Assert.Equal(1, room.PlayerCount);
        Assert.True(room.HasVacancy);

        room.Seat(o); // rejoin inside the grace window

        JoinedRecord joined = LastJoined(o);
        Assert.True(joined.Restored);
        Assert.Equal("O", joined.Mark);
        Assert.Equal("X", joined.State.Board[4]);

        await Task.Delay(350); // original deadline passes
        Assert.Equal("inProgress", LastState(x).Status);
        Assert.Equal(2, room.PlayerCount);
    }

    [Fact]
    public async Task PlayerDrop_GraceExpiry_ForfeitsToOpponent()
    {
        using Room room = CreateRoom(ShortGrace);
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);
        room.HandleMove(x, 4);

        room.HandleDisconnect(o);

        await Task.Delay(250);

        GameStateRecord state = LastState(x);
        Assert.Equal("won", state.Status);
        Assert.Equal("X", state.Winner);
        Assert.Equal("forfeit", state.WinnerReason);
        Assert.True(room.HasVacancy);
    }

    [Fact]
    public async Task SoloPlayerDrop_ClosesRoomImmediately()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        room.Seat(x);

        TaskCompletionSource<IReadOnlyList<Guid>> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        room.RoomClosed += ids => closed.TrySetResult(ids);

        room.HandleDisconnect(x);

        IReadOnlyList<Guid> evicted = await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(evicted);
        Assert.Equal(0, room.PlayerCount);
    }

    [Fact]
    public void ExplicitLeave_MidGame_ForfeitsImmediately()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);
        room.HandleMove(x, 0);

        room.HandleLeave(o);

        GameStateRecord state = LastState(x);
        Assert.Equal("won", state.Status);
        Assert.Equal("X", state.Winner);
        Assert.Equal("forfeit", state.WinnerReason);
        Assert.Equal(1, room.PlayerCount);
        Assert.True(HasLine(o, "\"type\":\"left\""));
    }

    [Fact]
    public void Spectator_ReceivesStates_ButMovesAreIgnored()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        Guid spect = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);
        room.AddSpectator(spect);

        Assert.Null(LastJoined(spect).Mark);

        room.HandleMove(x, 0); // produces the spectator's first state broadcast
        Assert.Equal("X", LastState(spect).Board[0]);

        room.HandleMove(spect, 4); // spectators cannot move
        GameStateRecord unchanged = LastState(spect);
        Assert.Equal("", unchanged.Board[4]);

        room.HandleMove(o, 1);
        Assert.Equal("O", LastState(spect).Board[1]);
    }

    [Fact]
    public void Rematch_RequiresBothVotes_SwapsMarks_IncrementsRound()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid o = Guid.NewGuid();
        room.Seat(x);
        room.Seat(o);
        room.HandleMove(x, 0);
        room.HandleMove(o, 3);
        room.HandleMove(x, 1);
        room.HandleMove(o, 4);
        room.HandleMove(x, 2); // X wins

        room.HandleRematchOffer(x);
        Assert.Equal("won", LastState(x).Status); // one vote is not enough

        room.HandleRematchOffer(o);

        GameStateRecord next = LastState(x);
        Assert.Equal(2, next.Round);
        Assert.Equal("inProgress", next.Status);
        Assert.All(next.Board, cell => Assert.Equal("", cell));
        Assert.Equal("X", next.Turn);
        Assert.Equal("O", LastJoined(x).Mark); // marks swapped via fresh joined records
        Assert.Equal("X", LastJoined(o).Mark);

        room.HandleMove(o, 4); // o now holds X and starts
        Assert.Equal("X", LastState(x).Board[4]);
    }

    [Fact]
    public void Chat_ReachesOnlyRoomMembers()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid outsider = Guid.NewGuid();
        room.Seat(x);

        room.HandleChat(outsider, "hello?");
        room.HandleChat(x, "gl hf");

        Assert.DoesNotContain("hello?", _inbox.GetValueOrDefault(outsider, []));
        Assert.Contains("gl hf", _inbox[x]);
    }

    [Fact]
    public async Task LastMemberLeaving_ClosesRoom_AndFiresEvent()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        Guid x = Guid.NewGuid();
        Guid spect = Guid.NewGuid();
        room.Seat(x);
        room.AddSpectator(spect);

        TaskCompletionSource<IReadOnlyList<Guid>> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        room.RoomClosed += ids => closed.TrySetResult(ids);

        room.HandleLeave(x);
        room.HandleLeave(spect);

        // Each member was individually released; the close event carries whoever
        // remained at close time — nobody here.
        IReadOnlyList<Guid> evicted = await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(evicted);
        Assert.True(HasLine(x, "\"type\":\"left\""));
    }

    [Fact]
    public void MembershipChanged_FiresOnTransitions()
    {
        using Room room = CreateRoom(TimeSpan.FromSeconds(10));
        int changes = 0;
        room.MembershipChanged += () => changes++;

        Guid x = Guid.NewGuid();
        Guid spect = Guid.NewGuid();
        room.Seat(x);
        room.AddSpectator(spect);
        room.HandleLeave(spect);

        Assert.True(changes >= 3);
    }
}
