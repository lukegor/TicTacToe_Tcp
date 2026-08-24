# Rooms & Neutral Referee Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the server into a pure referee hosting named two-player rooms with spectators, a joinable lobby, and a 10-second disconnect grace with automatic seat restoration.

**Architecture:** Identity-aware dumb pipes — `ServerTcp` tags each connection with a `Guid` and gains `SendToAsync`; all semantics live above the pipe in a server-side `LobbyService` that owns `Room` instances (each wrapping the existing `TicTacToe` engine). Clients run a `PlayerSession` state machine (Connecting → Lobby → Seated → Reconnecting) consumed by three windows: `ServerWindow` (referee view), `LobbyWindow` (list/create/join), `GameWindow` (board; client-only now).

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, System.Text.Json polymorphic envelopes (inbox), xUnit. No new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-08-23-rooms-neutral-referee-design.md`

## Global Constraints

- Target framework `net10.0-windows` comes from `Directory.Build.props`; never set per-project.
- Every build must finish **0 warnings / 0 errors** (`TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings`, `EnforceCodeStyleInBuild` all on).
- Package versions live only in `Directory.Packages.props`. Existing pins: Microsoft.NET.Test.Sdk 18.9.0, xunit 2.9.3, xunit.runner.visualstudio 4.0.0.
- File-scoped namespaces, braces everywhere; root namespace `Client_Server_App`, game/server logic in `Client_Server_App.Game`.
- No public property returns an array type (CA1819 is an error here) — expose `IReadOnlyList<T>`.
- Every `catch` must filter specific exception types; bare catches trip CA1031 as errors.
- The WPF SDK excludes `System.IO` from implicit usings — import it explicitly wherever `IOException` or streams appear.
- Window classes must stay `public` (WPF partial requirement) but their constructors take internal service types, so declare those constructors `internal` (avoids CS0051).
- New types are `internal`; tests reach them via the existing `<InternalsVisibleTo Include="Client-Server-App.Tests" />`.
- Wire format: newline-delimited UTF-8 JSON, camelCase (`JsonSerializerDefaults.Web`), `"type"` discriminator.
- Timing: server grace default **10 seconds** (injectable; tests use milliseconds); client reconnect budget default **15 seconds** (injectable), retry interval 1 second.
- Locking discipline for `Room`: never call another locking method while holding `_sync`; all sends and event invocations happen outside the lock. The code below follows it — preserve it when editing.
- Every task leaves the solution compiling and tests green; the order is additive-first, deletions-last.
- Working directory for all commands: repository root.

## File Structure

| File | Responsibility |
| --- | --- |
| `Client-Server-App/Game/TicTacToe.cs` | Modify: add `DeclareForfeit(Player)`. |
| `Client-Server-App/Game/GameEnvelope.cs` | Modify: `GameStateRecord` gains `Room`/`WinnerReason`; eight new envelope records. |
| `Client-Server-App/Game/Transports.cs` | Modify: identity-aware `IServerTransport`. |
| `Client-Server-App/ServerTcp.cs` | Modify: `Guid` ids, targeted sends. |
| `Client-Server-App/Game/Room.cs` | Create: membership, authority, grace/forfeit/restore, rematch. |
| `Client-Server-App/Game/LobbyService.cs` | Create: room table, routing, validation, list pushes. |
| `Client-Server-App/Game/PlayerSession.cs` | Create: client state machine + auto-reconnect. |
| `Client-Server-App/ServerWindow.xaml(.cs)` | Create: referee UI. |
| `Client-Server-App/LobbyWindow.xaml(.cs)` | Create: room browser UI. |
| `Client-Server-App/GameWindow.xaml(.cs)` | Rewrite: client-only, Leave button, reconnect banner, spectator mode. |
| `Client-Server-App/ConnectionWindow.xaml.cs` | Modify: host → referee; connect → lobby. |
| Deleted | `Game/TicTacToeHostService.cs`, `Game/TicTacToeClientService.cs`, `Game/GameRoles.cs` + their tests + old E2E. |
| Tests | New: `RoomTests`, `LobbyServiceTests`, `PlayerSessionTests`, `TicTacToeRoomsEndToEndTests`; rewritten: `FakeServerTransport`, `ServerTcpConnectionEventsTests`; extended: `GameJsonTests`. |

---

### Task 1: Protocol extensions and forfeit support in the domain

**Files:**
- Modify: `Client-Server-App/Game/TicTacToe.cs`
- Modify: `Client-Server-App/Game/GameEnvelope.cs`
- Modify: `tests/Client-Server-App.Tests/Game/GameJsonTests.cs`

**Interfaces:**
- Consumes: existing `GameEnvelope`, `GameJson`, `TicTacToe`.
- Produces (exact shapes later tasks rely on):
  - `GameStateRecord(..., int Round, string Room = "", string? WinnerReason = null)` — optional trailing params keep all existing construction sites compiling.
  - `CreateRoomRecord(string Name)`, `JoinRoomRecord(string Name)`, `LeaveRoomRecord`, `RoomInfoRecord(string Name, int Players, int Spectators)` (+ `string Label`), `RoomListRecord(IReadOnlyList<RoomInfoRecord>)`, `JoinedRecord(string Room, string? Mark, bool Restored, GameStateRecord State)`, `LeftRecord(string Reason)`, `ErrorRecord(string Message)` — all `: GameEnvelope`, all registered in `[JsonDerivedType]`.
  - `void TicTacToe.DeclareForfeit(Player winner)` — no-op unless `InProgress`; sets `Won`/`Winner`, clears `WinningLine`.

- [ ] **Step 1: Add `DeclareForfeit`**

In `Client-Server-App/Game/TicTacToe.cs`, insert after the `Reset` method:

```csharp
    /// <summary>Awards the game to <paramref name="winner"/> (disconnect grace expiry).</summary>
    public void DeclareForfeit(Player winner)
    {
        if (Status != GameStatus.InProgress)
        {
            return;
        }

        Status = GameStatus.Won;
        Winner = winner;
        WinningLine = null;
    }
```

- [ ] **Step 2: Replace `GameEnvelope.cs` contents**

```csharp
using System.Text.Json.Serialization;

namespace Client_Server_App.Game;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MoveRequestRecord), "moveRequest")]
[JsonDerivedType(typeof(RematchOfferRecord), "rematchOffer")]
[JsonDerivedType(typeof(GameStateRecord), "state")]
[JsonDerivedType(typeof(CreateRoomRecord), "createRoom")]
[JsonDerivedType(typeof(JoinRoomRecord), "joinRoom")]
[JsonDerivedType(typeof(LeaveRoomRecord), "leaveRoom")]
[JsonDerivedType(typeof(RoomListRecord), "roomList")]
[JsonDerivedType(typeof(JoinedRecord), "joined")]
[JsonDerivedType(typeof(LeftRecord), "left")]
[JsonDerivedType(typeof(ErrorRecord), "error")]
internal abstract record GameEnvelope;

internal sealed record MoveRequestRecord(int Cell) : GameEnvelope;

internal sealed record RematchOfferRecord : GameEnvelope;

internal sealed record GameStateRecord(
    IReadOnlyList<string> Board,
    string Turn,
    string Status,
    string? Winner,
    IReadOnlyList<int>? WinningLine,
    int Round,
    string Room = "",
    string? WinnerReason = null) : GameEnvelope;

internal sealed record CreateRoomRecord(string Name) : GameEnvelope;

internal sealed record JoinRoomRecord(string Name) : GameEnvelope;

internal sealed record LeaveRoomRecord : GameEnvelope;

internal sealed record RoomInfoRecord(string Name, int Players, int Spectators) : GameEnvelope
{
    public string Label => $"{Players} player(s), {Spectators} spectator(s)";
}

internal sealed record RoomListRecord(IReadOnlyList<RoomInfoRecord> Rooms) : GameEnvelope;

/// <summary>Targeted acknowledgement after joining. A null <paramref name="Mark"/> means spectator.</summary>
internal sealed record JoinedRecord(string Room, string? Mark, bool Restored, GameStateRecord State) : GameEnvelope;

internal sealed record LeftRecord(string Reason) : GameEnvelope;

internal sealed record ErrorRecord(string Message) : GameEnvelope;
```

- [ ] **Step 3: Extend `GameJsonTests`**

In `tests/Client-Server-App.Tests/Game/GameJsonTests.cs`:

(a) In `TryParse_RoundTripsEveryEnvelopeKind`, change the two `GameStateRecord` literals to end with `, 1, "friday")` and `, 3, "friday")` respectively, and extend the field-by-field comparison block with:

```csharp
                    Assert.Equal(o.Room, p.Room);
                    Assert.Equal(o.WinnerReason, p.WinnerReason);
```

(b) Add these facts and helper inside the test class:

```csharp
    [Fact]
    public void Serialize_State_CarriesRoomAndWinnerReason()
    {
        string json = GameJson.Serialize(new GameStateRecord(
            ["X", "", "", "", "", "", "", "", ""], "O", "won", "O", null, 1, "friday", "forfeit"));

        Assert.Contains("\"room\":\"friday\"", json);
        Assert.Contains("\"winnerReason\":\"forfeit\"", json);
    }

    [Fact]
    public void TryParse_RoundTripsLobbyEnvelopes()
    {
        GameEnvelope[] originals =
        [
            new CreateRoomRecord("friday"),
            new JoinRoomRecord("friday"),
            new LeaveRoomRecord(),
            new RoomListRecord([new RoomInfoRecord("friday", 1, 2), new RoomInfoRecord("duel", 2, 0)]),
            new JoinedRecord("friday", null, true, NewState()),
            new JoinedRecord("friday", "X", false, NewState()),
            new LeftRecord("roomClosed"),
            new ErrorRecord("Room name already taken."),
        ];

        foreach (GameEnvelope original in originals)
        {
            GameEnvelope parsed = GameJson.TryParse(GameJson.Serialize(original))!;

            Assert.Equal(original.GetType(), parsed.GetType());
            switch (original, parsed)
            {
                case (MoveRequestRecord o, MoveRequestRecord p):
                    Assert.Equal(o.Cell, p.Cell);
                    break;

                case (GameStateRecord o, GameStateRecord p):
                    Assert.Equal(o.Board, p.Board);
                    Assert.Equal(o.Round, p.Round);
                    Assert.Equal(o.Room, p.Room);
                    break;

                case (RoomListRecord o, RoomListRecord p):
                    Assert.Equal(o.Rooms.Select(r => r.Name), p.Rooms.Select(r => r.Name));
                    break;

                case (JoinedRecord o, JoinedRecord p):
                    Assert.Equal(o.Room, p.Room);
                    Assert.Equal(o.Mark, p.Mark);
                    Assert.Equal(o.Restored, p.Restored);
                    break;

                case (LeftRecord o, LeftRecord p):
                    Assert.Equal(o.Reason, p.Reason);
                    break;

                case (ErrorRecord o, ErrorRecord p):
                    Assert.Equal(o.Message, p.Message);
                    break;
            }
        }
    }

    private static GameStateRecord NewState() =>
        new(["", "", "", "", "", "", "", "", ""], "X", "inProgress", null, null, 1, "friday");
