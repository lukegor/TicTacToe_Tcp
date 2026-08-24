# Tic-Tac-Toe Over TCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two humans play tic-tac-toe over the app's TCP link, with an authoritative game session on the host and a request-only client.

**Architecture:** Session layer over the existing dumb-pipe transports (`ClientTcp`/`ServerTcp`). A pure `TicTacToe` domain type holds rules; JSON envelopes (System.Text.Json polymorphic discrimination) travel the existing newline-delimited lines; the host service validates/applies/broadcasts, the client service requests and renders states. A new `GameWindow` renders either role.

**Tech Stack:** .NET 10 (net10.0-windows), WPF, System.Text.Json (inbox), xUnit for tests. No new runtime NuGet dependencies; test-stack packages managed via Central Package Management.

**Spec:** `docs/superpowers/specs/2026-08-23-tic-tac-toe-design.md`

## Global Constraints

- Target framework: `net10.0-windows` (set centrally in `Directory.Build.props` — do not set per-project).
- `TreatWarningsAsErrors=true`, `Nullable=enable`, `ImplicitUsings=enable`, `EnforceCodeStyleInBuild=true` — every build must finish with **0 warnings, 0 errors**.
- Package versions live **only** in `Directory.Packages.props` (CPM). Projects use versionless `<PackageReference Include="..."/>` or `dotnet add package` (which writes CPM entries automatically).
- File-scoped namespaces, braces on all blocks (enforced by `.editorconfig`).
- No public property may return an array type (CA1819 fires as error under this repo's analyzer config) — use `IReadOnlyList<T>`.
- Catch clauses must filter specific exception types (`catch (Exception ex) when (...)`) — bare `catch` trips CA1031 as error.
- All game types are `internal`; tests see them via `<InternalsVisibleTo Include="Client-Server-App.Tests" />`.
- Namespace root: `Client_Server_App`, game code: `Client_Server_App.Game`.
- Wire format: newline-delimited UTF-8 JSON with `"type"` discriminator, camelCase (JsonSerializerDefaults.Web). Non-envelope lines render as chat text.
- Roles: host is X in round 1; marks swap every rematch round; X always moves first in a round.
- Working directory for all commands: repository root (``).

## File Structure

| File | Responsibility |
| --- | --- |
| `Client-Server-App/Game/TicTacToe.cs` | Pure rules engine: board, turns, win/draw detection. |
| `Client-Server-App/Game/GameEnvelope.cs` | Wire records (`MoveRequestRecord`, `GameStateRecord`, `RematchOfferRecord`) + polymorphism attributes. |
| `Client-Server-App/Game/GameJson.cs` | Cached-options serializer/parser for envelopes. |
| `Client-Server-App/Game/GameRoles.cs` | Round → mark mapping shared by host/client/UI. |
| `Client-Server-App/Game/Transports.cs` | `IServerTransport` / `IClientTransport` abstractions. |
| `Client-Server-App/Game/TicTacToeHostService.cs` | Authority: validates remote moves, owns rounds/rematch voting, broadcasts states. |
| `Client-Server-App/Game/TicTacToeClientService.cs` | Sends move/rematch requests, surfaces parsed states. |
| `Client-Server-App/GameWindow.xaml/.cs` | Board UI for either role. |
| `Client-Server-App/ServerTcp.cs` | Modify: connection events, `Port`, implements `IServerTransport`. |
| `Client-Server-App/ClientTcp.cs` | Modify: implements `IClientTransport`. |
| `Client-Server-App/ConnectionWindow.xaml.cs` | Modify: open `GameWindow` after setup succeeds; lifecycle cleanup. |
| `tests/Client-Server-App.Tests/…` | xUnit project: domain, serialization, service (fake transport), connection-event, and end-to-end tests. |

---

### Task 1: Test project scaffold + `TicTacToe` domain

**Files:**
- Create: `tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj`
- Create: `tests/Client-Server-App.Tests/Game/TicTacToeTests.cs`
- Create: `Client-Server-App/Game/TicTacToe.cs`
- Modify: `Client-Server-App/Client-Server-App.csproj`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `enum Player { X, O }`, `enum GameStatus { InProgress, Won, Draw }` (namespace `Client_Server_App.Game`)
  - `sealed class TicTacToe` with `IReadOnlyList<Player?> Board`, `Player Turn`, `GameStatus Status`, `Player? Winner`, `IReadOnlyList<int>? WinningLine`, `bool TryApplyMove(int cell, Player player)`, `void Reset(Player startingPlayer)`
  - Test project `Client-Server-App.Tests` registered in `Client-Server-App.slnx`.

- [ ] **Step 1: Create the test project and register it**

Create `tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Client-Server-App\Client-Server-App.csproj" />
  </ItemGroup>

</Project>
```

(TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors flow from `Directory.Build.props`. `UseWPF` is required because referenced types derive from/depend on WPF assemblies.)

Add the test-stack package versions to Central Package Management (this fetches newest stable and writes CPM entries):

```bash
dotnet add tests/Client-Server-App.Tests package Microsoft.NET.Test.Sdk
dotnet add tests/Client-Server-App.Tests package xunit
dotnet add tests/Client-Server-App.Tests package xunit.runner.visualstudio
dotnet sln Client-Server-App.slnx add tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj
```

Expose internals to tests — add to `Client-Server-App/Client-Server-App.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Client-Server-App.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Implement the domain**

Create `Client-Server-App/Game/TicTacToe.cs`:

```csharp
namespace Client_Server_App.Game;

internal enum Player
{
    X,
    O,
}

internal enum GameStatus
{
    InProgress,
    Won,
    Draw,
}

/// <summary>
/// Pure tic-tac-toe rules engine: no I/O, no events, no threading.
/// Cells are indexed 0-8, row-major.
/// </summary>
internal sealed class TicTacToe
{
    private static readonly int[][] WinningLines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6],
    ];

    private Player?[] _board = new Player?[9];

    public IReadOnlyList<Player?> Board => _board;
    public Player Turn { get; private set; } = Player.X;
    public GameStatus Status { get; private set; } = GameStatus.InProgress;
    public Player? Winner { get; private set; }
    public IReadOnlyList<int>? WinningLine { get; private set; }

    /// <summary>Applies <paramref name="player"/>'s move if it is legal; otherwise returns false.</summary>
    public bool TryApplyMove(int cell, Player player)
    {
        if (Status != GameStatus.InProgress || player != Turn)
        {
            return false;
        }

        if ((uint)cell >= (uint)_board.Length || _board[cell] is not null)
        {
            return false;
        }

        _board[cell] = player;
        FinishOrAdvance();
        return true;
    }

    public void Reset(Player startingPlayer)
    {
        _board = new Player?[9];
        Turn = startingPlayer;
        Status = GameStatus.InProgress;
        Winner = null;
        WinningLine = null;
    }

    private void FinishOrAdvance()
    {
        foreach (int[] line in WinningLines)
        {
            if (_board[line[0]] is { } mark && _board[line[1]] == mark && _board[line[2]] == mark)
            {
                Status = GameStatus.Won;
                Winner = mark;
                WinningLine = line;
                return;
            }
        }

        if (_board.All(cell => cell is not null))
        {
            Status = GameStatus.Draw;
            return;
        }

        Turn = Turn == Player.X ? Player.O : Player.X;
    }
}
```

- [ ] **Step 3: Add the unit tests**

Create `tests/Client-Server-App.Tests/Game/TicTacToeTests.cs`:

```csharp
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class TicTacToeTests
{
    private readonly TicTacToe _game = new();

    [Fact]
    public void NewGame_StartsWithEmptyBoardAndXTurn()
    {
        Assert.Equal(9, _game.Board.Count);
        Assert.All(_game.Board, cell => Assert.Null(cell));
        Assert.Equal(Player.X, _game.Turn);
        Assert.Equal(GameStatus.InProgress, _game.Status);
        Assert.Null(_game.Winner);
        Assert.Null(_game.WinningLine);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(3, 4, 5)]
    [InlineData(6, 7, 8)]
    [InlineData(0, 3, 6)]
    [InlineData(1, 4, 7)]
    [InlineData(2, 5, 8)]
    [InlineData(0, 4, 8)]
    [InlineData(2, 4, 6)]
    public void ThreeInARow_WinsWithWinningLine(int a, int b, int c)
    {
        // Two filler cells off the winning line keep the alternation legal
        // without letting either side win early.
        int[] fillers = Enumerable.Range(0, 9).Except([a, b, c]).ToArray();

        PlaySequence(
            (a, Player.X),
            (fillers[0], Player.O),
            (b, Player.X),
            (fillers[1], Player.O),
            (c, Player.X));

        Assert.Equal(GameStatus.Won, _game.Status);
        Assert.Equal(Player.X, _game.Winner);
        Assert.Equal(new[] { a, b, c }, _game.WinningLine);
    }

    [Fact]
    public void FullBoardWithoutWinner_Draws()
    {
        // Final board:   X O X
        //                X O O
        //                O X X     (verified: no completed line for either side,
        //                           strict X/O/X/O... alternation throughout)
        (int cell, Player player)[] moves =
        [
            (0, Player.X), (1, Player.O), (2, Player.X),
            (4, Player.O), (3, Player.X), (5, Player.O),
            (7, Player.X), (6, Player.O), (8, Player.X),
        ];

        foreach ((int cell, Player player) in moves)
        {
            Assert.True(_game.TryApplyMove(cell, player));
        }

        Assert.Equal(GameStatus.Draw, _game.Status);
        Assert.Null(_game.Winner);
    }

    [Fact]
    public void TryApplyMove_RejectsWrongTurn()
    {
        Assert.False(_game.TryApplyMove(0, Player.O));
        Assert.Equal(GameStatus.InProgress, _game.Status);
        Assert.Null(_game.Board[0]);
    }

    [Fact]
    public void TryApplyMove_RejectsOccupiedCell()
    {
        Assert.True(_game.TryApplyMove(4, Player.X));
        Assert.False(_game.TryApplyMove(4, Player.O));
        Assert.Equal(Player.X, _game.Board[4]);
    }

    [Fact]
    public void TryApplyMove_RejectsOutOfRangeCell()
    {
        Assert.False(_game.TryApplyMove(-1, Player.X));
        Assert.False(_game.TryApplyMove(9, Player.X));
    }

    [Fact]
    public void TryApplyMove_RejectsMovesAfterGameOver()
    {
        PlaySequence((0, Player.X), (3, Player.O), (1, Player.X), (4, Player.O), (2, Player.X));

        Assert.Equal(GameStatus.Won, _game.Status);
        Assert.False(_game.TryApplyMove(8, Player.X));
    }

    [Fact]
    public void Reset_ClearsBoardAndSetsStarter()
    {
        PlaySequence((0, Player.X), (3, Player.O), (1, Player.X));

        _game.Reset(Player.X);

        Assert.All(_game.Board, cell => Assert.Null(cell));
        Assert.Equal(Player.X, _game.Turn);
        Assert.Equal(GameStatus.InProgress, _game.Status);
        Assert.Null(_game.Winner);
        Assert.Null(_game.WinningLine);
    }

    private void PlaySequence(params (int cell, Player player)[] moves)
    {
        foreach ((int cell, Player player) in moves)
        {
            Assert.True(_game.TryApplyMove(cell, player));
        }
    }
}
```

Note: the win-line theory test intentionally plays filler cells around each asserted line; the `(c - 3 == b ? 5 : 3, Player.X)` expression keeps X's second move out of the winning line for every case. Verify by running the suite — every theory case must pass.

- [ ] **Step 4: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~TicTacToeTests"
dotnet build Client-Server-App.slnx -c Release
```

