# ConnectionViewModel Seam Redesign — IConnectionInfrastructure — Design

Date: 2026-08-26
Status: Approved (design discussion 2026-08-26)
Trigger: review flagged that `ConnectionViewModel` took three raw `Func<…>` dependencies
and performed infrastructure assembly (ServerTcp → LobbyService → Start) itself.

## Goal

Replace anonymous factory delegates with one intent-named seam so the ViewModel
expresses intent ("connect this session", "start hosting this port") while
infrastructure assembly returns to the composition root — with byte-for-byte
log parity and zero test-behavior change beyond updated fakes.

## Decisions

| Question | Decision |
| --- | --- |
| Seam shape | ONE cohesive interface: both capabilities are consumed by the same screen; splitting violates simplicity without benefit |
| Referee result | `record RefereeHandle(int Port, ServerTcp Server, LobbyService Lobby)` so the window keeps disposal ownership |
| Hello boundary | Infrastructure returns the session *before* the hello is sent; VM calls `session.ConnectAsync()` so "Connection failed:" parity holds for hello-phase failures |
| Host failure split | Source disposes a failed-to-bind server and rethrows the original exception; the VM logs "Could not start the host: …" exactly once |
| Player name | Passed as a parameter of `ConnectAsync` (VM owns the input) |

## Types

```csharp
internal interface IConnectionInfrastructure
{
    Task<PlayerSession> ConnectAsync(string host, int port, string playerName,
        CancellationToken ct);
    RefereeHandle StartHost(int port);
}

internal sealed record RefereeHandle(int Port, ServerTcp Server, LobbyService Lobby);
```

`RealConnectionInfrastructure(ILoggerFactory logs, Action<string> log)` (window-side)
implements it with the pre-MVVM handler body verbatim:

- transport connect (+ dispose-on-failure), log `"Connected to {host}:{port}."`,
  first-use/reconnect closure, `new PlayerSession(factory, displayName, logger)`
- host: `new ServerTcp(port, logger)` → `Start()` → on failure dispose + rethrow →
  `new LobbyService(server, logger)` → `Start()` → handle

The window constructs it after the VM (`log` bound to `vm.AppendLog`) and passes it
via the internal ctor: `(probes…, IConnectionInfrastructure? infra = null)`.

## ViewModel changes

- Ctor: `(IConnectionInfrastructure infra, IUiDispatcher ui)` — 2 deps (was 4).
- `ConnectCoreAsync`: validations → `session = await _infra.ConnectAsync(Address, port, PlayerName, ct)` →
  `await session.ConnectAsync()` → cancel-check → `LobbyReady`. Catch filter unchanged
  (OCE/ObjectDisposed/IO/Socket); `session?.Dispose()` in catch.
- `HostCore`: parse → `RefereeReady(_infra.StartHost(port))` inside the existing
  `ObjectDisposed|Socket` catch that logs `"Could not start the host: {message}"`.
- `Cancel()`/`Dispose()` unchanged.

## Testing

- `ConnectionViewModelTests`: lambdas replaced by two ~10-line recording fakes
  (`StubInfrastructure : IConnectionInfrastructure` recording calls, returning
  configurable session/handle or throwing). Assertions unchanged (hint/port messages,
  factory-not-reached counters become `ConnectCalls`/`StartHostCalls`).
- All other suites untouched. `ConnectionWindowTests` unaffected (real infra built
  inside the window; probes still suppress UI).

## Non-goals

- No DI container; no moving infra types into Core; no changes to Lobby/Game VMs.
