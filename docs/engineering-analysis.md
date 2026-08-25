# Engineering Analysis — DevEx, Observability & Repository Management Backlog

| | |
| --- | --- |
| Date | 2026-08-25 |
| Role | Software Engineer |
| Subject | Client-Server-App — development infrastructure, diagnostics, and operational handling |
| Context | Portfolio program — reviewers judge not only the product code but how the project is operated, diagnosed, and maintained |
| Scope | What should be *added around the code*: logging, panels/dashboards, error/crash handling, configuration, build/repo management files, developer tooling. Product features live in `business-analysis.md`; security/platform gaps in `business-technical-analysis.md`. |
| Status | Proposal for review |

---

## 1. Executive Summary

The inner code is disciplined (pure engine, testable services, strict analyzer policy),
but everything *around* it is thin: there is no logging system, no crash safety net,
no persisted configuration, no CI, no coverage measurement, and the referee panel is
a plain text box. For a portfolio project this is the difference between "well-written
app" and "well-operated system" — reviewers notice the latter.

This backlog adds the surrounding machinery in five layers:

1. **Observability** — structured logs, richer referee/client diagnostic panels.
2. **Failure handling** — global exception safety net, graceful shutdown, validation.
3. **Configuration & state** — user settings persistence, CLI surface.
4. **Repository management** — CI/release workflows, Dependabot, templates, changelog, versioning.
5. **Developer tooling** — bot harness, protocol/architecture docs, coverage.

Items are sized S (< ½ day), M (½–2 days), L (> 2 days) and prioritized for
portfolio ROI.

---

## 2. Current-State Inventory (what already earns credit)

| Area | Present | Notes |
| --- | --- | --- |
| Analyzer policy | Yes | `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel latest`, `EnforceCodeStyleInBuild`, `.editorconfig`. |
| Package management | Yes | Central `Directory.Packages.props`; `NuGetAudit=true` with `NuGetAuditMode=all`. |
| Repo hygiene files | Partial | `.gitignore`, `.gitattributes`, `.dockerignore`, `LICENSE.txt`, Dockerfile. |
| Testing | Yes | xUnit unit + integration + end-to-end suites with hand-rolled fakes. |
| Runtime logging | **No** | Ad-hoc strings pushed to UI panels; nothing timestamped, leveled, or persistent. |
| Crash handling | **No** | `App.xaml.cs` empty — an unhandled dispatcher exception kills the process silently. |
| Settings persistence | **No** | Name/host/port re-typed every launch. |
| CI/CD | **No** | No `.github/` at all; quality gates are local-only. |
| Coverage | **No** | No collector package, no threshold, no reporting. |

---

## 3. Proposed Additions

### Layer 1 — Observability

#### O1 — Logging pipeline `Effort: M`
Introduce a minimal logging seam used by transports, `LobbyService`, `Room`, and
`PlayerSession`: leveled entries (`Debug/Info/Warn/Error`) carrying a connection id,
room name, and event kind. Ship two sinks: the existing UI event stream and a rolling
file under `%LOCALAPPDATA%\Client-Server-App\logs` (retention: last N sessions).
Either adopt `Microsoft.Extensions.Logging.Abstractions` (di-only, zero host) or a
~60-line internal `ILog` — both defensible; pick one and document why.
*Why it matters:* every support conversation ("it disconnected") becomes answerable
from artifacts; interviewers see production instincts.
*Acceptance:* all current string logs become structured entries; file sink verified by test.

#### O2 — Referee dashboard upgrade `Effort: M`
Replace the referee window's append-only text box with:
- **Live metrics strip**: active connections, rooms, seated/spectator counts, messages/sec.
- **Log list control** (virtualized `ListView`): severity color, column layout, text filter box, pause-on-scroll, right-click copy, export-to-file button.
- **Room inspector**: click a room row to see seats, round number, last move, grace countdowns.
Data source is the same `LobbyService` events plus new counters — no transport changes.
*Why:* turns the referee from a log viewer into a *dashboard*, which reads as ops maturity in any screenshot.

#### O3 — Client connection panel `Effort: S`
Small collapsible status area in lobby/game windows: endpoint, session id fragment,
latency (application-level ping/pong envelope), reconnect attempt counter during the
grace flow, and last protocol error received.
*Why:* makes reconnect behavior — already the app's best story — visible instead of implicit.