Both must succeed with 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add Client-Server-App/Game/TicTacToe.cs Client-Server-App/Client-Server-App.csproj tests/
git commit -m "feat: add tic-tac-toe rules engine with unit tests"
```

---

### Task 2: JSON envelopes and serializer

**Files:**
- Create: `Client-Server-App/Game/GameEnvelope.cs`
- Create: `Client-Server-App/Game/GameJson.cs`
- Create: `tests/Client-Server-App.Tests/Game/GameJsonTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces:
  - `abstract record GameEnvelope` with derived `MoveRequestRecord(int Cell)`, `RematchOfferRecord`, `GameStateRecord(IReadOnlyList<string> Board, string Turn, string Status, string? Winner, IReadOnlyList<int>? WinningLine, int Round)`
  - `static class GameJson` with `string Serialize(GameEnvelope message)` and `GameEnvelope? TryParse(string line)` (returns null for non-envelope input)

- [ ] **Step 1: Define the envelopes**

Create `Client-Server-App/Game/GameEnvelope.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Client_Server_App.Game;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MoveRequestRecord), "moveRequest")]
[JsonDerivedType(typeof(RematchOfferRecord), "rematchOffer")]
[JsonDerivedType(typeof(GameStateRecord), "state")]
internal abstract record GameEnvelope;

internal sealed record MoveRequestRecord(int Cell) : GameEnvelope;

internal sealed record RematchOfferRecord : GameEnvelope;

internal sealed record GameStateRecord(
    IReadOnlyList<string> Board,
    string Turn,
    string Status,
    string? Winner,
    IReadOnlyList<int>? WinningLine,
    int Round) : GameEnvelope;
```

