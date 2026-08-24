# Tic-Tac-Toe Over TCP — Design

Date: 2026-08-23
Status: Approved (design discussion 2026-08-23)

## Goal

Two humans play tic-tac-toe over the app's existing TCP link: the host runs an
authoritative game session, the connected client plays against it. The feature
reuses the modernized transport (`ClientTcp` / `ServerTcp`, newline-delimited
UTF-8 lines) and keeps every new unit small and independently testable.

## Decisions (from design discussion)

| Question | Decision |
| --- | --- |
| Who plays? | Human vs human over TCP. |
| Where does state live? | Server-authoritative. Host validates and applies moves; client sends requests only. |
| Wire format | JSON envelopes via `System.Text.Json` (inbox; no new runtime package). |
| Lifecycle v1 | One active game plus rematch with swapped marks. No lobby, no spectators management. |
| Architecture | Approach A: session layer over dumb pipes; no MVVM/DI rewrite; nothing inline in `ConnectionWindow`. |

## Components (`Client_Server_App.Game` namespace)

### TicTacToe (pure domain)

- Board: `Player?[]` of length 9 (row-major, cells 0–8).
- API: `TryApplyMove(int cell, Player player)` — returns success only when the
  game is in progress, it is that player's turn, and the cell is empty.
- Detection: three-in-a-row across rows, columns, diagonals → `Won` with the
  winning line; full board without a winner → `Draw`.
- No I/O, no events, no threading. Deterministic and fully unit-testable.

### Envelopes and serialization

- `MoveRequestRecord(int Cell)` — client → host.
- `GameStateRecord(string[] Board, string Turn, string Status, string? Winner,
  int[]? WinningLine, int Round)` — host → clients.
  - `Board` cells: `"X"`, `"O"`, or `""`.
  - `Status`: `"inProgress"`, `"won"`, `"draw"`.
  - `Round`: increments on each rematch; lets UIs discard stale messages.
- Static serializer helper with a cached `JsonSerializerOptions` instance
  (satisfies CA1869) and a `type` discriminator written/read per envelope.
- Any line that fails to deserialize as a known envelope is displayed as plain
  chat text in the log. This preserves compatibility with free-text messages.

### TicTacToeHostService (authority)

- Owns one `TicTacToe` instance per round.
- Subscribes to `ServerTcp.MessageReceived`:
  - `moveRequest` from any client is treated as "the opponent" (single-opponent
    assumption for v1). Validated against turn/cell/status; illegal requests are
    ignored and answered by re-broadcasting the current state (self-healing).
  - Valid move → apply → broadcast fresh `GameState`.
  - `rematchOffer` counts as the client's readiness vote.
- On client join (new `ServerTcp.ClientConnected` event, raised from
  `RegisterClient`) → immediately broadcast the current `GameState` so a client
  that connects mid-game (or reconnects) renders the live board.
- Exposes `PlayMove(int cell)` for the host UI (host is X in round 1).
- Rematch: when both sides have voted (host's own click + received offer), start
  round N+1: swap marks, reset board, X (the new mark holder) starts, broadcast
  initial state.

### TicTacToeClientService

- Sends `moveRequest` for local clicks (client is O in round 1).
- Subscribes to `ClientTcp.MessageReceived`, parses envelopes, raises typed
  events (`StateReceived`, plus rematch prompts). No optimistic rendering:
  the board updates only on authoritative state.

### GameWindow

- Opened automatically after Create-Host (role host) or successful Connect
  (role client); `ConnectionWindow` remains the connection manager.
- Layout: status text ("Your turn", "Opponent's turn", result), 3×3 grid of
  cell buttons (disabled while not your turn or game over), rematch button
  (enabled after win/draw), and a message log reusing the existing log pattern.
- Marks rendered from the player's perspective using its role for the current
  round; stale states (older `Round`) are ignored.
- Disconnect handling:
  - Host learns opponent left via new `ServerTcp.ClientDisconnected` event
    (raised from `RemoveClient`) → status "Opponent disconnected".
  - Client uses existing `ClientTcp.Disconnected`.
- Extra clients beyond the first behave as spectators: they receive states and
  render the board, but their `moveRequest`s fail turn validation (v1 note).

## Protocol summary

```
client → host : {"type":"moveRequest","cell":4}
client/host   : {"type":"rematchOffer"}
host → all    : {"type":"state","board":["X","","",...],"turn":"O",
                  "status":"inProgress","winner":null,"winningLine":null,"round":1}
anything else : treated as chat text (rendered in both logs)
```

## Error handling

| Case | Behavior |
| --- | --- |
| Malformed JSON / unknown type | Rendered as chat text; receive loops never crash. |
| Illegal or out-of-turn move | Ignored by authority; current state re-broadcast to resync. |
| Opponent disconnect mid-game | Status text announces disconnect; board freezes at last state. |
| Connection refused / lost during setup | Existing `ConnectionWindow` error paths (unchanged). |

## Testing

New xUnit project `tests/Client-Server-App.Tests`, registered in the `.slnx`,
versions managed by Central Package Management:

- Win detection: rows, columns, diagonals; draw on full board.
- `TryApplyMove` rejects: wrong turn, occupied cell, move after game over.
- Rematch increments round and swaps starting marks.
- Envelope round-trip: serialize → deserialize equality for both message kinds;
  non-envelope strings fail gracefully (chat fallback path).

No WPF UI automation and no live-socket tests — transport behavior was already
smoke-verified end-to-end against the built assembly.

## Out of scope (v1)

- Multiple simultaneous games, lobbies, spectating controls.
- AI opponent.
- Move timers, rankings, persistence.
