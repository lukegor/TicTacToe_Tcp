# TicTacToe

A minimal WPF application that demonstrates room-based tic-tac-toe over TCP:
a neutral referee server hosts named two-player rooms with spectators, and
client instances browse a live lobby to create or join a game.

- **Connect** – join as a player: browse rooms, create one, or join as player/spectator.
- **Host** – run the referee: a neutral lobby that owns rooms and never plays.

## Playing

Enter your name in the Connect panel (optional — a guest name is assigned if you
skip it); it is shown to your opponent and in the referee log. Start three
instances. Instance one clicks **Create Host** (a referee window appears).
Instances two and three click **Connect**, then create or join a room in the
lobby. The first two players seat as X and O and the game starts
automatically; further joiners watch as spectators. When a game ends, offering
a rematch notifies the opponent immediately — their button turns into
**Accept Rematch** until they respond; the round restarts (with swapped marks)
once both agree. If a player's connection drops, they rejoin their seat
automatically within a 10-second grace window — otherwise the opponent wins by
forfeit. Closing the game window returns you to the lobby; **Leave** exits
deliberately and forfeits an ongoing game.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build and run

```shell
dotnet build TicTacToe.slnx -c Release
dotnet run --project src/TicTacToe
```

Session logs are written to `%LOCALAPPDATA%\TicTacToe\logs` (one file per
run; the newest five are kept).

Warnings are treated as errors (`TreatWarningsAsErrors` in `Directory.Build.props`); the build
is expected to be warning-free.

To launch both sides on one machine, start two instances: click **Create Host** in one and
**Connect** in the other.

## Distributable build

A single-file Windows executable is produced with:

```shell
dotnet publish src/TicTacToe -c Release -p:PublishProfile=win-x64
```

Output lands in `src/TicTacToe/bin/Release/net10.0-windows/publish/win-x64/`.

## Testing

```shell
./scripts/run-tests.ps1            # runs all suites and enforces the coverage gate
```

UI suites require Windows (WPF). Coverage is merged across suites via ReportGenerator;
the gate currently requires **80%** combined line coverage.

## Docker

`Dockerfile` produces reproducible, self-contained builds inside Windows containers:

```shell
docker build -t tictactoe .
```

Requires Docker in Windows container mode. The image packages a self-contained win-x64
GUI build; running it needs an interactive Windows session, so its primary purpose is
hermetic CI builds rather than headless execution.

## Repository layout

| Path | Purpose |
| --- | --- |
| `Directory.Build.props` | Shared compile settings (nullable, analyzer policy). Target frameworks are per-project. |
| `Directory.Packages.props` | Central Package Management for all NuGet versions. |
| `TicTacToe.slnx` | XML-based solution file. |
| `src/TicTacToe.Core/` | Portable engine: game rules, rooms, protocol, transports, diagnostics (net10.0). |
| `src/TicTacToe/` | WPF application shell referencing the core (net10.0-windows). Windows follow MVVM — per-window ViewModels (CommunityToolkit.Mvvm) in `ViewModels/`, code-behind only wires DataContext/lifecycle. |
| `tests/` | xunit.v3 (MTP) suite against the core. |