(`Status` values on the wire: `"inProgress"`, `"won"`, `"draw"`.)

- [ ] **Step 2: Implement the serializer**

Create `Client-Server-App/Game/GameJson.cs`:

```csharp
using System.Text.Json;

namespace Client_Server_App.Game;

internal static class GameJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(GameEnvelope message) =>
        JsonSerializer.Serialize(message, Options);

    /// <summary>Parses an envelope; returns null when the line is not a known game message (chat fallback).</summary>
    public static GameEnvelope? TryParse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<GameEnvelope>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Add serialization tests**

Create `tests/Client-Server-App.Tests/Game/GameJsonTests.cs`:

```csharp
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class GameJsonTests
{
    [Fact]
    public void Serialize_MoveRequest_UsesCamelCaseWithTypeDiscriminator()
    {
        string json = GameJson.Serialize(new MoveRequestRecord(4));

        Assert.Contains("\"type\":\"moveRequest\"", json);
        Assert.Contains("\"cell\":4", json);
    }

    [Fact]
    public void Serialize_State_IncludesAllFields()
    {
        string json = GameJson.Serialize(new GameStateRecord(
            Board: ["X", "", "O", "", "", "", "", "", ""],
            Turn: "X",
            Status: "inProgress",
            Winner: null,
            WinningLine: null,
            Round: 2));

        Assert.Contains("\"type\":\"state\"", json);
        Assert.Contains("\"board\":", json);
        Assert.Contains("\"turn\":\"X\"", json);
        Assert.Contains("\"status\":\"inProgress\"", json);
        Assert.Contains("\"winner\":null", json);
        Assert.Contains("\"winningLine\":null", json);
        Assert.Contains("\"round\":2", json);
    }

    [Fact]
    public void TryParse_RoundTripsEveryEnvelopeKind()
    {
        GameEnvelope[] originals =
        [
            new MoveRequestRecord(7),
            new RematchOfferRecord(),
            new GameStateRecord(["", "", "", "", "", "", "", "", ""], "X", "inProgress", null, null, 1),
            new GameStateRecord(["X", "X", "X", "", "O", "O", "", "", ""], "O", "won", "X", [0, 1, 2], 3),
        ];

        foreach (GameEnvelope original in originals)
        {
            GameEnvelope parsed = GameJson.TryParse(GameJson.Serialize(original))!;

            Assert.Equal(original.GetType(), parsed.GetType());
            Assert.Equal(original.ToString(), parsed.ToString());
        }
    }

    [Fact]
    public void TryParse_ReturnsNullForChatText()
    {
        Assert.Null(GameJson.TryParse("Hello from client"));
        Assert.Null(GameJson.TryParse("{\"type\":\"unknown\"}"));
        Assert.Null(GameJson.TryParse("{not json"));
    }
}
```

- [ ] **Step 4: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~GameJsonTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 5: Commit**

```bash
git add Client-Server-App/Game/GameEnvelope.cs Client-Server-App/Game/GameJson.cs tests/Client-Server-App.Tests/Game/GameJsonTests.cs
git commit -m "feat: add JSON game envelope protocol"
```

---

### Task 3: Transport interfaces + connection lifetime events

**Files:**
- Create: `Client-Server-App/Game/Transports.cs`
- Modify: `Client-Server-App/ServerTcp.cs`
- Modify: `Client-Server-App/ClientTcp.cs`
- Create: `tests/Client-Server-App.Tests/Integration/ServerTcpConnectionEventsTests.cs`

**Interfaces:**
- Consumes: existing `ServerTcp` / `ClientTcp` members (`MessageReceived`, `BroadcastLineAsync`, `SendLineAsync`, `Disconnected`, receive loops, `RemoveClient`/`RegisterClient`).
- Produces:
  - `interface IServerTransport { event Action<string>? MessageReceived; event Action? ClientConnected; event Action? ClientDisconnected; Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default); }`
  - `interface IClientTransport { event Action<string>? MessageReceived; event Action? Disconnected; Task SendLineAsync(string message, CancellationToken cancellationToken = default); }`
  - `ServerTcp : IServerTransport` with new `int Port` property (valid after `Start()`).
  - `ClientTcp : IClientTransport`.

- [ ] **Step 1: Define the interfaces**

Create `Client-Server-App/Game/Transports.cs`:

```csharp
namespace Client_Server_App.Game;

/// <summary>The host-side view of the server transport.</summary>
internal interface IServerTransport
{
    event Action<string>? MessageReceived;
    event Action? ClientConnected;
    event Action? ClientDisconnected;

    Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>The client-side view of the client transport.</summary>
internal interface IClientTransport
{
    event Action<string>? MessageReceived;
    event Action? Disconnected;