#### O4 — Crash safety net `Effort: S`
Wire `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and
`TaskScheduler.UnobservedTaskException`: write a crash log (via O1), show a friendly
dialog offering "copy details / open log folder", and shut down cleanly.
*Why:* cheapest possible reliability signal; currently one bad dispatch = silent death.

### Layer 2 — Failure & lifecycle handling

#### H1 — Malformed-frame handling `Effort: S`
Unparseable lines currently degrade into chat text. Return a targeted `error`
envelope, count the occurrence in metrics, and disconnect after repeated violations.
(Overlaps F11 in the technical analysis — listed here because it is runtime handling.)

#### H2 — Graceful shutdown `Effort: S`
On referee window close or Ctrl+C (future headless mode): stop accepting connections,
broadcast `left{reason:"serverShutdown"}`, drain pending writes, dispose listeners.
Clients react by returning to their connect screen with a clear message rather than a raw socket error.

#### H3 — Validation hardening pass `Effort: S`
Centralize input rules (name length/charset, cell range, room-name charset) into one
validated boundary per envelope type instead of scattered checks; add rejection counters
to the O2 metrics strip.

### Layer 3 — Configuration & user state

#### C1 — Persistent user settings `Effort: S`
JSON settings in `%APPDATA%\Client-Server-App\settings.json`: display name, last
host/port, window sizes/positions, log level. Loaded at startup, saved on change;
no schema beyond ~6 keys so it stays boring.
*Why:* removes the most repeated micro-friction (re-typing name/port every demo).

#### C2 — Command-line surface `Effort: S–M`
Parse `--port`, `--log-level`, `--headless` (stub now, real console host per technical
doc F6 later) with `System.CommandLine`-style manual parsing — keep zero-dependency
if the library pulls a graph of transitive deps against the project's ethos.
Document flags in README and `--help`.

### Layer 4 — Repository management files (the "management" layer)

#### R1 — CI workflow `.github/workflows/ci.yml` `Effort: S`
Windows runner: restore → `dotnet build -c Release` → `dotnet test` with coverage
collection → `dotnet format --verify-no-changes`. Badges in README.
The single highest-value missing file in the repository.

#### R2 — Coverage via Microsoft Testing Platform (MTP) `Effort: S–M`
Adopt **MTP** as the test runner — the native .NET 10 path, replacing the VSTest-era stack.
Target package set (versions current as of 2026-08, pinned centrally in
`Directory.Packages.props`):

| Package | Version | Replaces / purpose |
| --- | --- | --- |
| `xunit.v3` | **4.0.x** (4.0.0 stable, MTP v2 by default) | Removes `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `xunit` 2.9.3 |
| `Microsoft.Testing.Extensions.CodeCoverage` | match xunit.v3 MTP line | Coverage collection (`--coverage --coverage-output-format cobertura`) |
| `Xunit.StaFact` | 4.x line (see T6) | `[WpfFact]`/`[StaFact]` for UI-thread tests |

Run via `dotnet test` (the .NET 10 SDK drives MTP projects directly) or by invoking
the test binary directly. Enforce an honest statement threshold — measure first,
then gate (e.g. 80%+) via the extension's threshold settings or a ReportGenerator
step — and publish the HTML report + badge from CI.
*Why:* MTP is where .NET testing is heading; showing the current platform (not the
legacy collector habit) is itself a signal.
*Acceptance:* `dotnet test` runs green under MTP locally and in CI; cobertura artifact produced; threshold enforced.

Measured baseline (2026-08-25): **69.4% statement / 60.6% branch** via
`dotnet test --coverage --coverage-output-format cobertura`. The gap is concentrated
in WPF code-behind (`GameWindow`, `ConnectionWindow`, `LobbyWindow`, `ServerWindow`
≈ 330 uncovered lines at 0%), while `Game/` services sit at ~97%. Threshold gate
lands with CI (R1) once UI tests exist; TODO: unlock code-behind testing via
`Xunit.StaFact` `[WpfFact]` (backlog T6), then gate at 80%+.

