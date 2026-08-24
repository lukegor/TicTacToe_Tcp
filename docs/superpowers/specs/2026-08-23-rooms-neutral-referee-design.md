# Rooms & Neutral Referee — Design

Date: 2026-08-23
Status: Approved (design discussion 2026-08-23)
Supersedes: the host-plays model of `2026-08-23-tic-tac-toe-design.md`

## Goal

The server becomes a pure referee: it never plays. Players connect as clients,
see a list of rooms (or create one), and are matched two-per-room. Spectators
may watch any room. Player disconnects trigger a 10-second seat-reservation
grace with automatic reconnect; expiry awards the win to the opponent.

## Decisions (from design discussion)

| Question | Decision |
| --- | --- |
| Host instance role | Pure referee: lobby/status window, no board, cannot play. |
| Room identity | Creator-chosen names, unique case-insensitively, 1–30 chars. |
| Filling / starting | First joiner = X, second = O, game starts automatically. Extra joins become spectators. |
| Player disconnect | Seat reserved for a 10 s countdown; dropper auto-reconnects into the same seat with state restored; on expiry opponent wins (`winnerReason:"forfeit"`). Both players dropping inside the window closes the room. |
| Explicit Leave button | Immediate forfeit — no grace (deliberate act). |
| Architecture | Identity-aware dumb pipes (`Guid` per connection) + `LobbyService`/`Room` above them; clients get `PlayerSession`. |

## Transport changes

`ServerTcp` / `IServerTransport`:

```csharp
event Action<Guid>? ClientConnected;
event Action<Guid>? ClientDisconnected;
event Action<Guid, string>? MessageReceived;          // sender id + line
Task SendToAsync(Guid id, string message, CancellationToken ct = default);
Task BroadcastLineAsync(string message, CancellationToken ct = default); // all connections
```

- Each accepted connection is wrapped with a fresh `Guid`.
- All writes (targeted and broadcast) serialize through one semaphore.
- `ClientTcp` unchanged; identity is server-side only.

## Server components

### LobbyService

- Owns `Dictionary<string, Room>` keyed by lowercase name; tracks every
  connection's placement (lobby / seat / spectator).
- Routes client envelopes: `createRoom`, `joinRoom`, `leaveRoom`,
  `moveRequest`, `rematchOffer`; plain-text lines go to the sender's room only.
- Pushes a `roomList` envelope to **all** connections whenever room membership
  changes; seated UIs simply ignore it, and this guarantees an up-to-date list
  the moment anyone returns to the lobby.
- Runs each player-drop grace period via a cancellable delay (duration
  injected via constructor, default 10 seconds; tests use milliseconds).
  If the only seated player drops and the grace expires with no opponent
  present, the room closes instead of awarding a win.
- Validation failures produce targeted `error{message}` envelopes.

### Room

- Holds: name (original casing preserved for display; keyed case-insensitively),
  two seats (`Guid?` + mark), spectator set, its `TicTacToe` engine, round
  counter, per-seat rematch votes.
- Sends outbound lines through delegates supplied by `LobbyService`
  (`sendTo(memberId, line)`) and exposes its member ids for room broadcasts —
  no sockets inside, fully unit-testable.
- Lifecycle: created empty-with-X-seat; auto-starts when O sits; forfeit path
  awards win; closed (all members gone or both players gone in grace) → members
  notified via `left{reason}` and returned to lobby.

## Protocol additions

```
client → server:
  {"type":"createRoom","name":"friday"}
  {"type":"joinRoom","name":"friday"}
  {"type":"leaveRoom"}
  {"type":"moveRequest","cell":4}          (existing)
  {"type":"rematchOffer"}                  (existing)
  anything else                            → chat line routed to sender's room

server → client(s):
  {"type":"roomList","rooms":[{"name":"friday","players":1,"spectators":0}]}
  {"type":"joined","room":"friday","mark":"X"|null,"restored":false,
   "state":{...GameStateRecord fields...}}
  {"type":"state", ...existing GameStateRecord fields..., "room":"friday",
   "winnerReason":null|"forfeit"}
  {"type":"left","reason":"roomClosed"}
  {"type":"error","message":"Room name already taken."}
```

- `joined.mark` of `null` means spectator. `restored:true` marks grace-reclaim.
- `GameStateRecord` gains nothing structurally except that it now travels with
  `room` and `winnerReason` siblings in the same envelope.

