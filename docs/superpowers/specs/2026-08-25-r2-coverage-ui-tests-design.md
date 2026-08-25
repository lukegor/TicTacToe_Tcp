# R2 Completion — Honest Coverage Gate + Behavior-Focused UI Tests — Design

Date: 2026-08-25
Status: Approved (design discussion 2026-08-25)
Source backlog: `docs/engineering-analysis.md` R2 remainder + recorded TODO; absorbs the testing portion of T6
Baseline at design time: 69.4% statement / 60.6% branch; gaps concentrated in WPF code-behind (`GameWindow` 124 lines @0%, `ConnectionWindow` ≈93, `LobbyWindow` ≈69, `ServerWindow` 30); `Game/` services ≈97%

## Goal

Make the coverage number honest and enforceable: add behavior-focused tests for the
four windows (happy and sad paths), top up measured Core gaps only, and introduce a
one-command coverage gate that fails below threshold — without changing any product
behavior beyond two tiny seams.

## Decisions (from design discussion)

| Question | Decision |
| --- | --- |
| Scope | Complete R2's goal: UI tests + Core sad-path top-ups + local threshold gate. Absorbs T6's testing portion. |
| UI driving technique | In-process `[WpfFact]` STA tests on real windows; button presses via `ButtonAutomationPeer.Invoke()`; assertions on user-visible state only. FlaUI/UIA rejected (heavyweight); MVVM extraction rejected (architecture rewrite out of scope). |
| `Xunit.StaFact` version | Latest 4.x even if pre-release, matching `xunit.v3` 4.0.0; exact version pinned centrally in CPM. |
| Where UI tests live | New `tests/Client-Server-App.UiTests/` (`net10.0-windows`, `UseWPF`, references Core + App). Existing Core-only suite untouched. |
| Shared test doubles | Moved to `tests/TestSupport/`, compiled into BOTH test projects via `<Compile Include>` links — no duplication, no extra project. |
| Gate mechanism | `scripts/run-tests.ps1`: run both suites with cobertura outputs → merge with ReportGenerator (pinned via `.config/dotnet-tools.json`) → print per-assembly + total line-rate → exit non-zero below threshold. |
| Threshold policy | Measure after UI tests land; gate at `min(80, measured − 5)`; any shortfall becomes a named TODO — padding forbidden. |
| Production changes | Exactly two internal optional delegate parameters on `ConnectionWindow` (lobby-opener, referee-opener) defaulting to current behavior so happy paths don't spawn uncloseable windows. Nothing else. |

## Testing Principles (binding)

1. **Behavior over implementation.** Assert only what a user can observe: output/log
   text, list contents, `IsEnabled`, rendered cell marks, and envelopes captured on
   the fake wire. Press buttons through AutomationPeers, never by invoking private
   handlers; reflection is banned in tests.
2. **Happy/sad pairing.** Every behavior touched gets at least one success path and
   one hostile-input/hostile-event path.
3. **Determinism.** No `Thread.Sleep`, no real timers, synchronous fakes; async UI
   work is drained with a `TestDispatcher.FlushAsync()` helper (lives in TestSupport).
4. **No new mocking stacks.** The existing hand-rolled `Fake*Transport` doubles and
   `CapturingLogger` remain the only stand-ins.
5. **Coverage is a lint, not a goal.** Core top-ups are chosen strictly from the
   post-UI-tests cobertura diff; speculative tests are rejected.

## Test Inventory (~29 UI tests + measured Core top-ups)

### ConnectionWindow (7) — opener seams injected; live `ServerTcp` on port 0 for happy paths
| # | Path | Observable assertion |
| --- | --- | --- |
| 1 | H: connect against test server | lobby-opener invoked exactly once; Connect button re-enabled |
| 2 | S: empty address | output box shows "Enter an IP address."; nothing opened |
| 3 | S: port "abc" / 0 / 70000 (theory) | "'…' is not a valid port" logged per case |
| 4 | S: unreachable endpoint | "Connection failed:" logged; no opener invoked; button enabled |
| 5 | H: Host on free port | referee-opener invoked once; "Listening on port N." logged |
| 6 | S: Host on bound port | "Could not start the host:" logged; app alive |
| 7 | H: long display name | hello envelope name equals the trimmed textbox content |