```

(The existing `RematchOfferRecord` case falls through the switch untouched.)

- [ ] **Step 4: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~GameJsonTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 5: Commit**

```bash
git add Client-Server-App/Game/TicTacToe.cs Client-Server-App/Game/GameEnvelope.cs tests/Client-Server-App.Tests/Game/GameJsonTests.cs
git commit -m "feat: add room protocol envelopes and forfeit support"
```

---

### Task 2: `Room` engine (pure, transport-free)

**Files:**
- Create: `Client-Server-App/Game/Room.cs`
- Create: `tests/Client-Server-App.Tests/Game/RoomTests.cs`

**Interfaces:**
- Consumes: `TicTacToe` + `DeclareForfeit`, envelopes (Task 1).
- Produces:
  - `sealed class Room` — ctor `Room(string name, Action<Guid, string> sendTo, Action<string> log, TimeSpan gracePeriod)`
  - Properties: `string Name`, `int PlayerCount`, `int SpectatorCount`, `bool HasVacancy`
  - Methods: `Seat(Guid)`, `AddSpectator(Guid)`, `Contains(Guid): bool`, `HandleLeave(Guid)`, `HandleDisconnect(Guid)`, `HandleMove(Guid, int)`, `HandleRematchOffer(Guid)`, `HandleChat(Guid, string)`, `Dispose()`
  - Events: `Action? MembershipChanged`, `Action<Guid>? MemberReleased`, `Action<IReadOnlyList<Guid>>? RoomClosed`

Behavioral contract (LobbyService + tests depend on it exactly):
- `Seat` fills X then O, sends `joined{mark, restored, snapshot}` to the seated id, cancels any active grace (reporting `restored:true`), and broadcasts an initial `state` when seating completes the pair on a fresh board.
- After a rematch swap, BOTH seats receive a fresh `joined` (marks updated) followed by the new `state` — this is how clients learn their new mark.
- Disconnecting a player who has an opponent starts grace; expiry awards the opponent a forfeit win (`winnerReason:"forfeit"`) and frees the seat. Disconnecting a lone player closes the room immediately. Disconnecting a spectator releases silently.
- Explicit `HandleLeave` by a seated player with an opponent present mid-game = immediate forfeit, then release; released members receive `left{"left"}`.
- Last member leaving closes the room (`left{"roomClosed"}` only to remaining members, then `RoomClosed(ids)` — invoked even for an empty list so the lobby can deregister).
- Chat lines relay verbatim to current members only.

- [ ] **Step 1: Implement `Room`**

Create `Client-Server-App/Game/Room.cs`:

```csharp
namespace Client_Server_App.Game;