#### R3 — Release automation `.github/workflows/release.yml` `Effort: M`
On tag `v*`: build self-contained win-x64, zip, generate checksums, create GitHub
Release with notes drawn from the changelog. Makes B1-style downloadable demos trivial later.

#### R4 — Dependency automation `.github/dependabot.yml` `Effort: S`
Weekly nuget + github-actions update PRs. Complements the already-enabled `NuGetAudit`.

#### R5 — Community/governance files `Effort: S`
- `CONTRIBUTING.md` — build/test/format commands, branch naming, commit style (repo history already follows conventional commits).
- `SECURITY.md` — reporting contact + supported scope.
- `PULL_REQUEST_TEMPLATE.md`, issue templates (bug/feature).
- `CODEOWNERS` (`@owner`).
Cheap credibility; reviewers open these files.

#### R6 — Changelog & versioning `Effort: S–M`
`CHANGELOG.md` (Keep a Changelog format, backfill from git history); embed version
into assembly info and the referee window title via a single `Version` property in
`Directory.Build.props` (optionally MinVer later — manual is fine at this scale).

### Layer 5 — Developer tooling & documentation

#### T1 — Bot client harness `Effort: M`
Console utility (`tools/BotHarness`) that connects N scripted clients: join rooms,
make moves with delays, chat, drop connections mid-game. Uses the public protocol only.
Unlocks: stress testing the referee, populating lively screenshots/demo video (feeds
business doc B1), soak runs in CI on demand.
*Why:* demonstrates the system behaves like a *server*, and it is fun to show.

#### T2 — Protocol documentation `Effort: S`
`docs/protocol.md`: every envelope, direction, trigger, and failure response —
generated-by-hand table next to `GameEnvelope.cs` with a header comment cross-linking
both. Add a unit test that fails when a new derived record lacks a doc entry (keeps them in sync mechanically).

#### T3 — Architecture overview `Effort: S`
One mermaid diagram (transports → services → windows) + component responsibility table
in `docs/architecture.md`, linked from README. The design docs under
`docs/superpowers/` cover decisions; this page gives the 60-second orientation.

#### T4 — Benchmark smoke project (optional) `Effort: M`
BenchmarkDotNet console project measuring envelope serialize/parse throughput —
optional, include only if interview narratives touch performance; otherwise skip to avoid dead weight.

#### T5 — Architecture fitness functions via ArchUnitNET `Effort: S–M`
Encode the architecture's invariants as executable tests using **`TngTech.ArchUnitNET`
(0.13.x)** with its **`TngTech.ArchUnitNET.xUnitV3`** extension — chosen over
NetArchTest, which is effectively dormant; ArchUnitNET offers layer definitions,
member-level rules, and slice cycle detection. Candidate rules for this codebase:
- `Game` types never reference WPF namespaces (`System.Windows.*`) — keeps the engine/UI boundary honest.
- Only transports may touch `System.Net.Sockets`.
- Windows (`*.xaml.cs`) depend only on services, never on sockets or JSON details.
- No cycles across `Transports` / `Game` / UI layers.
*Why:* turns "clean layering" from a README claim into a CI-enforced contract —
a strong senior-level signal at near-zero runtime cost.
*Acceptance:* rules run in CI alongside unit tests; an intentional violation fails the build.

#### T6 — WPF UI test enablement via Xunit.StaFact `Effort: S`
Add **`Xunit.StaFact`** so view/window logic becomes testable: `[WpfFact]` runs tests
on an STA thread with a real `DispatcherSynchronizationContext`; `[StaFact]`/`[UIFact]`
cover lighter cases. Version pairing matters — StaFact 4.x pairs with xunit.v3 4.x
(stable 3.0.13 targets xunit.v3 3.x; if the 4.x line is still pre-release at adoption
time, either take the beta deliberately or temporarily pin xunit.v3 3.2.x).
First candidates: `ServerWindow` render logic against a fake `LobbyService`, dialog
validation in `ConnectionWindow`, and dispatcher-marshaling helpers added by O1/O2.
*Why:* today zero UI code is under test because plain `[Fact]` cannot construct WPF
objects off an STA thread; this unlocks it.
*Acceptance:* sample `[WpfFact]` instantiating a window green locally and in CI (Windows runner).

