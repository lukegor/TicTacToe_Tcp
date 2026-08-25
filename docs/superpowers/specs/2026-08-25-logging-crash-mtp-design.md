# Logging Pipeline, Crash Safety Net & MTP Coverage — Design

Date: 2026-08-25
Status: Approved (design discussion 2026-08-25)
Source backlog: `docs/engineering-analysis.md` items O1 (logging), O4 (crash net), R2 (MTP coverage)

## Goal

Give the app a leveled logging pipeline with a persistent rolling file sink, a global
crash safety net with graceful shutdown, and migrate all tests onto the Microsoft
Testing Platform (MTP) with code-coverage collection and an honest threshold gate —
without changing any user-visible behavior of the current UI panels or game flows.

## Decisions (from design discussion)

| Question | Decision |
| --- | --- |
| Logging abstraction | `Microsoft.Extensions.Logging.Abstractions` only (no host, no provider packages); sinks stay hand-written. |
| Unhandled-exception policy | Log critical entry → friendly dialog (Copy details / Open log folder / Close) → graceful app shutdown. |
| Unobserved task exceptions | Log as Error only — no dialog, no shutdown (not process-fatal since .NET 4.5). |
| Coverage gate | Measure real statement coverage first, set the threshold just below it; if unsatisfying, do not pad tests — set the honest gate and record a TODO of under-covered areas. |
| Test platform cutover | MTP-only clean cut: remove `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` entirely. |
| Sequencing | Three phases, each independently green: MTP → logging → crash net. |

---

## Phase 1 — MTP cutover

Package changes (central versions in `Directory.Packages.props`):

| Remove | Add |
| --- | --- |
| `Microsoft.NET.Test.Sdk` | `xunit.v3` **4.0.x** (stable 4.0.0; MTP v2 by default) |
| `xunit.runner.visualstudio` | `Microsoft.Testing.Extensions.CodeCoverage` |
| `xunit` (2.9.3) | |

Test project becomes an executable (`<OutputType>Exe</OutputType>`, required by
xunit.v3/MTP); `UseWPF` stays as-is. Existing suites use plain `[Fact]`/`[Theory]`/
`Assert.*` with hand-rolled fakes and are expected to be near source-compatible;
any v2→v3 API deltas (e.g., `IAsyncLifetime` returning `ValueTask`) are fixed as
encountered, with no behavior rewrites.

Coverage workflow (local only in this scope — CI is backlog item R1):

```shell
dotnet test   # .NET 10 SDK drives MTP projects directly
tests\Client-Server-App.Tests\bin\...\Client-Server-App.Tests.exe ^
  --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

Threshold is set after measuring; ReportGenerator HTML output optional locally.

## Phase 2 — Logging pipeline

New folder `Client-Server-App\Diagnostics\`:

### LogConfig

Record with `LogLevel MinimumLevel` (default `Information`) and optional
`string? Directory` override for the sink location. Constructed once at startup;
no global mutable state.

### FileLoggerProvider

Minimal custom `ILoggerProvider`:

- Format: `HH:mm:ss.fff [LEVEL] [category] message`.
- Single background writer (channel queue → one `StreamWriter`); flush + dispose on shutdown.
- One file per process session: `%LOCALAPPDATA%\Client-Server-App\logs\yyyy-MM-dd_HH-mm-ss.log`
  (or `LogConfig.Directory` when overridden).
- On startup, prune to the newest 5 session files.

### Injection & UI compatibility

- `ILogger<T>` passed as *optional* constructor parameters into `ServerTcp`,
  `ClientTcp`, `LobbyService`, `Room`, and `PlayerSession`; null defaults to
  `NullLogger.Instance`. Services never see files or UI.
- Existing string events (`LobbyService.LogReceived`, `PlayerSession.LogReceived`)
  remain untouched; the internal log helper emits both a structured entry via
  `ILogger` and the human-readable text via the event. All four windows keep their
  current behavior unchanged.

Excluded deliberately: scopes/enrichment beyond ids already present in messages,
external sinks, runtime level switching (restart-level only).

## Phase 3 — Crash safety net

New `Diagnostics/CrashHandler.cs`, registered once during `App` startup:

| Hook | Behavior |
| --- | --- |
| `DispatcherUnhandledException` | Mark handled → handle-and-shutdown flow (suppresses WPF default crash dialog) |
| `AppDomain.UnhandledException` | Same flow; if the UI thread is gone, skip dialog, log and exit with failure code |
| `TaskScheduler.UnobservedTaskException` | Error log entry only |

Handle-and-shutdown flow: reentrancy guard → `LogCritical` (exception + origin +
thread) → flush file sink → dialog via an injected `ICrashReporter` ("An unexpected
error occurred…", buttons Copy details / Open log folder / Close) →
`Application.Current.Shutdown()`.

Accepted limit: one process hosts client and referee windows alike, so any crash
ends the whole process (rooms included) — identical reachability to today, minus
the silent death.

## Testing strategy

| Area | Coverage |
| --- | --- |
| `FileLoggerProvider` | Formatted output, minimum-level filter, retention pruning, flush-on-dispose (temp directory) |
| `LobbyService`, `Room`, `PlayerSession` | Capturing `ILogger` double asserts leveled entries emitted alongside existing text events (no UI regression) |
| `CrashHandler` | Decision logic returns LogWritten/Dialog/Shutdown decisions; fake `ICrashReporter`; unobserved-task path logs without shutdown; reentrancy guard collapses concurrent hits |
| Existing suites | Remain green; only API-migration edits permitted |

## Rollout

Three commits, each independently buildable and testable:

1. `test: migrate to xunit.v3 on MTP with code coverage`
2. `feat: leveled logging pipeline with rolling file sink`
3. `feat: global crash safety net with graceful shutdown`

Plus a two-line README note on log location added with commit 2.

## Non-goals

- Serilog/NLog providers, OpenTelemetry exporters, metrics dashboards (separate backlog items O2/O3).
- CI workflows/badges (backlog item R1).
- Runtime log-level switching or per-category filters beyond minimum level.