## Client components

### PlayerSession (replaces TicTacToeClientService)

- Constructor takes a connect factory `Func<Task<IClientTransport>>` plus
  host/port — the seam that lets tests simulate drops without sockets.
- States: `Connecting → Lobby → Seated → Reconnecting → Lobby`.
- Raises typed events consumed by windows:
  `RoomsUpdated(IReadOnlyList<RoomInfo>)`, `Seated(JoinedInfo)`,
  `StateReceived(GameStateRecord)`, `ReturnedToLobby(string reason)`,
  `LogReceived(string)`, `ErrorReceived(string)`.
- On unexpected transport death while seated: enters `Reconnecting`, retries
  the factory every second for up to 15 seconds, re-sends `joinRoom{name}`;
  success restores the session; budget exhaustion returns to lobby with an
  error notice.
- Exposes `CreateRoomAsync(name)`, `JoinRoomAsync(name)`, `LeaveRoomAsync()`,
  `PlayCellAsync(cell)`, `SendRematchOfferAsync()`.

### Windows

- `ConnectionWindow`: unchanged layout semantics — **Create Host** starts the
  referee (`ServerWindow`); **Connect** opens `LobbyWindow`.
- `ServerWindow` (new): live table of rooms (name / players / spectators) plus
  an event log. Subscribes to `LobbyService` events.
- `LobbyWindow` (new): pushed room list, create-room box + button, Join per
  row, notice area. Seating transitions it to `GameWindow`.
- `GameWindow`: client-only now (host constructor removed). Adds a **Leave**
  button and a "Connection lost — rejoining…" banner during `Reconnecting`.
  Spectator mode renders states but keeps every cell disabled.

## Key flows

- **Create**: name validated → creator seated X → waits; room visible in lists.
- **Second joiner** seated O → initial `state` broadcast starts the game.
- **Spectators**: receive every `state`/chat; their moves are ignored by seat
  validation; they may leave freely.
- **Drop + grace**: transport dies → server starts grace CTS, notifies
  opponent via log line; dropper's `PlayerSession` auto-reconnects and re-joins;
  server detects the vacant seat inside grace, restores mark/board/round
  (`restored:true`), cancels the timer, play continues.
- **Grace expiry**: opponent marked winner (`status:"won"`,
  `winnerReason:"forfeit"`) broadcast to the room; expired dropper who returns
  late is seated as spectator with a notice.
- **Both players drop within grace**: room closes immediately; remaining
  members (spectators) get `left{reason:"roomClosed"}`.
- **Rematch**: existing two-vote model, per room; marks swap; round increments.

## Error handling

| Case | Behavior |
| --- | --- |
| Duplicate / invalid room name | Targeted `error`; UI shows message. |
| Join unknown room | Targeted `error`. |
| Move while unseated / not your turn / occupied | Ignored; authoritative resync (unchanged behavior). |
| Malformed line | Chat fallback (unchanged). Chat from an unseated connection is ignored (no lobby chat). |
| Server stops mid-session | All clients' transports die → reconnect attempts fail → return to lobby with error. |
| Grace timer races room teardown | Timer runs on a CTS cancelled by close/restore paths. |

## Testing

- `LobbyServiceTests` (fake `IServerTransport`, injectable short grace):
  create/join/start sequence, duplicate-name rejection, unknown-room rejection,
  spectator receives states and cannot move, move routing, rematch votes,
  forfeit-on-grace-expiry, restore-on-rejoin-cancels-grace, double-drop closes
  room, roomList pushes on membership changes.
- `PlayerSessionTests` (fake `IClientTransport` + scripted connect factory):
  lobby list events, seating transition, move/rematch sends, kicked-to-lobby,
  drop → scripted successful reconnect restores seating, drop → exhausted
  budget returns to lobby.
- Transport integration tests updated for `Guid` signatures (lifecycle +
  targeted send reaches exactly one client).
- Real-socket E2E: three clients — two players complete a game while a
  spectator observes; then a forced drop with millisecond grace proves both the
  reconnect-restore path and the forfeit path.
- Existing `TicTacToe` domain tests unchanged.

## Out of scope

Authentication/player names, room passwords, persistence, deployment concerns
(NAT/port forwarding), resume after full app restart, lobby-wide chat.