/// <summary>
/// One named tic-tac-toe room: two player seats, spectators, authoritative game
/// state, disconnect grace. Emits every outgoing line through the injected
/// <paramref name="sendTo"/> delegate; owns no sockets and no timers but its own
/// cancellable grace delay.
/// </summary>
internal sealed class Room : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Guid> _spectators = [];
    private readonly Action<Guid, string> _sendTo;
    private readonly Action<string> _log;
    private readonly TimeSpan _gracePeriod;
    private readonly TicTacToe _game = new();
    private CancellationTokenSource? _graceCts;
    private Guid? _playerX;
    private Guid? _playerO;
    private bool _xWantsRematch;
    private bool _oWantsRematch;
    private string? _graceDroppedMark;
    private string? _winnerReason;
    private int _round = 1;
    private bool _closed;

    public event Action? MembershipChanged;
    public event Action<Guid>? MemberReleased;
    public event Action<IReadOnlyList<Guid>>? RoomClosed;

    public Room(string name, Action<Guid, string> sendTo, Action<string> log, TimeSpan gracePeriod)
    {
        Name = name;
        _sendTo = sendTo;
        _log = log;
        _gracePeriod = gracePeriod;
    }

    public string Name { get; }

    public int PlayerCount
    {
        get
        {
            lock (_sync)
            {
                return PlayerCountCore();
            }
        }
    }

    public int SpectatorCount
    {
        get
        {
            lock (_sync)
            {
                return _spectators.Count;
            }
        }
    }

    public bool HasVacancy
    {
        get
        {
            lock (_sync)
            {
                return _playerX is null || _playerO is null;
            }
        }
    }

    public void Seat(Guid id)
    {
        string mark;
        bool restored;
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            if (_playerX is null)
            {
                _playerX = id;
                mark = "X";
            }
            else
            {
                _playerO = id;
                mark = "O";
            }

            restored = _graceCts is not null;
            CancelGraceCore();
        }

        SendTo(id, new JoinedRecord(Name, mark, restored, SnapshotCore()));
        NotifyMembershipChanged();

        bool started;
        lock (_sync)
        {
            started = _playerX is not null
                && _playerO is not null
                && _game.Status == GameStatus.InProgress
                && _game.Board.All(cell => cell is null);
        }

        if (started)
        {
            BroadcastState();
        }
    }

    public void AddSpectator(Guid id)
    {
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _spectators.Add(id);
        }

        SendTo(id, new JoinedRecord(Name, null, restored: false, SnapshotCore()));
        NotifyMembershipChanged();
    }

    public bool Contains(Guid id)
    {
        lock (_sync)
        {
            return IsMemberCore(id);
        }
    }

    public void HandleMove(Guid id, int cell)
    {
        string? line;
        List<Guid> recipients;
        lock (_sync)
        {
            string? mark = MarkOfCore(id);
            if (mark is null || _closed)
            {
                return;
            }

            _ = _game.TryApplyMove(cell, ParseMark(mark));
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        SendToEach(recipients, line);
    }

    public void HandleRematchOffer(Guid id)
    {
        bool restart;
        lock (_sync)
        {
            if (id == _playerX)
            {
                _xWantsRematch = true;
            }
            else if (id == _playerO)
            {
                _oWantsRematch = true;
            }
            else
            {
                return;
            }

            restart = _xWantsRematch && _oWantsRematch;
        }

        if (restart)
        {
            StartNextRound();
        }
    }

    public void HandleChat(Guid id, string line)
    {
        List<Guid> recipients;
        lock (_sync)
        {
            if (!IsMemberCore(id))
            {
                return;
            }

            recipients = MembersCore();
        }

        foreach (Guid recipient in recipients)
        {
            SendTo(recipient, line);
        }
    }

    /// <summary>Explicit leave. A seated player with an opponent present forfeits immediately.</summary>
    public void HandleLeave(Guid id)
    {
        string? guiltyMark;
        lock (_sync)
        {
            guiltyMark = OccupiedMarkCore(id);
        }

        if (guiltyMark is not null && OpponentPresentCore())
        {
            AwardForfeit(guiltyMark);
        }

        Release(id, notifySelf: true);
    }

    /// <summary>Unsolicited transport loss. Players trigger the grace flow.</summary>
    public void HandleDisconnect(Guid id)
    {
        string? droppedMark;
        bool opponentPresent;
        lock (_sync)
        {
            droppedMark = OccupiedMarkCore(id);
            opponentPresent = PlayerCountCore() == 2;
        }

        if (droppedMark is null)
        {
            Release(id, notifySelf: false);
            return;
        }

        FreeSeat(droppedMark);

        bool closeNow;
        lock (_sync)
        {
            closeNow = PlayerCountCore() == 0;
        }

        if (closeNow)
        {
            RequestClose("roomClosed");
            return;
        }

        if (opponentPresent)
        {
            StartGrace(droppedMark);
        }

        NotifyMembershipChanged();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            CancelGraceCore();
        }
    }

    private void StartGrace(string droppedMark)
    {
        CancellationToken token;
        lock (_sync)
        {
            CancelGraceCore();
            _graceDroppedMark = droppedMark;
            _graceCts = new CancellationTokenSource();
            token = _graceCts.Token;
        }

        _log($"[{Name}] A player lost connection. Grace period: {_gracePeriod.TotalSeconds:0.#}s.");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_gracePeriod, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string? guiltyMark;
            bool closeRoom;
            lock (_sync)
            {
                if (_closed || _graceCts is null)
                {
                    return; // restored or closed meanwhile
                }

                CancelGraceCore();
                guiltyMark = _graceDroppedMark;
                closeRoom = PlayerCountCore() == 0;
            }

            _log($"[{Name}] Grace period expired.");
            if (closeRoom || guiltyMark is null)
            {
                RequestClose("roomClosed");
                return;
            }

            AwardForfeit(guiltyMark);
        });
    }

    private void AwardForfeit(string guiltyMark)
    {
        List<Guid> recipients;
        string line;
        lock (_sync)
        {
            _winnerReason = "forfeit";
            if (_game.Status == GameStatus.InProgress)
            {
                _game.DeclareForfeit(ParseMark(guiltyMark == "X" ? "O" : "X"));
            }

            FreeSeatCore(guiltyMark);
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        SendToEach(recipients, line);
    }

    private void Release(Guid id, bool notifySelf)
    {
        bool wasSpectator;
        lock (_sync)
        {
            wasSpectator = _spectators.Remove(id);
            FreeSeatCore(OccupiedMarkCore(id));
        }

        if (notifySelf)
        {
            SendTo(id, new LeftRecord("left"));
        }

        if (notifySelf || wasSpectator)
        {
            MemberReleased?.Invoke(id);
        }

        bool closeRoom;
        lock (_sync)
        {
            closeRoom = !IsMemberCore(id) && PlayerCountCore() == 0 && _spectators.Count == 0;
        }

        if (closeRoom)
        {
            RequestClose("roomClosed");
        }
        else
        {
            NotifyMembershipChanged();
        }
    }

    private void StartNextRound()
    {
        List<(Guid Id, string Mark)> reseated;
        List<Guid> recipients;
        string line;
        lock (_sync)
        {
            (_playerX, _playerO) = (_playerO, _playerX);
            _xWantsRematch = false;
            _oWantsRematch = false;
            _winnerReason = null;
            _round++;
            _game.Reset(Player.X);

            reseated =
            [
                .. SeatsCore().Select(seat => (seat.Id, seat.Mark)),
            ];
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        foreach ((Guid id, string mark) in reseated)
        {
            SendTo(id, new JoinedRecord(Name, mark, restored: false, ParseSnapshot(line)));
        }

        SendToEach(recipients, line);
    }

    private void BroadcastState()
    {
        List<Guid> recipients;
        string line;
        lock (_sync)
        {
            line = SerializeSnapshotCore();
            recipients = MembersCore();
        }

        SendToEach(recipients, line);
    }

    private void RequestClose(string reason)
    {
        List<Guid> members;
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            CancelGraceCore();
            members = MembersCore();
            _playerX = null;
            _playerO = null;
            _spectators.Clear();
        }

        string line = GameJson.Serialize(new LeftRecord(reason));
        foreach (Guid member in members)
        {
            SendTo(member, line);
        }

        RoomClosed?.Invoke(members);
    }

    private void NotifyMembershipChanged() => MembershipChanged?.Invoke();

    private bool IsMemberCore(Guid id) =>
        _playerX == id || _playerO == id || _spectators.Contains(id);

    private int PlayerCountCore() => (_playerX is null ? 0 : 1) + (_playerO is null ? 0 : 1);

    private bool OpponentPresentCore() => PlayerCountCore() == 2;

    private string? OccupiedMarkCore(Guid id) =>
        _playerX == id ? "X" : _playerO == id ? "O" : null;

    private string? MarkOfCore(Guid id) => OccupiedMarkCore(id);

    private IEnumerable<(Guid Id, string Mark)> SeatsCore()
    {
        if (_playerX is { } x)
        {
            yield return (x, "X");
        }

        if (_playerO is { } o)
        {
            yield return (o, "O");
        }
    }

    private List<Guid> MembersCore()
    {
        List<Guid> members = [];
        foreach ((Guid id, _) in SeatsCore())
        {
            members.Add(id);
        }

        members.AddRange(_spectators);
        return members;
    }

    private void FreeSeat(string mark)
    {
        lock (_sync)
        {
            FreeSeatCore(mark);
        }
    }

    private void FreeSeatCore(string mark)
    {
        if (mark == "X")
        {
            _playerX = null;
            _xWantsRematch = false;
        }
        else if (mark == "O")
        {
            _playerO = null;
            _oWantsRematch = false;
        }
    }

    private GameStateRecord SnapshotCore() => ParseSnapshot(SerializeSnapshotCore());

    private string SerializeSnapshotCore() => GameJson.Serialize(BuildStateCore());

    private static GameStateRecord ParseSnapshot(string line) =>
        (GameStateRecord)GameJson.TryParse(line)!;

    private GameStateRecord BuildStateCore() => new(
        Board: _game.Board.Select(CellToString).ToArray(),
        Turn: _game.Turn.ToString(),
        Status: _game.Status switch
        {
            GameStatus.Won => "won",
            GameStatus.Draw => "draw",
            _ => "inProgress",
        },
        Winner: _game.Winner?.ToString(),
        WinningLine: _game.WinningLine?.ToArray(),
        Round: _round,
        Room: Name,
        WinnerReason: _winnerReason);

    private void CancelGraceCore()
    {
        if (_graceCts is { } cts)
        {
            _graceCts = null;
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void SendTo(Guid id, GameEnvelope message) => _sendTo(id, GameJson.Serialize(message));

    private void SendTo(IEnumerable<Guid> ids, string line)
    {
        foreach (Guid id in ids)
        {
            _sendTo(id, line);
        }
    }

    private static Player ParseMark(string mark) => mark == "X" ? Player.X : Player.O;

    private static string CellToString(Player? cell) => cell switch
    {
        Player.X => "X",
        Player.O => "O",
        _ => "",
    };
}
```

Implementation notes for the reviewer: (1) `HandleDisconnect` closes a room whose last player just vanished immediately — nobody exists to wait for, which is stricter than the spec's minimum and matches its intent; (2) every public method acquires `_sync` at most once and performs all sends/event-raising outside the lock — preserve this discipline when editing (nested acquisition would deadlock since no lock is reentrant); (3) `SnapshotCore`/`ParseSnapshot` round-trip through JSON so the room emits exactly the wire shape clients parse.

- [ ] **Step 2: Add the room tests**

Create `tests/Client-Server-App.Tests/Game/RoomTests.cs`:

```csharp
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class RoomTests
{
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(50);

    private readonly Dictionary<Guid, List<string>> _inbox = new();
    private readonly List<string> _logs = [];

    private Room CreateRoom(TimeSpan? grace = null) =>
        new(
            "friday",
            (id, line) =>
            {
                if (!_inbox.TryGetValue(id, out List<string> list))
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
        Assert.Equal(new[] { 0, 1, 2 }, state.WinningLine!.ToArray());
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
    public void LastMemberLeaving_ClosesRoom_AndFiresEvent()
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

        // Each member was individually released (MemberReleased); the close
        // event carries whoever remained at close time — nobody here.
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
```

Note: `LastMemberLeaving_ClosesRoom_AndFiresEvent` uses `await`, so mark that fact `public async Task` (the compiler enforces this; listed correctly here — apply `async Task` signature when writing it).

- [ ] **Step 3: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~RoomTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 4: Commit**

```bash
git add Client-Server-App/Game/Room.cs tests/Client-Server-App.Tests/Game/RoomTests.cs
git commit -m "feat: add room engine with grace and rematch"
```

---

### Task 3: Transport identity flip + `LobbyService` + referee window + wiring

The flip task: the server side moves to the identity model, the obsolete host service dies, and the referee instance becomes functional. Client gameplay code is untouched and still compiles.

**Files:**
- Modify: `Client-Server-App/Game/Transports.cs`
- Modify: `Client-Server-App/ServerTcp.cs`
- Create: `Client-Server-App/Game/LobbyService.cs`
- Create: `Client-Server-App/ServerWindow.xaml`, `Client-Server-App/ServerWindow.xaml.cs`
- Modify: `Client-Server-App/ConnectionWindow.xaml.cs` (host path only)
- Delete: `Client-Server-App/Game/TicTacToeHostService.cs`, `tests/Client-Server-App.Tests/Game/TicTacToeHostServiceTests.cs`, `tests/Client-Server-App.Tests/Integration/TicTacToeEndToEndTests.cs`
- Modify: `tests/Client-Server-App.Tests/TestDoubles/FakeServerTransport.cs` (rewrite), `tests/Client-Server-App.Tests/Integration/ServerTcpConnectionEventsTests.cs` (Guid signatures)

**Interfaces:**
- Consumes: `Room` (Task 2), envelopes (Task 1).
- Produces:
  - `IServerTransport`: events `Action<Guid>? ClientConnected`, `Action<Guid>? ClientDisconnected`, `Action<Guid, string>? MessageReceived`; methods `Task BroadcastLineAsync(string, CancellationToken = default)`, `Task SendToAsync(Guid, string, CancellationToken = default)`.
  - `sealed class LobbyService` — ctor `LobbyService(IServerTransport server, TimeSpan? gracePeriod = null)`; `void Start()`; `void Dispose()`; `IReadOnlyList<RoomInfoRecord> GetRooms()`; events `Action<string>? LogReceived`, `Action? RoomsChanged`.
  - `internal ServerWindow(LobbyService lobby, int port)`.

- [ ] **Step 1: Flip the transport interface and rewrite `ServerTcp`**

Replace the contents of `Client-Server-App/Game/Transports.cs`:

```csharp
namespace Client_Server_App.Game;

/// <summary>The referee-side view of the server transport: identity-aware pipes.</summary>
internal interface IServerTransport
{
    event Action<Guid>? ClientConnected;
    event Action<Guid>? ClientDisconnected;
    event Action<Guid, string>? MessageReceived;

    Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default);
    Task SendToAsync(Guid id, string message, CancellationToken cancellationToken = default);
}

/// <summary>The client-side view of the client transport (unchanged).</summary>
internal interface IClientTransport
{
    event Action<string>? MessageReceived;
    event Action? Disconnected;

    Task SendLineAsync(string message, CancellationToken cancellationToken = default);
}
```

Replace the contents of `Client-Server-App/ServerTcp.cs`:

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Asynchronous TCP server that tags every accepted connection with a GUID and
/// exchanges newline-delimited UTF-8 lines: broadcast or targeted.
/// </summary>
internal sealed class ServerTcp : IServerTransport, IDisposable
{
    private readonly ConcurrentDictionary<Guid, StreamWriter> _clients = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _writeLock = new(initialCount: 1, maxCount: 1);
    private readonly TcpListener _listener;
    private bool _disposed;

    /// <summary>Raised (on a worker thread) with the new connection's id.</summary>
    public event Action<Guid>? ClientConnected;

    /// <summary>Raised (on a worker thread) with the departed connection's id.</summary>
    public event Action<Guid>? ClientDisconnected;

    /// <summary>Raised (on a worker thread) for every line a connection sends.</summary>
    public event Action<Guid, string>? MessageReceived;

    /// <summary>The bound port; only meaningful after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ServerTcp(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener.Start();
        _ = AcceptLoopAsync(_cancellation.Token);
    }

    public async Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        Guid[] recipients;
        lock (_clients)
        {
            recipients = [.. _clients.Keys];
        }

        await WriteToRecipients(recipients, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendToAsync(Guid id, string message, CancellationToken cancellationToken = default)
    {
        await WriteToRecipients([id], message, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteToRecipients(Guid[] recipients, string message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            char[] buffer = (message + "\n").ToCharArray();
            foreach (Guid recipient in recipients)
            {
                if (_clients.TryGetValue(recipient, out StreamWriter? writer))
                {
                    await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                RegisterClient(client);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Expected when the server stops or the listener faults.
        }
    }

    private void RegisterClient(TcpClient client)
    {
        Guid id = Guid.NewGuid();
        _clients[id] = new StreamWriter(client.GetStream(), Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        _ = HandleClientAsync(id, client);
        ClientConnected?.Invoke(id);
    }

    private async Task HandleClientAsync(Guid id, TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream());
            while (await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false) is { } line)
            {
                MessageReceived?.Invoke(id, line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            // Expected when a client disconnects or the server shuts down.
        }
        finally
        {
            RemoveClient(id, client);
        }
    }

    private void RemoveClient(Guid id, TcpClient client)
    {
        if (_clients.TryRemove(id, out StreamWriter? writer))
        {
            writer.Dispose();
        }

        client.Dispose();
        ClientDisconnected?.Invoke(id);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellation.Cancel();

            foreach (KeyValuePair<Guid, StreamWriter> entry in _clients)
            {
                entry.Value.Dispose();
            }

            _clients.Clear();
            _writeLock.Dispose();
            _cancellation.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
```

(The single `_writeLock` serializes targeted sends against broadcasts so lines can never interleave mid-message.)

- [ ] **Step 2: Implement `LobbyService`**

Create `Client-Server-App/Game/LobbyService.cs`:

```csharp
using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Server-side referee: owns the room table, routes client envelopes, validates
/// room names, and pushes room-list updates to every connection.
/// </summary>
internal sealed class LobbyService : IDisposable
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(10);

    private readonly IServerTransport _server;
    private readonly TimeSpan _gracePeriod;
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Room> _membership = [];
    private readonly object _sync = new();

    public event Action<string>? LogReceived;
    public event Action? RoomsChanged;

    public LobbyService(IServerTransport server, TimeSpan? gracePeriod = null)
    {
        _server = server;
        _gracePeriod = gracePeriod ?? DefaultGracePeriod;
    }

    public void Start()
    {
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.MessageReceived += OnMessageReceived;
    }

    public IReadOnlyList<RoomInfoRecord> GetRooms()
    {
        lock (_sync)
        {
            return [.. _rooms.Values.Select(ToInfo)];
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (Room room in _rooms.Values)
            {
                room.Dispose();
            }

            _rooms.Clear();
            _membership.Clear();
        }
    }

    private void OnClientConnected(Guid id)
    {
        Log($"Client {Short(id)} connected.");
        PushRoomList();
    }

    private void OnClientDisconnected(Guid id)
    {
        Room? room;
        lock (_sync)
        {
            _membership.Remove(id, out room);
        }

        if (room is not null)
        {
            room.HandleDisconnect(id);
        }

        Log($"Client {Short(id)} disconnected.");
    }

    private void OnMessageReceived(Guid sender, string line)
    {
        switch (GameJson.TryParse(line))
        {
            case CreateRoomRecord create:
                HandleCreate(sender, create.Name);
                break;

            case JoinRoomRecord join:
                HandleJoin(sender, join.Name);
                break;

            case LeaveRoomRecord:
                HandleLeave(sender);
                break;

            case MoveRequestRecord move:
                Route(sender, room => room.HandleMove(sender, move.Cell));
                break;

            case RematchOfferRecord:
                Route(sender, room => room.HandleRematchOffer(sender));
                break;

            case GameStateRecord:
            case RoomListRecord:
            case JoinedRecord:
            case LeftRecord:
            case ErrorRecord:
                // Server-originated envelopes are never accepted from clients.
                break;

            case null:
                Route(sender, room => room.HandleChat(sender, line));
                break;
        }
    }

    private void HandleCreate(Guid creator, string rawName)
    {
        string name = rawName.Trim();
        if (name.Length is 0 or > 30)
        {
            SendError(creator, "Room name must be 1-30 characters.");
            return;
        }

        Room room;
        lock (_sync)
        {
            if (_rooms.ContainsKey(name))
            {
                SendError(creator, $"Room '{name}' already exists.");
                return;
            }

            room = new Room(
                name,
                (id, line) => _ = SafeSendAsync(id, line),
                Log,
                _gracePeriod);
            room.MembershipChanged += PushRoomList;
            room.MemberReleased += ReleaseMember;
            room.RoomClosed += ids => CloseRoom(room, ids);
            _rooms[name] = room;
        }

        // Seat outside the lobby lock: Room raises MembershipChanged synchronously,
        // which re-enters PushRoomList -> GetRooms (same lock).
        room.Seat(creator);
        lock (_sync)
        {
            _membership[creator] = room;
        }

        Log($"Room '{name}' created by {Short(creator)}.");
        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void HandleJoin(Guid joiner, string rawName)
    {
        string name = rawName.Trim();
        Room? room;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(name, out room))
            {
                SendError(joiner, $"Room '{name}' does not exist.");
                return;
            }
        }

        if (room.HasVacancy)
        {
            room.Seat(joiner);
        }
        else
        {
            room.AddSpectator(joiner);
        }

        lock (_sync)
        {
            _membership[joiner] = room;
        }

        Log($"{Short(joiner)} joined '{room.Name}'.");
        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void HandleLeave(Guid id)
    {
        Room? room;
        lock (_sync)
        {
            _membership.Remove(id, out room);
        }

        room?.HandleLeave(id);
    }

    private void Route(Guid sender, Action<Room> action)
    {
        Room? room;
        lock (_sync)
        {
            _membership.TryGetValue(sender, out room);
        }

        if (room is not null)
        {
            action(room);
        }
    }

    private void ReleaseMember(Guid id)
    {
        lock (_sync)
        {
            _membership.Remove(id);
        }

        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void CloseRoom(Room room, IReadOnlyList<Guid> evicted)
    {
        lock (_sync)
        {
            _rooms.Remove(room.Name);
            foreach (Guid id in evicted)
            {
                _membership.Remove(id);
            }
        }

        Log($"Room '{room.Name}' closed.");
        PushRoomList();
        RoomsChanged?.Invoke();
    }

    private void PushRoomList()
    {
        _ = _server.BroadcastLineAsync(GameJson.Serialize(new RoomListRecord(GetRooms())));
    }

    private void SendError(Guid id, string message)
    {
        _ = SafeSendAsync(id, GameJson.Serialize(new ErrorRecord(message)));
        Log($"Error for {Short(id)}: {message}");
    }

    private async Task SafeSendAsync(Guid id, string line)
    {
        try
        {
            await _server.SendToAsync(id, line).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Recipient vanished mid-write; disconnect handling takes over.
        }
    }

    private static RoomInfoRecord ToInfo(Room room) =>
        new(room.Name, room.PlayerCount, room.SpectatorCount);

    private void Log(string message) => LogReceived?.Invoke(message);

    private static string Short(Guid id) => id.ToString()[..8];
}
```

Reentrancy note (do not "simplify" away): `HandleCreate`/`HandleJoin` deliberately seat the player OUTSIDE the lobby lock because `Room.Seat` synchronously raises `MembershipChanged`, which calls back into `PushRoomList` → `GetRooms`.

- [ ] **Step 3: Create the referee window**

Create `Client-Server-App/ServerWindow.xaml`:

```xml
<Window x:Class="Client_Server_App.ServerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="Referee" Height="420" Width="520"
        ResizeMode="NoResize" WindowStartupLocation="CenterOwner">
	<Grid Margin="12">
		<Grid.RowDefinitions>
			<RowDefinition Height="Auto" />
			<RowDefinition Height="*" />
			<RowDefinition Height="Auto" />
			<RowDefinition Height="150" />
		</Grid.RowDefinitions>

		<Grid Grid.Row="0" Margin="0,0,0,4">
			<Grid.ColumnDefinitions>
				<ColumnDefinition Width="*" />
				<ColumnDefinition Width="90" />
				<ColumnDefinition Width="100" />
			</Grid.ColumnDefinitions>
			<TextBlock Text="Room" FontWeight="Bold" />
			<TextBlock Grid.Column="1" FontWeight="Bold" Text="Players" />
			<TextBlock Grid.Column="2" FontWeight="Bold" Text="Spectators" />
		</Grid>

		<StackPanel x:Name="RoomsPanel" Grid.Row="1" />

		<TextBlock Grid.Row="2" Text="Events" FontWeight="Bold" Margin="0,8,0,4" />

		<TextBox x:Name="OutputTextBox"
				 Grid.Row="3"
				 IsReadOnly="True"
				 FontFamily="Consolas"
				 TextWrapping="Wrap"
				 VerticalScrollBarVisibility="Auto" />
	</Grid>
</Window>
```

Create `Client-Server-App/ServerWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>Read-only referee view: live room table plus an event log.</summary>
public partial class ServerWindow : Window
{
    private readonly LobbyService _lobby;

    internal ServerWindow(LobbyService lobby, int port)
    {
        _lobby = lobby;
        InitializeComponent();
        Title = $"Referee — port {port}";
        lobby.LogReceived += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        lobby.RoomsChanged += () => Dispatcher.BeginInvoke(RenderRooms);
        RenderRooms();
    }

    private void AppendLog(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    private void RenderRooms()
    {
        RoomsPanel.Children.Clear();
        foreach (RoomInfoRecord room in _lobby.GetRooms())
        {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            TextBlock name = new() { Text = room.Name };
            TextBlock players = new() { Text = $"{room.Players}/2" };
            TextBlock spectators = new() { Text = room.Spectators.ToString() };
            Grid.SetColumn(players, 1);
            Grid.SetColumn(spectators, 2);

            row.Children.Add(name);
            row.Children.Add(players);
            row.Children.Add(spectators);
            row.Margin = new Thickness(0, 2, 0, 2);

            RoomsPanel.Children.Add(row);
        }
    }
}
```

- [ ] **Step 4: Rewire the host path; delete the host service**

In `Client-Server-App/ConnectionWindow.xaml.cs` replace `HostButton_Click` with the version below and add the `OpenRefereeWindow` helper next to it (leave the connect path untouched — Task 6 handles it):

```csharp
    private void HostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePort(HostPortTextBox.Text, out int port))
        {
            AppendLog($"'{HostPortTextBox.Text}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        _server?.Dispose();
        ServerTcp server = new(port);
        try
        {
            server.Start();
            _server = server;
            AppendLog($"Listening on port {port}.");

            LobbyService lobby = new(server);
            lobby.Start();
            OpenRefereeWindow(
                () => new ServerWindow(lobby, port),
                onClose: () =>
                {
                    lobby.Dispose();
                    server.Dispose();
                    if (ReferenceEquals(_server, server))
                    {
                        _server = null;
                    }
                });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            AppendLog($"Could not start the host: {ex.Message}");
            server.Dispose();
        }
    }

    private void OpenRefereeWindow(Func<ServerWindow> createWindow, Action onClose)
    {
        ServerWindow window = createWindow();
        window.Owner = this;
        window.Closed += (_, _) => onClose();
        window.Show();
    }
```

Also add `using Client_Server_App.Game;` at the top of the file if not already present, and delete the old `server.MessageReceived += ...` log hookup that no longer compiles.

Delete obsolete artifacts:

```bash
git rm Client-Server-App/Game/TicTacToeHostService.cs tests/Client-Server-App.Tests/Game/TicTacToeHostServiceTests.cs tests/Client-Server-App.Tests/Integration/TicTacToeEndToEndTests.cs
```

- [ ] **Step 5: Rewrite fake transport; update integration tests**

Replace `tests/Client-Server-App.Tests/TestDoubles/FakeServerTransport.cs`:

```csharp
using Client_Server_App.Game;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class FakeServerTransport : IServerTransport
{
    private readonly Dictionary<Guid, List<string>> _inboxes = [];

    public event Action<Guid>? ClientConnected;
    public event Action<Guid>? ClientDisconnected;
    public event Action<Guid, string>? MessageReceived;

    public List<(Guid? Target, string Line)> Sent { get; } = [];

    public Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        Sent.Add((null, message));
        foreach (Guid id in _inboxes.Keys)
        {
            Deliver(id, message);
        }

        return Task.CompletedTask;
    }

    public Task SendToAsync(Guid id, string message, CancellationToken cancellationToken = default)
    {
        Sent.Add((id, message));
        Deliver(id, message);
        return Task.CompletedTask;
    }

    public Guid SimulateClientConnected()
    {
        Guid id = Guid.NewGuid();
        _inboxes[id] = [];
        ClientConnected?.Invoke(id);
        return id;
    }

    public void SimulateClientDisconnected(Guid id)
    {
        _inboxes.Remove(id);
        ClientDisconnected?.Invoke(id);
    }

    public void ReceiveLine(Guid id, string line) => MessageReceived?.Invoke(id, line);

    public IReadOnlyList<string> Inbox(Guid id) =>
        _inboxes.GetValueOrDefault(id, []);

    private void Deliver(Guid id, string message)
    {
        if (!_inboxes.TryGetValue(id, out List<string> list))
        {
            list = [];
            _inboxes[id] = list;
        }

        list.Add(message);
    }
}
```

Replace `tests/Client-Server-App.Tests/Integration/ServerTcpConnectionEventsTests.cs`:

```csharp
using Client_Server_App;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class ServerTcpConnectionEventsTests
{
    [Fact]
    public async Task ClientLifecycle_RaisesIdentityEvents()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource<Guid> connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Guid> disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? registeredId = null;
        server.ClientConnected += id =>
        {
            registeredId = id;
            connected.TrySetResult(id);
        };
        server.ClientDisconnected += id => disconnected.TrySetResult(id);

        server.Start();
        Assert.True(server.Port > 0);

        using ClientTcp client = new();
        await client.ConnectAsync("127.0.0.1", server.Port);
        Guid serverSideId = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(Guid.Empty, serverSideId);
        Assert.Equal(serverSideId, registeredId);

        client.Dispose();
        Assert.Equal(serverSideId, await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SendTo_ReachesExactlyOneClient()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource<Guid> firstConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Guid> secondConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0;
        server.ClientConnected += id =>
        {
            arrivals++;
            _ = arrivals == 1
                ? firstConnected.TrySetResult(id)
                : secondConnected.TrySetResult(id);
        };

        server.Start();

        using ClientTcp first = new();
        TaskCompletionSource<string> firstReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        first.MessageReceived += line => firstReceived.TrySetResult(line);
        await first.ConnectAsync("127.0.0.1", server.Port);
        Guid firstId = await firstConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using ClientTcp second = new();
        int secondSeen = 0;
        second.MessageReceived += _ => secondSeen++;
        await second.ConnectAsync("127.0.0.1", server.Port);
        _ = await secondConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await server.SendToAsync(firstId, "just-you");

        Assert.Equal("just-you", await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(200);
        Assert.Equal(0, secondSeen);
    }
}
```

- [ ] **Step 6: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~ServerTcpConnectionEventsTests|FullyQualifiedName~RoomTests|FullyQualifiedName~TicTacToeClientServiceTests"
dotnet build Client-Server-App.slnx -c Debug
dotnet build Client-Server-App.slnx -c Release
```

(`TicTacToeClientService` still exists and must stay green — it is deleted in Task 6.)

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: identity-aware transport and lobby referee service"
```

---

### Task 4: `LobbyService` behavioral tests

**Files:**
- Create: `tests/Client-Server-App.Tests/Game/LobbyServiceTests.cs`

**Interfaces:**
- Consumes: `FakeServerTransport` (Task 3), `LobbyService` (Task 3).
- Produces: regression coverage only.

- [ ] **Step 1: Write the suite**

Create `tests/Client-Server-App.Tests/Game/LobbyServiceTests.cs`:

```csharp
using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Xunit;

namespace Client_Server_App.Tests.Game;

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
        _transport.Inbox(second).Clear();

        _transport.ReceiveLine(second, GameJson.Serialize(new CreateRoomRecord("FRIDAY")));

        Assert.Contains("already exists", LastError(second).Message);
        Assert.False(_transport.Inbox(second).Any(l => l.Contains("\"type\":\"joined\"", StringComparison.Ordinal)));
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
    public void MoveRequests_RouteThroughRoom_SpectatorMoveIgnored()
    {
        using LobbyService lobby = CreateLobby();
        Guid creator = _transport.SimulateClientConnected();
        Guid second = _transport.SimulateClientConnected();
        Guid third = _transport.SimulateClientConnected();
        _transport.ReceiveLine(creator, GameJson.Serialize(new CreateRoomRecord("duel")));
        _transport.ReceiveLine(second, GameJson.Serialize(new JoinRoomRecord("duel")));
        _transport.ReceiveLine(third, GameJson.Serialize(new JoinRoomRecord("duel")));

        _transport.ReceiveLine(third, GameJson.Serialize(new MoveRequestRecord(0)));
        Assert.Equal("", LastState(third).Board[0]);

        _transport.ReceiveLine(creator, GameJson.Serialize(new MoveRequestRecord(0)));
        Assert.Equal("X", LastState(second).Board[0]);
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

        await Task.Delay(250);
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

        Assert.Empty(LastRoomList(creator).Rooms);
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

        Assert.True(_transport.Inbox(second).Any(l => l.Contains("\"type\":\"left\"", StringComparison.Ordinal)));
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
```

- [ ] **Step 2: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~LobbyServiceTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 3: Commit**

```bash
git add tests/Client-Server-App.Tests/Game/LobbyServiceTests.cs
git commit -m "test: cover lobby service behaviors"
```

---

### Task 5: `PlayerSession` client state machine

**Files:**
- Create: `Client-Server-App/Game/PlayerSession.cs`
- Create: `tests/Client-Server-App.Tests/Game/PlayerSessionTests.cs`

**Interfaces:**
- Consumes: `IClientTransport` (unchanged shape), envelopes (Task 1).
- Produces:
  - `enum PlayerSessionState { Connecting, Lobby, Seated, Reconnecting, Disconnected }`
  - `sealed class PlayerSession` — ctor `PlayerSession(Func<Task<IClientTransport>> connectFactory, TimeSpan? reconnectBudget = null)` (default budget 15 s, retry every 1 s)
  - Properties: `PlayerSessionState State`, `string? CurrentRoomName`, `string? MyMark`, `bool IsSpectator`, `GameStateRecord? CurrentState`, `IReadOnlyList<RoomInfoRecord> LatestRooms`
  - Events: `Action<IReadOnlyList<RoomInfoRecord>>? RoomsUpdated`, `Action<JoinedRecord>? Seated`, `Action<GameStateRecord>? StateReceived`, `Action<string>? ReturnedToLobby`, `Action<string>? ErrorReceived`, `Action<string>? LogReceived`, `Action? ReconnectingStarted`
  - Methods: `Task ConnectAsync()`, `Task CreateRoomAsync(string name)`, `Task JoinRoomAsync(string name)`, `Task LeaveRoomAsync()`, `Task PlayCellAsync(int cell)`, `Task SendRematchOfferAsync()`, `void Dispose()`

Behavioral contract:
- `ConnectAsync` opens the first connection via the factory and transitions Connecting → Lobby; it throws when unreachable (caller reports).
- A `joined` envelope seats the session (any prior state, including Reconnecting) and records mark/room/snapshot; spectators get `mark == null`.
- Unexpected transport death while Seated starts the reconnect loop: retry the factory every second until the budget expires, re-send `joinRoom{CurrentRoomName}` on each new transport; a `joined` ack completes recovery. Budget exhaustion returns to lobby with an error notice.
- Transport death in any other state → `Disconnected` + `ErrorReceived("Connection lost.")`.
- `LeaveRoomAsync` sends `leaveRoom` and returns to lobby locally (uniform UI signal); the server's duplicate `left{"left"}` may raise `ReturnedToLobby("left")` again — windows handle repeated signals idempotently.
- Sends are swallowed on broken pipes; the `Disconnected` event drives recovery.

- [ ] **Step 1: Implement `PlayerSession`**

Create `Client-Server-App/Game/PlayerSession.cs`:

```csharp
using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Client-side session: lobby interactions, seated gameplay, and automatic
/// reconnect-to-seat after unexpected transport loss.
/// </summary>
internal sealed class PlayerSession : IDisposable
{
    public static readonly TimeSpan DefaultReconnectBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private readonly Func<Task<IClientTransport>> _connectFactory;
    private readonly TimeSpan _reconnectBudget;
    private readonly CancellationTokenSource _disposal = new();
    private IClientTransport? _transport;

    public event Action<IReadOnlyList<RoomInfoRecord>>? RoomsUpdated;
    public event Action<JoinedRecord>? Seated;
    public event Action<GameStateRecord>? StateReceived;
    public event Action<string>? ReturnedToLobby;
    public event Action<string>? ErrorReceived;
    public event Action<string>? LogReceived;
    public event Action? ReconnectingStarted;

    public PlayerSessionState State { get; private set; } = PlayerSessionState.Connecting;
    public string? CurrentRoomName { get; private set; }
    public string? MyMark { get; private set; }
    public bool IsSpectator => State == PlayerSessionState.Seated && MyMark is null;
    public GameStateRecord? CurrentState { get; private set; }
    public IReadOnlyList<RoomInfoRecord> LatestRooms { get; private set; } = [];

    public PlayerSession(Func<Task<IClientTransport>> connectFactory, TimeSpan? reconnectBudget = null)
    {
        _connectFactory = connectFactory;
        _reconnectBudget = reconnectBudget ?? DefaultReconnectBudget;
    }

    /// <summary>Opens the first connection. Throws when the server is unreachable.</summary>
    public async Task ConnectAsync()
    {
        IClientTransport transport = await _connectFactory().ConfigureAwait(true);
        AttachTransport(transport);
        State = PlayerSessionState.Lobby;
    }

    public async Task CreateRoomAsync(string name) =>
        await SendAsync(new CreateRoomRecord(name)).ConfigureAwait(true);

    public async Task JoinRoomAsync(string name) =>
        await SendAsync(new JoinRoomRecord(name)).ConfigureAwait(true);

    public async Task LeaveRoomAsync()
    {
        if (State != PlayerSessionState.Seated)
        {
            return;
        }

        await SendAsync(new LeaveRoomRecord()).ConfigureAwait(true);
        ReturnToLobby("left");
    }

    public async Task PlayCellAsync(int cell)
    {
        if (State != PlayerSessionState.Seated || IsSpectator)
        {
            return;
        }

        await SendAsync(new MoveRequestRecord(cell)).ConfigureAwait(true);
    }

    public async Task SendRematchOfferAsync()
    {
        if (State != PlayerSessionState.Seated || IsSpectator)
        {
            return;
        }

        await SendAsync(new RematchOfferRecord()).ConfigureAwait(true);
    }

    private void AttachTransport(IClientTransport transport)
    {
        DetachTransport();
        _transport = transport;
        _transport.MessageReceived += OnLineReceived;
        _transport.Disconnected += OnDisconnected;
    }

    private void DetachTransport()
    {
        if (_transport is not null)
        {
            _transport.MessageReceived -= OnLineReceived;
            _transport.Disconnected -= OnDisconnected;
        }
    }

    private void OnLineReceived(string line)
    {
        switch (GameJson.TryParse(line))
        {
            case RoomListRecord list:
                LatestRooms = list.Rooms;
                RoomsUpdated?.Invoke(list.Rooms);
                break;

            case JoinedRecord joined:
                CurrentRoomName = joined.Room;
                MyMark = joined.Mark;
                CurrentState = joined.State;
                State = PlayerSessionState.Seated;
                Seated?.Invoke(joined);
                if (joined.Restored)
                {
                    LogReceived?.Invoke($"Reconnected to '{joined.Room}'.");
                }

                break;

            case GameStateRecord state:
                if (State == PlayerSessionState.Seated && state.Room == CurrentRoomName)
                {
                    CurrentState = state;
                    StateReceived?.Invoke(state);
                }

                break;

            case LeftRecord left:
                ReturnToLobby(left.Reason);
                break;

            case ErrorRecord error:
                ErrorReceived?.Invoke(error.Message);
                break;

            case null:
                LogReceived?.Invoke(line);
                break;
        }
    }

    private void OnDisconnected()
    {
        if (State == PlayerSessionState.Seated && CurrentRoomName is not null)
        {
            EnterReconnecting();
        }
        else if (State != PlayerSessionState.Reconnecting)
        {
            State = PlayerSessionState.Disconnected;
            ErrorReceived?.Invoke("Connection lost.");
        }
    }

    private void EnterReconnecting()
    {
        State = PlayerSessionState.Reconnecting;
        ReconnectingStarted?.Invoke();
        LogReceived?.Invoke("Connection lost - rejoining...");
        _ = ReconnectLoopAsync(_disposal.Token);
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + _reconnectBudget;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                IClientTransport transport = await _connectFactory().ConfigureAwait(false);
                AttachTransport(transport);
                await transport.SendLineAsync(GameJson.Serialize(new JoinRoomRecord(CurrentRoomName!))).ConfigureAwait(false);
                return; // the joined acknowledgement completes the transition
            }
            catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
            {
                try
                {
                    await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        ReturnToLobby("reconnectFailed");
        ErrorReceived?.Invoke("Could not rejoin the room in time.");
    }

    private void ReturnToLobby(string reason)
    {
        State = PlayerSessionState.Lobby;
        CurrentRoomName = null;
        MyMark = null;
        CurrentState = null;
        ReturnedToLobby?.Invoke(reason);
    }

    private async Task SendAsync(GameEnvelope message)
    {
        IClientTransport? transport = _transport;
        if (transport is null)
        {
            throw new InvalidOperationException("The session is not connected.");
        }

        try
        {
            await transport.SendLineAsync(GameJson.Serialize(message)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
        {
            // Broken pipe: the Disconnected event drives recovery.
        }
    }

    public void Dispose()
    {
        _disposal.Cancel();
        DetachTransport();
        (_transport as IDisposable)?.Dispose();
        _transport = null;
    }
}
```

Create the enum alongside in the same file? No — separate concerns: add `public enum PlayerSessionState` at the bottom of `Client-Server-App/Game/Transports.cs` (it describes transport-level session phases shared by UI code):

```csharp
namespace Client_Server_App.Game;

/// <summary>Lifecycle phases of a client-side <see cref="PlayerSession"/>.</summary>
public enum PlayerSessionState
{
    Connecting,
    Lobby,
    Seated,
    Reconnecting,
    Disconnected,
}
```

(Append this type to the existing `Transports.cs` from Task 3.)

- [ ] **Step 2: Add the tests**

Create `tests/Client-Server-App.Tests/Game/PlayerSessionTests.cs`:

```csharp
using System.Diagnostics;
using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class PlayerSessionTests
{
    private static readonly TimeSpan TinyBudget = TimeSpan.FromMilliseconds(300);

    private sealed class ScriptedConnections
    {
        private int _attempts;

        public int FailuresFirst { get; set; }
        public List<FakeClientTransport> Created { get; } = [];
        public Func<Task<IClientTransport>> Factory => ConnectAsync;

        private async Task<IClientTransport> ConnectAsync()
        {
            int attempt = _attempts++;
            if (attempt < FailuresFirst)
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
        Assert.Equal(Snapshot("friday"), session.CurrentState);
    }

    [Fact]
    public void SpectatorSeating_IsFlagged()
    {
        ScriptedConnections connections = new();
        using PlayerSession session = new(connections.Factory);
        session.ConnectAsync().GetAwaiter().GetResult();
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
        ScriptedConnections connections = new() { FailuresFirst = 1 };
        using PlayerSession session = new(connections.Factory, TinyBudget);
        await session.ConnectAsync();
        FakeClientTransport original = connections.Created.Single();
        original.ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", false,
            new GameStateRecord(["X", "", "", "", "", "", "", "", ""], "O", "inProgress", null, null, 1, "friday"))));

        bool reconnectingRaised = false;
        session.ReconnectingStarted += () => reconnectingRaised = true;
        original.SimulateDisconnect();

        Stopwatch clock = Stopwatch.StartNew();
        while (session.State != PlayerSessionState.Reconnecting && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        // Wait for the retry loop to succeed on the scripted replacement transport.
        while ((session.State == PlayerSessionState.Reconnecting || connections.Created.Count < 2)
               && clock.Elapsed < TimeSpan.FromSeconds(5))
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
        ScriptedConnections connections = new() { FailuresFirst = int.MaxValue };
        using PlayerSession session = new(connections.Factory, TinyBudget);
        await session.ConnectAsync();
        connections.Created.Single().ReceiveLine(GameJson.Serialize(new JoinedRecord("friday", "O", false, Snapshot("friday"))));
        string? error = null;
        session.ErrorReceived += e => error = e;

        connections.Created.Single().SimulateDisconnect();

        Stopwatch clock = Stopwatch.StartNew();
        while (session.State != PlayerSessionState.Lobby && clock.Elapsed < TimeSpan.FromSeconds(5))
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
        using PlayerSession session = new(connections.Factory, TinyBudget);
        await session.ConnectAsync();
        string? error = null;
        session.ErrorReceived += e => error = e;

        connections.Created.Single().SimulateDisconnect();

        Assert.Equal(PlayerSessionState.Disconnected, session.State);
        Assert.Equal("Connection lost.", error);
    }
}
```

Note the deliberate distinction in `UnexpectedDrop_WhileSeated_ReconnectsAndRestores`: after the replacement transport sends `joinRoom`, the session stays in `Reconnecting` until the server's `joined` arrives — the test asserts both phases explicitly.

- [ ] **Step 3: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~PlayerSessionTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 4: Commit**

```bash
git add Client-Server-App/Game/PlayerSession.cs Client-Server-App/Game/Transports.cs tests/Client-Server-App.Tests/Game/PlayerSessionTests.cs
git commit -m "feat: add player session with reconnect-to-seat"
```

---

### Task 6: Windows — `LobbyWindow`, `GameWindow` rework, connect-path wiring, deletions

**Files:**
- Create: `Client-Server-App/LobbyWindow.xaml`, `Client-Server-App/LobbyWindow.xaml.cs`
- Rewrite: `Client-Server-App/GameWindow.xaml`, `Client-Server-App/GameWindow.xaml.cs`
- Modify: `Client-Server-App/ConnectionWindow.xaml.cs` (connect path)
- Delete: `Client-Server-App/Game/TicTacToeClientService.cs`, `Client-Server-App/Game/GameRoles.cs`, `tests/Client-Server-App.Tests/Game/TicTacToeClientServiceTests.cs`

**Interfaces:**
- Consumes: `PlayerSession` (Task 5), `RoomInfoRecord.Label` (Task 1).
- Produces: `internal LobbyWindow(PlayerSession session)`; `internal GameWindow(PlayerSession session)`.

- [ ] **Step 1: Create `LobbyWindow`**

Create `Client-Server-App/LobbyWindow.xaml`:

```xml
<Window x:Class="Client_Server_App.LobbyWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="Lobby" Height="420" Width="480"
        ResizeMode="NoResize" WindowStartupLocation="CenterOwner">
	<Grid Margin="12">
		<Grid.RowDefinitions>
			<RowDefinition Height="Auto" />
			<RowDefinition Height="*" />
			<RowDefinition Height="110" />
		</Grid.RowDefinitions>

		<StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
			<TextBlock Text="Room name:" VerticalAlignment="Center" Margin="0,0,6,0" />
			<TextBox x:Name="RoomNameBox" Width="180" VerticalAlignment="Center" />
			<Button x:Name="CreateButton" Content="Create room" Width="100" Margin="8,0,0,0" Click="CreateButton_Click" />
		</StackPanel>

		<ListBox x:Name="RoomsList" Grid.Row="1">
			<ListBox.ItemTemplate>
				<DataTemplate>
					<DockPanel Margin="2">
						<Button DockPanel.Dock="Right"
								Content="Join"
								Width="70"
								Click="JoinButton_Click" />
						<StackPanel Orientation="Horizontal">
							<TextBlock Text="{Binding Name}" FontWeight="Bold" Margin="0,0,10,0" />
							<TextBlock Text="{Binding Label}" Foreground="Gray" />
						</StackPanel>
					</DockPanel>
				</DataTemplate>
			</ListBox.ItemTemplate>
		</ListBox>

		<TextBox x:Name="OutputTextBox"
				 Grid.Row="2"
				 Margin="0,8,0,0"
				 IsReadOnly="True"
				 FontFamily="Consolas"
				 TextWrapping="Wrap"
				 VerticalScrollBarVisibility="Auto" />
	</Grid>
</Window>
```

Create `Client-Server-App/LobbyWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>Room browser: lists rooms pushed by the referee, creates or joins one.</summary>
public partial class LobbyWindow : Window
{
    private readonly PlayerSession _session;
    private GameWindow? _gameWindow;

    internal LobbyWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        session.RoomsUpdated += rooms => Dispatcher.BeginInvoke(() => RoomsList.ItemsSource = rooms.ToList());
        session.Seated += joined => Dispatcher.BeginInvoke(OnSeated);
        session.ReturnedToLobby += reason => Dispatcher.BeginInvoke(ShowFromGame);
        session.ErrorReceived += message => Dispatcher.BeginInvoke(() => AppendNotice(message));
        session.LogReceived += message => Dispatcher.BeginInvoke(() => AppendNotice(message));
        RoomsList.ItemsSource = session.LatestRooms.ToList();
    }

    private void OnSeated()
    {
        if (_gameWindow is not null)
        {
            return; // rematch re-seating: the existing board window already refreshed itself
        }

        _gameWindow = new GameWindow(_session);
        _gameWindow.Owner = Owner;
        _gameWindow.Closed += (_, _) =>
        {
            _gameWindow = null;
            Show();
        };
        _gameWindow.Show();
        Hide();
    }

    private void ShowFromGame()
    {
        if (!IsVisible && _gameWindow is null)
        {
            Show();
        }

        RoomsList.ItemsSource = _session.LatestRooms.ToList();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string name = RoomNameBox.Text.Trim();
        if (name.Length == 0)
        {
            AppendNotice("Enter a room name first.");
            return;
        }

        try
        {
            await _session.CreateRoomAsync(name);
            RoomNameBox.Text = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    private async void JoinButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not RoomInfoRecord room)
        {
            return;
        }

        try
        {
            await _session.JoinRoomAsync(room.Name);
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    private void AppendNotice(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    protected override void OnClosed(EventArgs e)
    {
        _session.Dispose();
        base.OnClosed(e);
    }
}
```

(`rooms.ToList()` needs `System.Linq` — provided by implicit usings. The window owns the session's lifetime: closing it disposes the session, which disposes the current transport.)

- [ ] **Step 2: Rework `GameWindow` for sessions**

Replace the contents of `Client-Server-App/GameWindow.xaml`:

```xml
<Window x:Class="Client_Server_App.GameWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="Tic-Tac-Toe" Height="520" Width="420"
        ResizeMode="NoResize" WindowStartupLocation="CenterOwner">
	<Grid Margin="12">
		<Grid.RowDefinitions>
			<RowDefinition Height="Auto" />
			<RowDefinition Height="*" />
			<RowDefinition Height="Auto" />
			<RowDefinition Height="110" />
		</Grid.RowDefinitions>

		<TextBlock x:Name="StatusText"
				   Grid.Row="0"
				   FontSize="16"
				   FontWeight="Bold"
				   TextAlignment="Center"
				   Margin="0,0,0,8"
				   Text="Joining..." />

		<UniformGrid x:Name="BoardGrid"
					 Grid.Row="1"
					 Rows="3"
					 Columns="3" />

		<StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,8">
			<Button x:Name="RematchButton"
					Content="Offer Rematch"
					Width="130"
					Height="30"
					Margin="0,0,10,0"
					IsEnabled="False"
					Click="RematchButton_Click" />
			<Button x:Name="LeaveButton"
					Content="Leave"
					Width="90"
					Height="30"
					Click="LeaveButton_Click" />
		</StackPanel>

		<TextBox x:Name="OutputTextBox"
				 Grid.Row="3"
				 Margin="0,8,0,0"
				 IsReadOnly="True"
				 FontFamily="Consolas"
				 TextWrapping="Wrap"
				 VerticalScrollBarVisibility="Auto" />
	</Grid>
</Window>
```

Replace the contents of `Client-Server-App/GameWindow.xaml.cs`:

```csharp
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Tic-tac-toe board driven by a <see cref="PlayerSession"/>. Works for players
/// and spectators; handles the reconnecting banner and the Leave flow.
/// </summary>
public partial class GameWindow : Window
{
    private readonly Button[] _cells = new Button[9];
    private readonly PlayerSession _session;
    private readonly Brush _defaultCellBackground = Brushes.White;
    private GameStateRecord? _renderedState;
    private bool _rematchOfferedLocally;

    internal GameWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        CreateCells();
        session.StateReceived += OnStateReceived;
        session.Seated += OnSeatedInternal;
        session.ReturnedToLobby += OnReturnedToLobby;
        session.ErrorReceived += OnLogMessage;
        session.LogReceived += OnLogMessage;
        session.ReconnectingStarted += OnReconnectingStarted;
    }

    private void OnStateReceived(GameStateRecord state) =>
        Dispatcher.BeginInvoke(() => Render(state));

    private void OnSeatedInternal(JoinedRecord joined) =>
        Dispatcher.BeginInvoke(() => OnSeated(joined));

    private void OnReturnedToLobby(string reason) =>
        Dispatcher.BeginInvoke(Close);

    private void OnLogMessage(string message) =>
        Dispatcher.BeginInvoke(() => AppendLog(message));

    private void OnReconnectingStarted() =>
        Dispatcher.BeginInvoke(ShowReconnectBanner);

    private void CreateCells()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            Button cell = new()
            {
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Background = _defaultCellBackground,
                IsEnabled = false,
                Tag = i,
            };
            cell.Click += CellButton_Click;
            _cells[i] = cell;
            BoardGrid.Children.Add(cell);
        }
    }

    private void OnSeated(JoinedRecord joined)
    {
        Title = $"Tic-Tac-Toe — {joined.Room}" + (joined.Mark is null ? " (spectator)" : $" ({joined.Mark})");

        if (_renderedState is null || joined.State.Round >= _renderedState.Round)
        {
            if (_renderedState is { } previous && joined.State.Round > previous.Round)
            {
                _rematchOfferedLocally = false;
            }

            _renderedState = joined.State;
        }

        if (joined.Restored)
        {
            AppendLog("Reconnected — your seat was restored.");
        }

        Render(_renderedState);
    }

    private void ShowReconnectBanner()
    {
        StatusText.Text = "Connection lost — rejoining...";
        foreach (Button cell in _cells)
        {
            cell.IsEnabled = false;
        }

        RematchButton.IsEnabled = false;
    }

    private async void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (_renderedState is not { Status: "inProgress" } || _session.IsSpectator)
        {
            return;
        }

        if (_session.MyMark is not { } myMark || _renderedState.Turn != myMark)
        {
            return;
        }

        int cell = (int)((Button)sender).Tag!;
        await SafeCallAsync(() => _session.PlayCellAsync(cell), "Move failed");
    }

    private async void RematchButton_Click(object sender, RoutedEventArgs e)
    {
        _rematchOfferedLocally = true;
        RefreshRematchButton();
        await SafeCallAsync(() => _session.SendRematchOfferAsync(), "Rematch failed");
    }

    private async void LeaveButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveButton.IsEnabled = false;
        await SafeCallAsync(() => _session.LeaveRoomAsync(), "Leave failed");
        // ReturnedToLobby closes this window.
    }

    private async Task SafeCallAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or SocketException)
        {
            AppendLog($"{failurePrefix}: {ex.Message}");
        }
    }

    private void Render(GameStateRecord state)
    {
        if (_renderedState is { } previous && state.Round > previous.Round)
        {
            _rematchOfferedLocally = false;
        }

        _renderedState = state;
        string? myMark = _session.MyMark;
        bool spectator = myMark is null;
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && !spectator && state.Turn == myMark;

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i].Content = state.Board[i] == "" ? "" : state.Board[i];
            _cells[i].IsEnabled = myTurn && state.Board[i] == "";
            _cells[i].Background = _defaultCellBackground;
        }

        if (state.WinningLine is not null)
        {
            foreach (int cell in state.WinningLine)
            {
                _cells[cell].Background = Brushes.LightGoldenrodYellow;
            }
        }

        StatusText.Text = (spectator ? "[Spectating] " : string.Empty) + state.Status switch
        {
            "won" when state.WinnerReason == "forfeit" =>
                $"{state.Winner} wins by forfeit.",
            "won" when state.Winner == myMark => "You win!",
            "won" when spectator => $"{state.Winner} wins!",
            "won" => "You lose.",
            "draw" => "It's a draw.",
            _ when myTurn => $"Your move ({myMark}).",
            _ when spectator => $"{state.Turn}'s move.",
            _ => "Opponent's move.",
        };

        RefreshRematchButton(inProgress);
    }

    private void RefreshRematchButton(bool inProgress = false)
    {
        bool gameOver = _renderedState is not null && !inProgress;
        bool player = !_session.IsSpectator;
        RematchButton.IsEnabled = gameOver && player && !_rematchOfferedLocally;
        RematchButton.Content = _rematchOfferedLocally ? "Rematch offered..." : "Offer Rematch";
    }

    private void AppendLog(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    protected override void OnClosed(EventArgs e)
    {
        _session.StateReceived -= OnStateReceived;
        _session.Seated -= OnSeatedInternal;
        _session.ReturnedToLobby -= OnReturnedToLobby;
        _session.ErrorReceived -= OnLogMessage;
        _session.LogReceived -= OnLogMessage;
        _session.ReconnectingStarted -= OnReconnectingStarted;
        base.OnClosed(e);
    }
}
```

(The session dies with the lobby window anyway, but clean detachment keeps double-rendering impossible while both windows coexist.)

- [ ] **Step 3: Rewire the connect path in `ConnectionWindow`**

In `Client-Server-App/ConnectionWindow.xaml.cs` replace `ConnectButton_Click` with:

```csharp
    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        string host = AddressTextBox.Text.Trim();
        if (host.Length == 0)
        {
            AppendLog("Enter an IP address.");
            return;
        }

        if (!TryParsePort(PortTextBox.Text, out int port))
        {
            AppendLog($"'{PortTextBox.Text}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        _client?.Dispose();
        ClientTcp client = new();
        _client = client;
        ConnectButton.IsEnabled = false;
        try
        {
            await client.ConnectAsync(host, port);
            AppendLog($"Connected to {host}:{port}.");

            // First factory call reuses the already-connected socket; later calls
            // (reconnects) open fresh ones.
            ClientTcp? initialTransport = client;
            async Task<IClientTransport> ConnectFactory()
            {
                ClientTcp? transport = Interlocked.Exchange(ref initialTransport, null);
                if (transport is not null)
                {
                    return transport;
                }

                ClientTcp fresh = new();
                await fresh.ConnectAsync(host, port);
                return fresh;
            }

            PlayerSession session = new(ConnectFactory);
            await session.ConnectAsync();

            OpenLobbyWindow(
                () => new LobbyWindow(session),
                onClose: () =>
                {
                    session.Dispose();
                    if (ReferenceEquals(_client, client))
                    {
                        _client = null;
                    }
                });
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            AppendLog($"Connection failed: {ex.Message}");
            client.Dispose();
            if (ReferenceEquals(_client, client))
            {
                _client = null;
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void OpenLobbyWindow(Func<LobbyWindow> createWindow, Action onClose)
    {
        LobbyWindow window = createWindow();
        window.Owner = this;
        window.Closed += (_, _) => onClose();
        window.Show();
    }
```

Delete obsolete artifacts:

```bash
git rm Client-Server-App/Game/TicTacToeClientService.cs Client-Server-App/Game/GameRoles.cs tests/Client-Server-App.Tests/Game/TicTacToeClientServiceTests.cs
```

Also remove the `using System.Globalization;` line only if it became unused (it is still needed by `TryParsePort`).

- [ ] **Step 4: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~TicTacToeTests|FullyQualifiedName~GameJsonTests|FullyQualifiedName~RoomTests|FullyQualifiedName~LobbyServiceTests|FullyQualifiedName~PlayerSessionTests|FullyQualifiedName~ServerTcpConnectionEventsTests"
dotnet build Client-Server-App.slnx -c Debug
dotnet build Client-Server-App.slnx -c Release
```

Manual smoke (optional, interactive): run three instances — one Create Host (referee window appears), two Connect; create a room in instance two, join it in instance three, play; kill the network of one player (close its game window forcibly via task manager is NOT equivalent — instead stop the server) and observe notices.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: lobby and game windows for room-based play"
```

---

### Task 7: Rooms end-to-end test + README + final matrix

**Files:**
- Create: `tests/Client-Server-App.Tests/Integration/TicTacToeRoomsEndToEndTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: real `ServerTcp`, real `ClientTcp`, `LobbyService` (injectable grace), `PlayerSession` (factory seam + injectable budget).
- Produces: full-stack regression proof — lobby seating, auto-start, spectating, client win, rematch mark swap, drop→reconnect seat restoration (grace cancelled), forfeit on exhausted grace.

- [ ] **Step 1: Write the E2E suite**

Create `tests/Client-Server-App.Tests/Integration/TicTacToeRoomsEndToEndTests.cs`:

```csharp
using System.IO;
using Client_Server_App;
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class TicTacToeRoomsEndToEndTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);

    private sealed class TestServer : IDisposable
    {
        public ServerTcp Transport { get; } = new(0);
        public LobbyService Lobby { get; }

        public TestServer(TimeSpan? grace = null)
        {
            Transport.Start();
            Lobby = new LobbyService(Transport, grace ?? Grace);
            Lobby.Start();
        }

        public int Port => Transport.Port;

        public void Dispose()
        {
            Lobby.Dispose();
            Transport.Dispose();
        }
    }

    private static PlayerSession Connect(TestServer server)
    {
        PlayerSession session = new(async () =>
        {
            ClientTcp transport = new();
            await transport.ConnectAsync("127.0.0.1", server.Port);
            return transport;
        });
        session.ConnectAsync().GetAwaiter().GetResult();
        return session;
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within 10s: {what}");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Moves with <paramref name="mover"/> and waits until both sessions observe the cell.</summary>
    private static async Task MoveAndAwait(PlayerSession a, PlayerSession b, PlayerSession mover, int cell)
    {
        await mover.PlayCellAsync(cell);
        await WaitForAsync(() => a.CurrentState!.Board[cell] != "", $"{a.MyMark} saw {cell}");
        await WaitForAsync(() => b.CurrentState!.Board[cell] != "", $"{b.MyMark} saw {cell}");
    }

    [Fact]
    public async Task TwoPlayersAndSpectator_FullGame_RematchSwapsMarks()
    {
        using TestServer server = new(TimeSpan.FromSeconds(10));
        using PlayerSession host = Connect(server);
        using PlayerSession guest = Connect(server);
        using PlayerSession spectator = Connect(server);

        await host.CreateRoomAsync("duel");
        await WaitForAsync(() => host.CurrentState is not null && host.MyMark == "X", "host seated as X");

        await guest.JoinRoomAsync("duel");
        await WaitForAsync(() => guest.CurrentState is not null && guest.MyMark == "O", "guest seated as O");

        await spectator.JoinRoomAsync("duel");
        await WaitForAsync(() => spectator.CurrentState is not null, "spectator watching");
        Assert.True(spectator.IsSpectator);

        // X:0, O:3, X:1, O:4, X:6, O:5 -> O wins [3,4,5].
        await MoveAndAwait(host, guest, host, 0);
        await MoveAndAwait(host, guest, guest, 3);
        await MoveAndAwait(host, guest, host, 1);
        await MoveAndAwait(host, guest, guest, 4);
        await MoveAndAwait(host, guest, host, 6);
        await MoveAndAwait(host, guest, guest, 5);

        await WaitForAsync(() => host.CurrentState!.Status == "won", "host sees result");
        await WaitForAsync(() => guest.CurrentState!.Status == "won", "guest sees result");
        await WaitForAsync(() => spectator.CurrentState!.Status == "won", "spectator sees result");
        Assert.Equal("O", guest.CurrentState!.Winner);
        Assert.Equal(new[] { 3, 4, 5 }, guest.CurrentState!.WinningLine!.ToArray());

        // Rematch: both vote (order-independent); marks swap; X starts round 2.
        await guest.SendRematchOfferAsync();
        await host.SendRematchOfferAsync();

        await WaitForAsync(() => host.CurrentState!.Round == 2, "round 2 on host");
        await WaitForAsync(() => guest.CurrentState!.Round == 2, "round 2 on guest");
        await WaitForAsync(() => spectator.CurrentState!.Round == 2, "round 2 on spectator");
        await WaitForAsync(() => host.MyMark == "O" && guest.MyMark == "X", "marks swapped");
        Assert.Equal("X", host.CurrentState!.Turn);
        Assert.All(host.CurrentState!.Board, cell => Assert.Equal("", cell));
    }

    [Fact]
    public async Task Drop_ReconnectRestoresSeat_AndCancelsGrace()
    {
        using TestServer server = new(TimeSpan.FromSeconds(3));
        List<ClientTcp> transports = [];
        using PlayerSession dropper = new(async () =>
        {
            ClientTcp transport = new();
            await transport.ConnectAsync("127.0.0.1", server.Port);
            transports.Add(transport);
            return transport;
        });
        try
        {
            await dropper.ConnectAsync();
            using PlayerSession opponent = Connect(server);

            await dropper.CreateRoomAsync("resume");
            await WaitForAsync(() => dropper.CurrentState is not null, "dropper seated");

            await opponent.JoinRoomAsync("resume");
            await WaitForAsync(() => opponent.CurrentState is not null, "opponent seated");

            await dropper.PlayCellAsync(4); // X center
            await WaitForAsync(() => opponent.CurrentState!.Board[4] == "X", "opponent sees move");

            transports[^1].Dispose(); // kill the pipe

            await WaitForAsync(() =>
                dropper.State == PlayerSessionState.Seated
                && dropper.MyMark == "O"
                && dropper.CurrentState!.Board[4] == "X", "seat restored within grace");

            // Grace was cancelled: the game is still alive well past the deadline.
            await Task.Delay(Grace + TimeSpan.FromMilliseconds(500));
            Assert.Equal("inProgress", opponent.CurrentState!.Status);
            Assert.Null(opponent.CurrentState!.WinnerReason);
        }
        finally
        {
            dropper.Dispose();
        }
    }

    [Fact]
    public async Task Drop_NeverReconnects_GraceForfeitsToOpponent()
    {
        using TestServer server = new(Grace);
        bool allowConnections = true;
        ClientTcp? live = null;
        using PlayerSession quitter = new(async () =>
        {
            ClientTcp? transport = Interlocked.Exchange(ref live, null);
            if (transport is null || !allowConnections)
            {
                await Task.Yield();
                throw new IOException("no network");
            }

            return transport;
        }, TimeSpan.FromMilliseconds(500));
        using PlayerSession survivor = Connect(server);

        await quitter.ConnectAsync(); // consumes the single live transport
        await quitter.CreateRoomAsync("gone");
        await WaitForAsync(() => quitter.CurrentState is not null, "quitter seated");

        await survivor.JoinRoomAsync("gone");
        await WaitForAsync(() => survivor.CurrentState is not null, "survivor seated");

        await quitter.PlayCellAsync(0); // one move, then the pipe dies forever
        await WaitForAsync(() => survivor.CurrentState!.Board[0] == "X", "move visible");

        allowConnections = false;
        Interlocked.Exchange(ref live, null)?.Dispose();

        await WaitForAsync(() =>
            survivor.CurrentState!.Status == "won"
            && survivor.CurrentState!.WinnerReason == "forfeit", "forfeit awarded");

        // The quitter's reconnect attempts all fail; it falls back to the lobby.
        await WaitForAsync(() => quitter.State == PlayerSessionState.Lobby, "quitter back in lobby");
    }
}
```

Timing notes for the reviewer: the restore test uses a 3-second grace so loopback reconnection lands comfortably inside it, then proves cancellation by surviving past the original deadline; the forfeit test flips `allowConnections` before killing the pipe so every reconnect attempt throws immediately, letting the 500 ms budget expire long before the 300 ms... correction — BEFORE the server's grace expires is NOT required; what matters is the server-side 300 ms forfeit firing while the client budget (500 ms) may still be retrying. Both waits are poll-based with generous 10 s ceilings, so ordering between "client gives up" and "server forfeits" does not affect assertions.

- [ ] **Step 2: Update the README**

Replace the two feature bullets under the intro paragraph with:

```markdown
- **Connect** – join as a player: browse rooms, create one, or join as player/spectator.
- **Host** – run the referee: a neutral lobby that owns rooms and never plays.
```

and replace the whole `## Playing` section with:

```markdown
## Playing

Start three instances. Instance one clicks **Create Host** (a referee window
appears). Instances two and three click **Connect**, then create or join a room
in the lobby. The first two players seat as X and O and the game starts
automatically; further joiners watch as spectators. If a player's connection
drops, they rejoin their seat automatically within a 10-second grace window —
otherwise the opponent wins by forfeit. Closing the game window returns you to
the lobby; **Leave** exits deliberately and forfeits an ongoing game.
```

- [ ] **Step 3: Full validation matrix**

```bash
dotnet test Client-Server-App.slnx
dotnet build Client-Server-App.slnx -c Debug
dotnet build Client-Server-App.slnx -c Release
```

All tests pass; both configurations build with 0 warnings / 0 errors.

- [ ] **Step 4: Commit**

```bash
git add tests/Client-Server-App.Tests/Integration/TicTacToeRoomsEndToEndTests.cs README.md
git commit -m "test: add rooms end-to-end coverage and update docs"
```

---

## Self-Review Record

**Spec coverage map**

| Spec requirement | Task |
| --- | --- |
| Pure referee instance (lobby/status window, cannot play) | T3 (`ServerWindow` + wiring) |
| Named rooms, unique case-insensitively, 1–30 chars | T3 (`HandleCreate`) + T4 tests |
| Auto-start; first joiner X, second O | T2 (`Seat`) + T4 + T7 |
| Spectators receive states; moves ignored | T2 (`HandleMove` guard) + T4 + T7 |
| `roomList` pushed on membership change | T3 (`PushRoomList`) driven by T2 (`MembershipChanged`) |
| Disconnect → 10 s grace → forfeit with `winnerReason:"forfeit"` | T2 + T4 + T7 |
| Solo-player drop closes room immediately | T2 + T2 test |
| Auto-reconnect into reserved seat, snapshot restored | T5 loop + T2 `restored:true` + T7 |
| Reconnect budget exhaustion → lobby + error | T5 + T7 |
| Explicit Leave = immediate forfeit | T2 + T2 test |
| Rematch two-vote flow, mark swap, round increment, clients learn new marks | T2 (`StartNextRound` fresh `joined`) + tests + T7 |
| Chat routed to sender's room only; unseated chat ignored | T3 routing + T2 + T4 test |
| Malformed lines → chat fallback | unchanged `GameJson.TryParse` (T1 tests) |
| Server-originated envelopes rejected from clients | T3 (`OnMessageReceived`) |

**Placeholder scan:** none — all code blocks are final copy-pasteable content; drafting corrections were folded in during plan preparation rather than left as instructions.

**Type consistency verified:** `SendToAsync(Guid, string, CancellationToken)` matches `LobbyService.SafeSendAsync`; `RoomInfoRecord.Label` consumed by `LobbyWindow` binding; `JoinedRecord(Room, Mark, Restored, State)` identical across Room / PlayerSession / GameWindow / tests; `PlayerSessionState` defined once in `Transports.cs` and used identically everywhere; `DeclareForfeit` name matches Room usage; reason strings (`left`, `roomClosed`, `reconnectFailed`) consistent end-to-end; `GetRooms()` returns `IReadOnlyList<RoomInfoRecord>` matching `ServerWindow.RenderRooms`.

**Locking audit:** every `Room` public method acquires `_sync` at most once and performs sends/events outside the lock; `LobbyService.HandleCreate`/`HandleJoin` deliberately seat outside the lobby lock because `Room.Seat` synchronously raises `MembershipChanged` → `PushRoomList` → `GetRooms` (same lock). Preserve these properties when editing.

**Deliberate simplifications (documented, spec-compliant):** a reconnect racing the grace timer resolves either as seat-restore or spectator-join — both leave consistent state; room lists broadcast to everyone including seated members (seated UIs ignore them).