### LobbyWindow (6) — fake-backed `PlayerSession`; window never shown
| # | Path | Observable assertion |
| --- | --- | --- |
| 8 | H: rooms at construction | list renders names + "N player(s), M spectator(s)" |
| 9 | H: `roomList` arrives on worker thread | list refreshes (Dispatcher marshaling proof) |
| 10 | H: Create pressed with text | `createRoom{name}` envelope on the wire matching the textbox content |
| 11 | S: Join pressed with no selection | error notice in output box; nothing sent |
| 12 | S: `error` envelope arrives | message surfaced verbatim in output box |
| 13 | H/S: returned-to-lobby notice | appended to output box |

### GameWindow (12) — fake-backed seated session; biggest surface
| # | Path | Observable assertion |
| --- | --- | --- |
| 14 | H: fresh `inProgress` render | nine empty cells; correct turn indicator |
| 15 | H: my turn, click empty cell | exactly one `moveRequest{cell}` sent |
| 16 | S: click when not my turn | no envelope sent |
| 17 | S: spectator clicks | no envelope; spectating indicator visible |
| 18 | H: state echo applies move | cell renders my mark |
| 19 | H: win with `WinningLine` | the three cells visually distinct |
| 20 | S: draw reached | status reflects draw; further clicks send nothing |
| 21 | H: offer rematch | `rematchOffer` sent; waiting visual state |
| 22 | H: opponent offers (`RematchOfferedBy`) | button flips to Accept; accepting sends offer |
| 23 | S: reconnect grace starts | banner visible |
| 24 | H: restored `JoinedRecord` | banner hidden; restored board rendered |
| 25 | H/S: session log/chat lines | appended in order |

### ServerWindow (4) — fake-backed `LobbyService`
| # | Path | Observable assertion |
| --- | --- | --- |
| 26 | H: two rooms seated | rows show correct players/2 and spectator counts |
| 27 | H: membership change fires | rows appear/disappear live |
| 28 | H: log event on worker thread | referee log appends (dispatcher proof) |
| 29 | S: empty lobby / room closed | zero rows; no crash |

### Core top-ups (≈3–5)
Selected at plan time strictly from the post-UI-test cobertura diff. Candidates:
double-grace-expiry guards in `Room`, rejoin-into-full-room edge in `LobbyService`,
malformed-envelope variants in `GameJson`.

## Gate

`scripts/run-tests.ps1 [-Threshold <int>]`:

1. `dotnet test` Core suite → `core.cobertura.xml`
2. `dotnet test` UiTests → `ui.cobertura.xml`
3. ReportGenerator merges both → combined line-rate (+ per-assembly table)
4. Exit `0` at/above threshold; exit `1` otherwise, printing actual vs required

Threshold resolved at execution: measure, then `min(80, measured − 5)`; shortfalls
become named TODOs in this spec's follow-up notes. R1 (CI) will call this script
unchanged — gate logic exists in exactly one place.

## Risks & Mitigations

| Risk | Mitigation |
| --- | --- |
| `Xunit.StaFact` 4.x pre-release churn | Exact version pinned in CPM; upgrades deliberate |
| UI tests require Windows | Accepted; gate runs fully on Windows runners (R1); Core suite stays portable |
| Dispatcher async nondeterminism | `FlushAsync` drain helper; synchronous fakes; zero sleeps |
| ReportGenerator version drift | Locked in `.config/dotnet-tools.json` |
| Flaky window spawning | Windows are constructed, never `Show()`n, except through injected recorder seams |

## Non-goals

- No MVVM/presenter refactor of existing windows.
- No UI automation frameworks (FlaUI/UIA).
- No App-process crash-hook automation (decision logic already unit-tested; wiring verified manually).
- No CI workflow here (R1 consumes `run-tests.ps1` as-is).