---

## 4. Prioritization

| ID | Item | Value signal | Effort | Priority | Wave |
| --- | --- | --- | --- | --- | --- |
| R1 | CI workflow | ★★★★★ | S | P0 | 1 |
| O4 | Crash safety net | ★★★★☆ | S | P0 | 1 |
| R2 | MTP coverage (xunit.v3) | ★★★★☆ | S–M | P0 | 1 |
| O1 | Logging pipeline | ★★★★★ | M | P0 | 1–2 |
| C1 | Settings persistence | ★★★★☆ | S | P0 | 1 |
| R4 | Dependabot | ★★★☆☆ | S | P1 | 1 |
| R6 | Changelog + versioning | ★★★☆☆ | S–M | P1 | 1 |
| O2 | Referee dashboard | ★★★★☆ | M | P1 | 2 |
| H1 | Malformed-frame errors | ★★★★☆ | S | P1 | 2 |
| H2 | Graceful shutdown | ★★★☆☆ | S | P1 | 2 |
| R5 | Governance files | ★★★☆☆ | S | P1 | 2 |
| T1 | Bot harness | ★★★★☆ | M | P1 | 2 |
| T2 | Protocol doc + sync test | ★★★☆☆ | S | P2 | 2 |
| T5 | ArchUnitNET arch tests | ★★★★☆ | S–M | P1 | 2 |
| T6 | StaFact WPF UI tests | ★★★☆☆ | S | P2 | 3 |
| T3 | Architecture page | ★★★☆☆ | S | P2 | 2 |
| O3 | Client status panel | ★★★☆☆ | S | P2 | 3 |
| H3 | Validation centralization | ★★☆☆☆ | S | P2 | 3 |
| C2 | CLI surface | ★★★☆☆ | S–M | P2 | 3 |
| R3 | Release workflow | ★★★☆☆ | M | P2 | 3 |
| T4 | Benchmarks | ★★☆☆☆ | M | P3 | opt. |

Wave 1 ≈ 3 days total and transforms the repo's verifiability; Wave 2 makes it look
and behave like an operated service; Wave 3 rounds out polish.

---

## 5. Resulting Repository Shape (delta)

```text
.github/
  workflows/ci.yml                # R1 (+R2 coverage)
  workflows/release.yml           # R3
  dependabot.yml                  # R4
  PULL_REQUEST_TEMPLATE.md        # R5
  ISSUE_TEMPLATE/{bug,feature}.md # R5
  CODEOWNERS                      # R5
CHANGELOG.md                      # R6
CONTRIBUTING.md  SECURITY.md      # R5
docs/
  architecture.md                 # T3
  protocol.md                     # T2
tools/
  BotHarness/                     # T1
tests/
  Architecture/                   # T5 (ArchUnitNET rules)
  Ui/                             # T6 ([WpfFact] tests)
Client-Server-App/
  Diagnostics/ILog.cs …           # O1
  CrashHandler.cs                 # O4
  AppSettings.cs                  # C1
```

Existing management files stay as-is — `Directory.Build.props` gains only a `Version`
property (R6); CPM and audit policy need no changes.

---

## 6. Explicit Non-Goals

- **Serilog/NLog + full MEL host** — a two-sink need does not justify provider ecosystems; revisit if sinks multiply.
- **OpenTelemetry/metrics exporters** — no deployment target consumes them yet; the dashboard covers the story.
- **Mono-repo tooling (Nx/Bazel/etc.), artifact registries** — scale mismatch.
- **Git hooks frameworks (husky.net)** — CI enforcement is enough for a solo project.
- **Mutation testing as a gate** — mention-worthy, not maintenance-worthy here.

---

## 7. Risks & Notes

- **UI thread discipline:** O1/O2 introduce background logging/metrics into WPF — all
  sink callbacks must marshal through the dispatcher (the pattern already used in
  `ServerWindow.xaml.cs:17`). A race-prone naive implementation would undercut the
  very competence being demonstrated.
- **Log noise vs. readability:** default level `Info`; `Debug` behind settings only.
- **Coverage honesty:** set the threshold *after* measuring — a padded number invites
  exactly the wrong interview conversation.