    Task SendLineAsync(string message, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Extend `ServerTcp`**

In `Client-Server-App/ServerTcp.cs`:

Change the class declaration and add the `Port` property plus the two events (keep everything else identical):

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client_Server_App;

internal sealed class ServerTcp : IServerTransport
{
    private readonly ConcurrentDictionary<TcpClient, StreamWriter> _clients = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _broadcastLock = new(initialCount: 1, maxCount: 1);
    private readonly TcpListener _listener;
    private bool _disposed;

    public event Action<string>? MessageReceived;

    /// <summary>Raised (on a worker thread) when any client completes the TCP handshake.</summary>
    public event Action? ClientConnected;

    /// <summary>Raised (on a worker thread) when a client leaves.</summary>
    public event Action? ClientDisconnected;

    /// <summary>The bound port; only meaningful after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ServerTcp(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }
```

Raise the events — in `RegisterClient`, append after the `_ = HandleClientAsync(client);` line:

```csharp
        ClientConnected?.Invoke();
```

and in `RemoveClient`, append at the end of the method:

```csharp
        ClientDisconnected?.Invoke();
```

(The existing `BroadcastLineAsync` and `MessageReceived` already match `IServerTransport`; the interface declaration above is now satisfied.)

- [ ] **Step 3: Implement `IClientTransport` on `ClientTcp`**

In `Client-Server-App/ClientTcp.cs`, change the declaration only — all members already exist:

```csharp
internal sealed class ClientTcp : IClientTransport
```

- [ ] **Step 4: Add the connection-event integration test**

Create `tests/Client-Server-App.Tests/Integration/ServerTcpConnectionEventsTests.cs`:

```csharp
using Client_Server_App;
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class ServerTcpConnectionEventsTests
{
    [Fact]
    public async Task ClientLifecycle_RaisesConnectedThenDisconnected()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += () => connected.TrySetResult();
        server.ClientDisconnected += () => disconnected.TrySetResult();

        server.Start();
        Assert.True(server.Port > 0);

        using ClientTcp client = new();
        await client.ConnectAsync("127.0.0.1", server.Port);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        client.Dispose();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Broadcast_ReachesConnectedClient()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += () => connected.TrySetResult();
        server.Start();

        using ClientTcp client = new();
        TaskCompletionSource<string> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += line => received.TrySetResult(line);

        await client.ConnectAsync("127.0.0.1", server.Port);
        // ClientConnected fires after the writer is registered, so a subsequent
        // broadcast is guaranteed to reach the client.
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await server.BroadcastLineAsync("ping");
        Assert.Equal("ping", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
```

Note: `new ServerTcp(0)` binds an OS-assigned port; `Port` exposes it so tests never fight over fixed ports, and the connected-event handshake removes any registration race from the broadcast assertion.

- [ ] **Step 5: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~ServerTcpConnectionEventsTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 6: Commit**

```bash
git add Client-Server-App/Game/Transports.cs Client-Server-App/ServerTcp.cs Client-Server-App/ClientTcp.cs tests/Client-Server-App.Tests/Integration/
git commit -m "feat: add transport interfaces and connection lifetime events"
```

---

### Task 4: Authoritative host service

**Files:**
- Create: `Client-Server-App/Game/GameRoles.cs`
- Create: `Client-Server-App/Game/TicTacToeHostService.cs`
- Create: `tests/Client-Server-App.Tests/TestDoubles/FakeServerTransport.cs`
- Create: `tests/Client-Server-App.Tests/Game/TicTacToeHostServiceTests.cs`

**Interfaces:**
- Consumes: `TicTacToe` (Task 1), `GameJson`/envelopes (Task 2), `IServerTransport` (Task 3).
- Produces:
  - `static class GameRoles { static string HostMark(int round); static string ClientMark(int round); }` (round 1 → host X / client O; swaps each round)
  - `sealed class TicTacToeHostService`:
    - ctor `TicTacToeHostService(IServerTransport server)`
    - `void Start()` — subscribes and publishes initial state
    - `Task PlayMoveAsync(int cell)`
    - `Task RequestRematchAsync()`
    - `int Round`, `GameStateRecord? CurrentState`
    - events: `Action<GameStateRecord>? StateChanged`, `Action<string>? LogReceived`, `Action? RematchRequested`, `Action? OpponentDisconnected`

- [ ] **Step 1: Implement `GameRoles`**

Create `Client-Server-App/Game/GameRoles.cs`:

```csharp
namespace Client_Server_App.Game;

/// <summary>Marks swap between rounds; X always starts a round.</summary>
internal static class GameRoles
{
    public static string HostMark(int round) => round % 2 == 1 ? "X" : "O";

    public static string ClientMark(int round) => round % 2 == 1 ? "O" : "X";
}
```

- [ ] **Step 2: Implement `TicTacToeHostService`**

Create `Client-Server-App/Game/TicTacToeHostService.cs`:

```csharp
using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Authoritative tic-tac-toe session for the host. Validates remote move requests,
/// owns round/rematch progression, and broadcasts state after every change.
/// </summary>
internal sealed class TicTacToeHostService
{
    private readonly IServerTransport _server;
    private readonly TicTacToe _game = new();
    private readonly object _sync = new();
    private int _round = 1;
    private bool _hostWantsRematch;
    private bool _clientWantsRematch;

    public event Action<GameStateRecord>? StateChanged;
    public event Action<string>? LogReceived;
    public event Action? RematchRequested;
    public event Action? OpponentDisconnected;

    public int Round => _round;
    public GameStateRecord? CurrentState { get; private set; }

    public TicTacToeHostService(IServerTransport server) => _server = server;

    public void Start()
    {
        _server.MessageReceived += OnLineReceived;
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _ = PublishStateAsync();
    }

    public async Task PlayMoveAsync(int cell)
    {
        lock (_sync)
        {
            _ = _game.TryApplyMove(cell, ParseMark(GameRoles.HostMark(_round)));
        }

        await PublishStateAsync().ConfigureAwait(false);
    }

    public async Task RequestRematchAsync()
    {
        bool start;
        lock (_sync)
        {
            start = _clientWantsRematch;
            if (!start)
            {
                _hostWantsRematch = true;
            }
        }

        if (start)
        {
            await StartNextRoundAsync().ConfigureAwait(false);
            return;
        }

        await SendAsync(new RematchOfferRecord()).ConfigureAwait(false);
    }

    private void OnClientConnected() => _ = PublishStateAsync();

    private void OnClientDisconnected() => OpponentDisconnected?.Invoke();

    private void OnLineReceived(string line)
    {
        switch (GameJson.TryParse(line))
        {
            case MoveRequestRecord request:
                _ = ApplyRemoteMoveAsync(request.Cell);
                break;

            case RematchOfferRecord:
                HandleRematchOffer();
                break;

            case GameStateRecord:
                // Hosts never consume states.
                break;

            case null:
                LogReceived?.Invoke(line);
                break;
        }
    }

    private async Task ApplyRemoteMoveAsync(int cell)
    {
        lock (_sync)
        {
            _ = _game.TryApplyMove(cell, ParseMark(GameRoles.ClientMark(_round)));
        }

        await PublishStateAsync().ConfigureAwait(false);
    }

    private void HandleRematchOffer()
    {
        bool start;
        lock (_sync)
        {
            _clientWantsRematch = true;
            start = _hostWantsRematch;
        }

        if (start)
        {
            _ = StartNextRoundAsync();
        }
        else
        {
            RematchRequested?.Invoke();
        }
    }

    private async Task StartNextRoundAsync()
    {
        lock (_sync)
        {
            _round++;
            _hostWantsRematch = false;
            _clientWantsRematch = false;
            _game.Reset(Player.X);
        }

        await PublishStateAsync().ConfigureAwait(false);
    }

    private async Task PublishStateAsync()
    {
        GameStateRecord state = BuildState();
        CurrentState = state;
        StateChanged?.Invoke(state);
        try
        {
            await _server.BroadcastLineAsync(GameJson.Serialize(state)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Receiver vanished; state remains available locally.
        }
    }

    private async Task SendAsync(GameEnvelope message)
    {
        try
        {
            await _server.BroadcastLineAsync(GameJson.Serialize(message)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException)
        {
            // Receiver vanished.
        }
    }

    private GameStateRecord BuildState()
    {
        lock (_sync)
        {
            return new GameStateRecord(
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
                Round: _round);
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

- [ ] **Step 3: Create the fake server transport**

Create `tests/Client-Server-App.Tests/TestDoubles/FakeServerTransport.cs`:

```csharp
using Client_Server_App.Game;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class FakeServerTransport : IServerTransport
{
    public event Action<string>? MessageReceived;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public List<string> BroadcastLines { get; } = [];

    public Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        BroadcastLines.Add(message);
        return Task.CompletedTask;
    }

    public void ReceiveLine(string line) => MessageReceived?.Invoke(line);

    public void SimulateClientConnected() => ClientConnected?.Invoke();

    public void SimulateClientDisconnected() => ClientDisconnected?.Invoke();
}
```

- [ ] **Step 4: Add host service tests**

Create `tests/Client-Server-App.Tests/Game/TicTacToeHostServiceTests.cs`:

```csharp
using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class TicTacToeHostServiceTests
{
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
        Assert.Equal(new[] { 0, 1, 2 }, state.WinningLine!.ToArray());
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
```

- [ ] **Step 5: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~TicTacToeHostServiceTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 6: Commit**

```bash
git add Client-Server-App/Game/GameRoles.cs Client-Server-App/Game/TicTacToeHostService.cs tests/Client-Server-App.Tests/TestDoubles/ tests/Client-Server-App.Tests/Game/TicTacToeHostServiceTests.cs
git commit -m "feat: add authoritative host game service"
```

---

### Task 5: Client game service

**Files:**
- Create: `Client-Server-App/Game/TicTacToeClientService.cs`
- Create: `tests/Client-Server-App.Tests/TestDoubles/FakeClientTransport.cs`
- Create: `tests/Client-Server-App.Tests/Game/TicTacToeClientServiceTests.cs`

**Interfaces:**
- Consumes: `IClientTransport` (Task 3), envelopes (Task 2), `GameRoles` (Task 4).
- Produces: `sealed class TicTacToeClientService`:
  - ctor `TicTacToeClientService(IClientTransport client)`
  - `void Start()`
  - `Task PlayCellAsync(int cell)`, `Task SendRematchOfferAsync()`
  - `GameStateRecord? CurrentState`
  - events: `Action<GameStateRecord>? StateChanged`, `Action<string>? LogReceived`, `Action? RematchRequested`, `Action? OpponentDisconnected`

- [ ] **Step 1: Implement the service**

Create `Client-Server-App/Game/TicTacToeClientService.cs`:

```csharp
using System.IO;
using System.Net.Sockets;

namespace Client_Server_App.Game;

/// <summary>
/// Client-side session: renders authoritative states, sends move and rematch
/// requests. Performs no local rule enforcement.
/// </summary>
internal sealed class TicTacToeClientService
{
    private readonly IClientTransport _client;
    private int _round;

    public event Action<GameStateRecord>? StateChanged;
    public event Action<string>? LogReceived;
    public event Action? RematchRequested;
    public event Action? OpponentDisconnected;

    public GameStateRecord? CurrentState { get; private set; }

    public TicTacToeClientService(IClientTransport client) => _client = client;

    public void Start()
    {
        _client.MessageReceived += OnLineReceived;
        _client.Disconnected += () => OpponentDisconnected?.Invoke();
    }

    public async Task PlayCellAsync(int cell) =>
        await SendAsync(new MoveRequestRecord(cell)).ConfigureAwait(false);

    public async Task SendRematchOfferAsync() =>
        await SendAsync(new RematchOfferRecord()).ConfigureAwait(false);

    private void OnLineReceived(string line)
    {
        switch (GameJson.TryParse(line))
        {
            case GameStateRecord state:
                if (CurrentState is { } previous && state.Round < previous.Round)
                {
                    break; // stale replay from a previous round
                }

                _round = state.Round;
                CurrentState = state;
                StateChanged?.Invoke(state);
                break;

            case RematchOfferRecord:
                RematchRequested?.Invoke();
                break;

            case null:
                LogReceived?.Invoke(line);
                break;
        }
    }

    private async Task SendAsync(GameEnvelope message)
    {
        try
        {
            await _client.SendLineAsync(GameJson.Serialize(message)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or SocketException or InvalidOperationException)
        {
            // Connection gone or not established; UI learns via OpponentDisconnected.
        }
    }
}
```

- [ ] **Step 2: Create the fake client transport**

Create `tests/Client-Server-App.Tests/TestDoubles/FakeClientTransport.cs`:

```csharp
using Client_Server_App.Game;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class FakeClientTransport : IClientTransport
{
    public event Action<string>? MessageReceived;
    public event Action? Disconnected;

    public List<string> SentLines { get; } = [];

    public Task SendLineAsync(string message, CancellationToken cancellationToken = default)
    {
        SentLines.Add(message);
        return Task.CompletedTask;
    }

    public void ReceiveLine(string line) => MessageReceived?.Invoke(line);

    public void SimulateDisconnect() => Disconnected?.Invoke();
}
```

- [ ] **Step 3: Add client service tests**

Create `tests/Client-Server-App.Tests/Game/TicTacToeClientServiceTests.cs`:

```csharp
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
```

- [ ] **Step 4: Run validation**

```bash
dotnet test Client-Server-App.slnx --filter "FullyQualifiedName~TicTacToeClientServiceTests"
dotnet build Client-Server-App.slnx -c Release
```

- [ ] **Step 5: Commit**

```bash
git add Client-Server-App/Game/TicTacToeClientService.cs tests/Client-Server-App.Tests/TestDoubles/FakeClientTransport.cs tests/Client-Server-App.Tests/Game/TicTacToeClientServiceTests.cs
git commit -m "feat: add client game service"
```

---

### Task 6: `GameWindow` UI

**Files:**
- Create: `Client-Server-App/GameWindow.xaml`
- Create: `Client-Server-App/GameWindow.xaml.cs`

**Interfaces:**
- Consumes: `TicTacToeHostService` / `TicTacToeClientService` (Tasks 4–5), `GameRoles` (Task 4), `GameStateRecord` (Task 2).
- Produces: `partial class GameWindow` with two constructors — `GameWindow(TicTacToeHostService host)` and `GameWindow(TicTacToeClientService client)`. Callers construct the service, call `Start()`, then construct/show the window.

- [ ] **Step 1: Write the XAML**

Create `Client-Server-App/GameWindow.xaml`:

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
			<RowDefinition Height="130" />
		</Grid.RowDefinitions>

		<TextBlock x:Name="StatusText"
				   Grid.Row="0"
				   FontSize="16"
				   FontWeight="Bold"
				   TextAlignment="Center"
				   Margin="0,0,0,8"
				   Text="Waiting for opponent..." />

		<UniformGrid x:Name="BoardGrid"
					 Grid.Row="1"
					 Rows="3"
					 Columns="3" />

		<Button x:Name="RematchButton"
				Grid.Row="2"
				Content="Offer Rematch"
				Width="150"
				Height="30"
				Margin="0,8"
				IsEnabled="False"
				Click="RematchButton_Click" />

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

- [ ] **Step 2: Write the code-behind**

Create `Client-Server-App/GameWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Tic-tac-toe board for either role. Construct with exactly one service;
/// the caller is responsible for creating the service and calling Start().
/// </summary>
public partial class GameWindow : Window
{
    private readonly Button[] _cells = new Button[9];
    private readonly TicTacToeHostService? _host;
    private readonly TicTacToeClientService? _client;
    private readonly Brush _defaultCellBackground;
    private GameStateRecord? _renderedState;
    private bool _rematchOfferedLocally;
    private bool _opponentLeft;

    public GameWindow(TicTacToeHostService host)
    {
        _host = host;
        InitializeComponent();
        _defaultCellBackground = Brushes.White;
        CreateCells();
        host.StateChanged += OnStateChanged;
        host.LogReceived += message => AppendLog(message);
        host.RematchRequested += OnRematchRequested;
        host.OpponentDisconnected += OnOpponentDisconnected;
        Title = "Tic-Tac-Toe (Host)";
        Render(host.CurrentState);
    }

    public GameWindow(TicTacToeClientService client)
    {
        _client = client;
        InitializeComponent();
        _defaultCellBackground = Brushes.White;
        CreateCells();
        client.StateChanged += OnStateChanged;
        client.LogReceived += message => AppendLog(message);
        client.RematchRequested += OnRematchRequested;
        client.OpponentDisconnected += OnOpponentDisconnected;
        Title = "Tic-Tac-Toe (Client)";
        Render(null);
    }

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

    private async void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (_renderedState is not { Status: "inProgress" })
        {
            return;
        }

        string myMark = MyMark(_renderedState.Round);
        if (_renderedState.Turn != myMark)
        {
            return;
        }

        int cell = (int)((Button)sender).Tag!;
        try
        {
            if (_host is not null)
            {
                await _host.PlayMoveAsync(cell);
            }
            else if (_client is not null)
            {
                await _client.PlayCellAsync(cell);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or System.Net.Sockets.SocketException)
        {
            AppendLog($"Move failed: {ex.Message}");
        }
    }

    private async void RematchButton_Click(object sender, RoutedEventArgs e)
    {
        _rematchOfferedLocally = true;
        RefreshRematchButton();
        try
        {
            if (_host is not null)
            {
                await _host.RequestRematchAsync();
            }
            else if (_client is not null)
            {
                await _client.SendRematchOfferAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or System.Net.Sockets.SocketException)
        {
            AppendLog($"Rematch failed: {ex.Message}");
        }
    }

    private void OnStateChanged(GameStateRecord state) =>
        Dispatcher.BeginInvoke(() => Render(state));

    private void OnRematchRequested() =>
        Dispatcher.BeginInvoke(() =>
        {
            AppendLog("Opponent wants a rematch.");
            RefreshRematchButton();
        });

    private void OnOpponentDisconnected() =>
        Dispatcher.BeginInvoke(() =>
        {
            _opponentLeft = true;
            StatusText.Text = "Opponent disconnected.";
            foreach (Button cell in _cells)
            {
                cell.IsEnabled = false;
            }

            RematchButton.IsEnabled = false;
        });

    private void Render(GameStateRecord? state)
    {
        if (state is null)
        {
            StatusText.Text = "Waiting for opponent...";
            return;
        }

        if (_renderedState is { } previous && state.Round > previous.Round)
        {
            _rematchOfferedLocally = false;
        }

        _renderedState = state;
        string myMark = MyMark(state.Round);
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && state.Turn == myMark;

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i].Content = state.Board[i];
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

        StatusText.Text = state.Status switch
        {
            "won" when state.Winner == myMark => $"You win! ({state.Winner})",
            "won" => $"You lose. ({state.Winner} wins)",
            "draw" => "It's a draw.",
            _ when _opponentLeft => "Opponent disconnected.",
            _ when myTurn => $"Your move ({myMark}).",
            _ => "Opponent's move.",
        };

        RefreshRematchButton(inProgress);
    }

    private void RefreshRematchButton(bool inProgress = false)
    {
        bool gameOver = _renderedState is not null && !inProgress;
        RematchButton.IsEnabled = gameOver && !_rematchOfferedLocally;
        RematchButton.Content = _rematchOfferedLocally ? "Rematch offered..." : "Offer Rematch";
    }

    private string MyMark(int round) => _host is not null ? GameRoles.HostMark(round) : GameRoles.ClientMark(round);

    private void AppendLog(string message) =>
        OutputTextBox.AppendText(message + Environment.NewLine);

    protected override void OnClosed(EventArgs e)
    {
        if (_host is not null)
        {
            _host.StateChanged -= OnStateChanged;
            _host.RematchRequested -= OnRematchRequested;
            _host.OpponentDisconnected -= OnOpponentDisconnected;
        }

        if (_client is not null)
        {
            _client.StateChanged -= OnStateChanged;
            _client.RematchRequested -= OnRematchRequested;
            _client.OpponentDisconnected -= OnOpponentDisconnected;
        }

        base.OnClosed(e);
    }
}
```

Note: `LogReceived` subscriptions use lambdas and are intentionally not detached in `OnClosed` — the services die with their transports (owned by `ConnectionWindow`), so their lifetimes are strictly shorter than any leaked delegate risk would matter. Detaching the three named handlers keeps double-rendering impossible if a service outlives the window.

- [ ] **Step 3: Run validation**

```bash
dotnet build Client-Server-App.slnx -c Release
```

Must compile with 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add Client-Server-App/GameWindow.xaml Client-Server-App/GameWindow.xaml.cs
git commit -m "feat: add tic-tac-toe game window"
```

---

### Task 7: Launch games from `ConnectionWindow`

**Files:**
- Modify: `Client-Server-App/ConnectionWindow.xaml.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `TicTacToeHostService`/`TicTacToeClientService` (Tasks 4–5), `GameWindow` (Task 6).
- Produces: user-visible flow — Create Host opens a host `GameWindow`; successful Connect opens a client `GameWindow`. Closing a `GameWindow` tears down its transport so Connect/Create-Host can be used again.

- [ ] **Step 1: Rewire the handlers**

In `Client-Server-App/ConnectionWindow.xaml.cs` replace the bodies of `ConnectButton_Click` and `HostButton_Click`, and add the two `OpenGameWindow` helpers. The full file becomes:

```csharp
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using Client_Server_App.Game;

namespace Client_Server_App;

/// <summary>
/// Interaction logic for ConnectionWindow.xaml
/// </summary>
public partial class ConnectionWindow : Window
{
    private ClientTcp? _client;
    private ServerTcp? _server;

    public ConnectionWindow()
    {
        InitializeComponent();
    }

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
        client.MessageReceived += message => AppendLog($"[server] {message}");
        client.Disconnected += () => AppendLog("Disconnected from the server.");
        _client = client;
        ConnectButton.IsEnabled = false;
        try
        {
            await client.ConnectAsync(host, port);
            AppendLog($"Connected to {host}:{port}.");

            TicTacToeClientService clientService = new(client);
            clientService.Start();
            OpenGameWindow(
                () => new GameWindow(clientService),
                onClose: () =>
                {
                    client.Dispose();
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

    private void HostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePort(HostPortTextBox.Text, out int port))
        {
            AppendLog($"'{HostPortTextBox.Text}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        _server?.Dispose();
        ServerTcp server = new(port);
        server.MessageReceived += message => AppendLog($"[client] {message}");
        try
        {
            server.Start();
            _server = server;
            AppendLog($"Listening on port {port}.");

            TicTacToeHostService hostService = new(server);
            hostService.Start();
            OpenGameWindow(
                () => new GameWindow(hostService),
                onClose: () =>
                {
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

    private void OpenGameWindow(Func<GameWindow> createWindow, Action onClose)
    {
        GameWindow gameWindow = createWindow();
        gameWindow.Owner = this;
        gameWindow.Closed += (_, _) => onClose();
        gameWindow.Show();
    }

    private static bool TryParsePort(string text, out int port)
    {
        return int.TryParse(text.Trim(), CultureInfo.InvariantCulture, out port)
            && port is > 0 and <= IPEndPoint.MaxPort;
    }

    private void AppendLog(string message) =>
        _ = Dispatcher.BeginInvoke(() => OutputTextBox.AppendText(message + Environment.NewLine));

    protected override void OnClosed(EventArgs e)
    {
        _client?.Dispose();
        _server?.Dispose();
        base.OnClosed(e);
    }
}
```

Behavioral changes to note: the hardcoded "Hello from client" greeting is gone (the host pushes game state on join, which announces the client); closing a `GameWindow` disposes that side's transport, freeing Connect/Create-Host for a fresh attempt.

- [ ] **Step 2: Update the README**

In `README.md`, extend the bullet list under "A minimal WPF application…" intro paragraph:

```markdown
- **Connect** – connect to a host/port and play tic-tac-toe (or read chat text).
- **Host** – listen on a port; acts as the authoritative tic-tac-toe referee and
  broadcasts every board update to all connected clients.
```

And append a short section before "## Requirements":

```markdown
## Playing

Start two instances. In the first click **Create Host**; in the second fill in
the host's IP/port and click **Connect**. The host plays X and moves first;
marks swap after each rematch.
```

- [ ] **Step 3: Run validation**

```bash
dotnet build Client-Server-App.slnx -c Release
```

Manual smoke (optional, interactive): launch two instances, host + connect, play a game to completion, offer rematch from both sides, confirm marks swap and the board resets.

- [ ] **Step 4: Commit**

```bash
git add Client-Server-App/ConnectionWindow.xaml.cs README.md
git commit -m "feat: launch tic-tac-toe from the connection dialog"
```

---

### Task 8: End-to-end integration test

**Files:**
- Create: `tests/Client-Server-App.Tests/Integration/TicTacToeEndToEndTests.cs`

**Interfaces:**
- Consumes: everything (real `ServerTcp`/`ClientTcp` + both services).
- Produces: regression proof of the full loop: connect → join-state push → moves → win → rematch with mark swap.

- [ ] **Step 1: Write the test**

Create `tests/Client-Server-App.Tests/Integration/TicTacToeEndToEndTests.cs`:

```csharp
using Client_Server_App;
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class TicTacToeEndToEndTests
{
    [Fact]
    public async Task FullSession_ClientWins_RematchSwapsMarks()
    {
        using ServerTcp server = new(0);
        server.Start();
        using ClientTcp clientTransport = new();
        await clientTransport.ConnectAsync("127.0.0.1", server.Port);

        TicTacToeHostService host = new(server);
        host.Start();
        TicTacToeClientService client = new(clientTransport);
        client.Start();

        // Client receives the join push for round 1 (host = X, client = O).
        await WaitForAsync(() => client.CurrentState is not null);

        // Round 1: the CLIENT wins to exercise client-request validation:
        // X:0, O:3, X:1, O:4, X:6, O:5 -> O wins on [3,4,5].
        await host.PlayMoveAsync(0);
        await WaitForAsync(() => client.CurrentState!.Turn == GameRoles.ClientMark(1));
        await client.PlayCellAsync(3);
        await WaitForAsync(() => host.CurrentState!.Turn == "X");

        await host.PlayMoveAsync(1);
        await WaitForAsync(() => client.CurrentState!.Turn == GameRoles.ClientMark(1));
        await client.PlayCellAsync(4);
        await WaitForAsync(() => host.CurrentState!.Turn == "X");

        await host.PlayMoveAsync(6);
        await WaitForAsync(() => client.CurrentState!.Turn == GameRoles.ClientMark(1));
        await client.PlayCellAsync(5);

        await WaitForAsync(() => host.CurrentState!.Status == "won");
        await WaitForAsync(() => client.CurrentState!.Status == "won");
        Assert.Equal("O", host.CurrentState!.Winner);
        Assert.Equal("O", client.CurrentState!.Winner);

        // Rematch: client offers first. The host must NOT advance rounds alone.
        await client.SendRematchOfferAsync();
        await Task.Delay(100); // let any (incorrect) premature round advance surface
        Assert.Equal(1, host.CurrentState!.Round);

        // Host's click is the second vote -> round 2 starts, marks swapped.
        await host.RequestRematchAsync();

        await WaitForAsync(() => host.CurrentState!.Round == 2);
        await WaitForAsync(() => client.CurrentState!.Round == 2);
        Assert.All(host.CurrentState!.Board, cell => Assert.Equal("", cell));
        Assert.Equal("O", host.CurrentState!.Turn);   // X always starts; host is no longer X
        Assert.Equal("O", GameRoles.HostMark(2));
        Assert.Equal("X", GameRoles.ClientMark(2));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within 10 seconds.");
            }

            await Task.Delay(25);
        }
    }
}
```

Sanity-check the win arithmetic: X occupies 0, 1, 6; O occupies 3, 4, 5 → [3,4,5] is the middle row, so O wins. Host's `PlayMoveAsync(6)` is X's third mark and wins nothing ([0,1,2] incomplete, diagonal [2,4,6] blocked by O at 4, column [0,3,6] blocked by O at 3). The `Task.Delay(100)` negative check is deliberately the only delay-based assertion; every positive progression is poll-based via `WaitForAsync`.

- [ ] **Step 2: Run the full test suite + builds**

```bash
dotnet test Client-Server-App.slnx
dotnet build Client-Server-App.slnx -c Debug
dotnet build Client-Server-App.slnx -c Release
```

Everything green, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add tests/Client-Server-App.Tests/Integration/TicTacToeEndToEndTests.cs
git commit -m "test: add end-to-end tic-tac-toe session test"
```

---

## Self-Review Record

- Spec coverage checked: pure domain (Task 1), JSON envelopes + chat fallback (Task 2), join-push + disconnect events (Tasks 3–4), authority/self-healing/resync (Task 4), client request-only + stale-round discard (Task 5), GameWindow both roles (Task 6), launch-after-setup + teardown (Task 7), rematch votes/swap/round increment (Tasks 4, 8), spectator note satisfied by turn-validation rejection (Task 4 test `IllegalRemoteMove_IsRejectedAndResynced`). Out-of-scope items (lobby/AI/timers) untouched.
- Placeholder scan: no TBD/TODO snippets; every code block is final copy-pasteable content. Test sequences were hand-verified for legality (alternation, no premature wins, draw layout) and the Task 8 win arithmetic was re-derived cell by cell.
- Type consistency: `BroadcastLineAsync`, `MessageReceived`, `Disconnected` reused from existing transports; `PlayMoveAsync`/`RequestRematchAsync` (host) vs `PlayCellAsync`/`SendRematchOfferAsync` (client) used consistently in Tasks 6–8; `GameRoles.HostMark/ClientMark` consistent across Tasks 4–8; `LastBroadcastState()` filters non-state envelopes so rematch-offer broadcasts cannot break state assertions.
