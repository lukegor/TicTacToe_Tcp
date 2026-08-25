# Project Restructure — Portable Core + WPF Shell — Design

Date: 2026-08-25
Status: Approved (design discussion 2026-08-25)
Prior art: superseded draft `docs/project-structure.md` (folded into this spec)
Drives backlog items: headless referee (F6), bot harness (T1), `[WpfFact]` UI tests (T6), cross-platform client path (F12), architecture tests (T5)

## Goal

Split the solution into a portable engine library and a thin WPF shell so that
non-GUI consumers become possible and the "UI cannot leak into engine" rule is
enforced by the compiler — with zero test-logic changes and every commit green.

## Decisions (from design discussion)

| Question | Decision |
| --- | --- |
| Approach | Two-product split under `src/` (Core library + WPF app). Finer splits rejected — no consumers yet (YAGNI); single-project status quo rejected — fake boundary, blocks F6/T1/T5/F12. |
| Namespaces | Renamed now to match assemblies (`Client_Server_App.*` → `ClientServer.*`), as a dedicated final commit. |
| Scope | Structure only. `tools/` is a reserved name; created when BotHarness/F6 land. No RefereeCli stub. |
| Tests | Reference Core only; TFM `net10.0` (drops Windows gate). App gets its own UI-test project later (T6). |
| Artifacts | `win-x64.pubxml` (self-contained single-file) added now; release workflow R3 will consume it. |

## Target Layout

```text
src/
  Client-Server-App.Core/            net10.0 lib  → Client-Server-App.Core.dll
    Client-Server-App.Core.csproj    InternalsVisibleTo("Client-Server-App.Tests")
    Game/        TicTacToe.cs  Room.cs  LobbyService.cs  PlayerSession.cs
                 (includes PlayerSessionState)  GameEnvelope.cs  GameJson.cs
    Transports/  Transports.cs (IServerTransport/IClientTransport)
                 ServerTcp.cs  ClientTcp.cs
    Diagnostics/ LogConfig.cs  FileLoggerProvider.cs
                 SingleProviderLoggerFactory.cs  CrashHandler.cs (+ICrashReporter)
  Client-Server-App/                 net10.0-windows WinExe (UseWPF)
    Client-Server-App.csproj         ProjectReference → Core
    AssemblyInfo.cs (ThemeInfo)      App.xaml(.cs)   five windows
    Diagnostics/MessageBoxCrashReporter.cs     # WPF impl of Core's ICrashReporter
    Properties/PublishProfiles/win-x64.pubxml
tools/                               # reserved name; nothing created now
tests/
  Client-Server-App.Tests/           net10.0 Exe (xunit.v3/MTP); references Core only
docs/
```

Dependency rule (the single enforced boundary): **App → Core; Core references
nothing; Tests → Core only.**

Artifacts: dev builds stay framework-dependent; release produces one
self-contained `win-x64` `Client-Server-App.exe` via the publish profile
(`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`) — feeds the
downloadable-demo goal (B1).

## Build Configuration

- `Directory.Build.props`: global `<TargetFramework>` removed — TFMs per project.
  All other shared settings (nullable, warnings-as-errors, analyzers, code style)
  remain global.
- `Directory.Packages.props`: unchanged mechanism; no new versions needed.
- App csproj: loses moved sources; gains Core reference; its `InternalsVisibleTo`
  is deleted (tests no longer reference App).
- Tests csproj: drops `UseWPF` and the App reference; MTP/xunit.v3 setup untouched;
  assembly name stays `Client-Server-App.Tests` so Core's `InternalsVisibleTo`
  keeps working.
- `.slnx`: Core added under `src/`; app/tests paths updated.
- Dockerfile: restore layer copies both `src/**/**.csproj` plus props files;
  publish targets the app project; entry point unchanged.

## Namespace Map

| Old | New |
| --- | --- |
| `Client_Server_App.Game` | `ClientServer.Core.Game` |
| transports (`ServerTcp`, `ClientTcp`, interfaces in old root ns) | `ClientServer.Core.Transports` (`Transports.cs` relocates from `Game/` to `Transports/`) |
| engine diagnostics | `ClientServer.Core.Diagnostics` |
| windows / `App` / reporter | `ClientServer.App` / `ClientServer.App.Diagnostics` |
| `Client_Server_App.Tests.*` | `ClientServer.Tests.*` |

Every XAML `x:Class` is updated with its code-behind; `StartupUri` unchanged.

## Migration Sequence (each commit buildable, suite green)

1. **Extract core** — physical moves keep *old* namespaces (pure-move review diff);
   new Core csproj; slnx/Dockerfile/per-project TFM updates; tests drop the App
   reference and go `net10.0`.
2. **Align namespaces** — mechanical rename sweep per the map above across .cs,
   .xaml, and tests.
3. **Add publish profile** — `win-x64.pubxml` + README artifact note.

## Validation Gates

Per commit: `dotnet build Client-Server-App.slnx -c Release` warning-free and the
full MTP suite green (84/84 today).

Commit 1 adds an implicit portability assertion: the entire suite compiles and runs
as `net10.0` against Core alone — any WPF type entering engine code fails on any OS.

Commit 3 adds the artifact gate: publish yields a single exe with no loose
`Client-Server-App.Core.dll` beside it; launch smoke where an interactive desktop
exists. Docker image build verified by inspection locally (optional manual where
Windows containers are unavailable).

## Risks & Mitigations

| Risk | Mitigation |
| --- | --- |
| Missed `x:Class`/namespace during sweep | Markup compile fails loudly at build time |
| Stale WPF `_wpftmp`/obj caches after moves | Clean `obj`/`bin` if ghost compile errors appear |
| Hidden Windows coupling in Core | Impossible by construction: `net10.0` restore fails otherwise |
| Review noise from moves+renames | Separated into move-first / rename-second commits |

## Non-goals

- No new executables (RefereeCli/BotHarness arrive with F6/T1).
- No `tools/` or UiTests scaffolding before their features exist.
- No packaging of Core as NuGet (`IsPackable=false` default holds).
- No CI/release workflow here (R1/R3 consume the outputs later).
