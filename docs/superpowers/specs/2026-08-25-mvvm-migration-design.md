# MVVM Migration — CommunityToolkit.Mvvm ViewModels — Design

Date: 2026-08-25
Status: Approved (design discussion 2026-08-25)
Context decision: full five-window migration now; CommunityToolkit.Mvvm adopted
(verified current stable 8.4.2, Microsoft-maintained, official .NET 10 / C# 14
support — partial-property `[ObservableProperty]` needs no preview LangVersion)

## Goal

Move essentially all window logic out of code-behind into per-window ViewModels
built with CommunityToolkit.Mvvm, leaving code-behind with pure view concerns —
while keeping every existing test green throughout and adding headless ViewModel
tests for the isolated decision logic.

## Decisions (from design discussion)

| Question | Decision |
| --- | --- |
| Scope | Full migration of all five windows in one coordinated effort |
| Library | `CommunityToolkit.Mvvm` **8.4.2**, pinned centrally in CPM |
| Location | `src/Client-Server-App/ViewModels/` inside the App project (no new project — YAGNI; revisit if VMs ever need net10.0 portability) |
| Threading | Injected `IUiDispatcher` (`Post(Action)`); production impl wraps the `SynchronizationContext` captured by the window at construction; headless tests inject an immediate inline executor |
| Existing UI tests | All 38 stay untouched and must remain green — they become the binding-wiring regression net |
| New tests | One headless `…ViewModelTests` file per migrated window (plain `[Fact]`, inline dispatcher, existing fakes); ~18–22 tests covering decision branches |
| Text inputs | `UpdateSourceTrigger=PropertyChanged` on every bound input so programmatic `.Text=` pushes to the VM without focus changes |

## Architecture

### IUiDispatcher

```csharp
public interface IUiDispatcher { void Post(Action action); }
```

Production implementation wraps a captured `SynchronizationContext`; the test
implementation invokes actions synchronously. VMs never touch `Dispatcher`,
`Window`, or controls — they are constructible and fully testable without WPF
surfaces.

### ViewModel map

| Window | ViewModel | Observable members | Commands | Events raised for the view |
| --- | --- | --- | --- | --- |
| Connection | `ConnectionViewModel` | `Address`, `Port`, `PlayerName`, `HostPort`, `LogText`, `ConnectRunning` | `ConnectCommand`, `HostCommand` (async relays) | `LobbyReady(PlayerSession)`, `RefereeReady(LobbyService, int)` |
| Lobby | `LobbyViewModel` | `Rooms` (ObservableCollection<RoomInfoRecord>), `RoomNameInput`, `LogText` | `CreateRoomCommand`, `JoinRoomCommand(RoomInfoRecord)` | `Seated(JoinedRecord)` passthrough, `ReturnedToLobby` passthrough |
| Game | `GameViewModel` | `Cells` (9 × `CellVm`: `Mark`, `IsEnabled`, `IsHighlighted`), `StatusText`, `TitleText`, `RematchLabel`, `RematchEnabled`, `BannerVisible`, `OutputText` | `MoveCommand(int)`, `OfferRematchCommand`, `LeaveCommand` | none (window closes itself on `ReturnedToLobby`) |
| Server | `ServerViewModel` | `Rooms`, `LogText` | — | — |

Behavior parity requirements: every user-visible string ("Enter an IP address.",
"'…' is not a valid port", "Connection failed:", "Could not start the host:",
"Enter a room name first.", status switch text, rematch label matrix, banner
text) is produced byte-for-byte by the ViewModel exactly as code-behind did.
Render rules (winning-line highlight set, per-cell enablement = myTurn ∧ empty,
seat description suffix, spectator prefix) move verbatim into `GameViewModel`.

`ConnectionViewModel` constructor dependencies (explicit, so tests can inject
fakes without windows):

```csharp
internal ConnectionViewModel(
    Func<string, int, Task<IClientTransport>> connectFactory, // new ClientTcp + ConnectAsync
    Func<int, ServerTcp> hostFactory,                         // new ServerTcp(port)
    Func<ServerTcp, LobbyService> lobbyFactory,               // new LobbyService(server)
    IUiDispatcher ui)
```

On success it raises `LobbyReady(PlayerSession)` / `RefereeReady(ServerTcp,
LobbyService)`; the window keeps ownership of disposal exactly as today.

### Code-behind contract (~10–15 lines per window)

Keeps only: `InitializeComponent()`; constructing the VM with session/lobby +
captured `SynchronizationContext`; `DataContext` assignment; subscribing VM
events that translate to view actions (open lobby/referee through the existing
opener-probe path, `Hide()`/`Show()` pairing, `Close()` on returned-to-lobby);
`OnClosed` disposal/unsubscription. Opener probes remain window-side unchanged;
the VM raises readiness events instead of touching windows.

## Testing

- The 38 UiTests remain the primary safety net and must pass unmodified except
  where a test previously wrote control state that now flows through a binding
  (covered by `UpdateSourceTrigger=PropertyChanged`).
- Per-window ViewModel tests use plain `[Fact]` + inline dispatcher + existing
  fakes (`FakeClientTransport`, `FakeServerTransport`, `UiTestSession`), placed
  in the UiTests project under `ViewModels/`. No STA, no window construction,
  no AutomationPeers.
- Coverage gate (`scripts/run-tests.ps1`, 83%) must pass on every commit;
  combined line coverage is expected to rise (new VM code exercised by both
  suites).

## Migration Sequence (each commit buildable, all suites green)

1. Pin `CommunityToolkit.Mvvm 8.4.2`; add `IUiDispatcher` + both implementations.
2. Extract `ConnectionViewModel` + `ServerViewModel` (simplest pair), including
   their VM tests and input-trigger XAML updates.
3. Extract `LobbyViewModel` + tests.
4. Extract `GameViewModel` + tests (largest surface).
5. Final sweep: README architecture note (one line), verify gate + full suite.

## Risks & Mitigations

| Risk | Mitigation |
| --- | --- |
| Binding typos after moving state into VMs | Kept 38 UiTests assert the same visible state through real bindings |
| Worker-thread collection mutation crashes | Single rule: VM mutates observables only inside `ui.Post`; inline dispatcher makes violations deterministic in tests |
| Async command reentrancy changes UX | `AsyncRelayCommand` disables while running — same effect as the old manual button toggling |
| Source-generator IDE glitches (VS2022 older SDK bands, #1184) | CLI builds unaffected; documented, not blocking |
| Partial-property language support | Requires C# 14 — default on net10.0; verified fixed since 8.4.1 |

## Non-goals

- No DI container introduction (windows construct VMs directly).
- No MVVM toolkit usage inside Core (unchanged).
- No new project for ViewModels.
- No conversion/deletion of existing UiTests.
- No compiled-bindings adoption (unavailable on WPF; see testing-strategy.md §9).
