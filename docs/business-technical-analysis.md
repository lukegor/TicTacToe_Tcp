# Business–Technical Analysis — Feature Backlog

| | |
| --- | --- |
| Date | 2026-08-25 |
| Role | Business Analyst (technical lens) |
| Subject | Client-Server-App (room-based tic-tac-toe over TCP, WPF + .NET 10) |
| Context | Portfolio program — the repository's purpose is to win technical interviews, not to serve end users |
| Scope | Engineering-facing features: CI, security, deployment, platform reach. Product/business opportunities are analyzed separately in `business-analysis.md`. |
| Status | Proposal for review |

---

## 1. Executive Summary

The application is functionally complete for its stated scope: a neutral referee
server hosts named rooms, players connect over TCP, spectators watch, rematches
negotiate themselves, and dropped players get a 10-second reconnect grace. It is
well-factored, tested (unit + integration + end-to-end), and builds warning-free.

The gap is not *does it work* — it is *can a reviewer perceive that it works, in
under two minutes, without reading code*. Today that requires building a Windows-only
GUI and launching three instances. Nothing in the repo proves quality automatically
(no CI), shows what it looks like (no screenshots/demo), or survives scrutiny of its
network surface (no message limits, no flood protection, and a seat-hijack flaw in
the reconnect flow).

This backlog therefore optimizes for one metric: **reviewer time-to-impression**,
followed by breadth of engineering signals (DevOps, security, real-time systems,
product thinking) per unit of effort.

---

## 2. Stakeholders and Goals

| Stakeholder | What they need from this repo |
| --- | --- |
| Recruiter / non-technical reviewer | Instant visual proof the product exists and is polished (30 seconds). |
| Technical interviewer | Evidence of sound architecture, testing discipline, security awareness, ops maturity. |
| Developer (owner) | Talking points per interview theme; sustainable maintenance; no dead-end complexity. |

**Primary business goal:** convert repository visits into interview conversations.
Every proposed feature must map to a competency signal or reduce reviewer friction.

---

## 3. Current State Assessment

### Strengths

- Clean layered design: transports are dumb pipes; `LobbyService`/`Room` hold all
  logic and are fully unit-testable; `TicTacToe` is a pure rules engine.
- Real distributed-systems behaviors implemented and tested: authoritative state,
  spectator fan-out, grace-period reconnect, forfeit, rematch negotiation.
- Strict build hygiene (`TreatWarningsAsErrors`, nullable, analyzers), central
  package management, zero external dependencies, design docs under `docs/`.

### Findings (gaps and defects observed)

| # | Finding | Impact |
| --- | --- | --- |
| N1 | **No CI pipeline.** Quality gates exist only on the dev machine. | Reviewers cannot verify "tests pass"; no badge; looks unfinished regardless of actual quality. |
| N2 | **No visual evidence.** Zero screenshots/GIF/video; WPF apps are rarely run by reviewers. | The single highest-leverage gap. Work is invisible. |
| N3 | **Seat hijack during reconnect grace.** Reconnection re-authenticates by self-reported display name only; anyone sending `hello:"Alice"` + `joinRoom` can claim Alice's reserved seat mid-grace. | Genuine security defect; also the perfect threat-modeling story once fixed. |
| N4 | **Unbounded network input.** No maximum line length, no per-connection message-rate limit, no read timeout. One client can exhaust server memory or starve the loop. | Robustness/security gap on the exact layer the project showcases. |
| N5 | **Malformed JSON becomes chat.** `GameJson.TryParse == null` routes garbage into room chat as plain text. | Protocol ambiguity; invalid frames should yield targeted `error`, not broadcast noise. |
| N6 | **Docker image has no runtime purpose.** It packages a GUI that needs an interactive session; there is no headless host. | Missed deployment story; integration tests can't run against a real container. |
| N7 | **No persistence.** All match history evaporates on shutdown. | Blocks leaderboards/stats; shortens perceived product depth. |
| N8 | **Windows-only reach.** WPF + raw TCP means most reviewers (macOS/Linux) cannot execute anything. | Strategic constraint; address by demo assets now, cross-platform client later. |

---

## 4. Proposed Features

Priority reflects portfolio ROI (signal ÷ effort). Effort: S < 1 day, M ≈ 1–3 days, L > 3 days.

### P0 — Credibility baseline (do these first)

#### F1 — Continuous Integration pipeline `Effort: S · Signals: DevOps, professionalism`
GitHub Actions workflow: restore, build Release, run tests, publish coverage summary;
badge in README. The repo already treats warnings as errors — make that verifiable.
*Acceptance:* green check on every push; README badge; PRs blocked on red.

#### F2 — Visual demo assets `Effort: S · Signals: product communication`
Screenshots (lobby, game window, referee log) plus a short GIF of a full round,
embedded at the top of the README. Record once, benefits every future viewer.
*Acceptance:* reviewer understands the product from the README alone in < 60 s.

#### F3 — Network input hardening `Effort: S · Signals: security awareness`
Enforce max line length (e.g., 4 KB), per-connection message rate cap, idle/read
timeouts; drop-and-log violators. Small change in `ServerTcp`/`ClientTcp`, large
credibility gain — this is the layer the project claims to showcase.
*Acceptance:* oversized/flooded connections are disconnected; tests cover limits.

#### F4 — Authenticated reconnect tokens `Effort: M · Signals: threat modeling, protocol design`
Fix N3: on seat, server issues a random reconnect token (delivered only to that
seat's connection); grace-window reclaim requires the token, not the name.
Also resolves duplicate-display-name ambiguity (N3 adjacent).
*Acceptance:* attacker with a known name cannot steal a seat; legitimate reconnect
flow unchanged; documented in the protocol spec.

#### F5 — Turn timer `Effort: M · Signals: real-time systems, edge-case rigor`
Configurable per-move countdown (default off; e.g., 30 s when enabled). Expiry
auto-forfeits the move/game. Exercises timers racing against network events —
classic source of subtle bugs, great interview material, prevents stalled rooms.
*Acceptance:* timeout produces deterministic forfeit; pause/resume across reconnect
grace is specified and tested.

### P1 — Depth and deployability

#### F6 — Headless referee host + runnable Docker image `Effort: M · Signals: deployment, ops`
CLI mode (`Client-Server-App --headless [--port N]`) running the same
`LobbyService` with console logging; Docker image gains a real runtime purpose and
Linux CI can run true networked integration tests.
*Acceptance:* `docker run` serves a lobby reachable by GUI clients; documented.

#### F7 — Built-in AI opponent `Effort: M · Signals: algorithms, single-user demo`
"Practice vs Computer" room: minimax tic-tac-toe (perfect play is trivial at this
board size) with difficulty tiers (random / heuristic / perfect). Enables a complete
demo with **one** instance and adds an algorithmic dimension to a CRUD-free codebase.
*Acceptance:* perfect tier never loses; difficulty selectable at room creation.

#### F8 — Match persistence + lobby leaderboard `Effort: M · Signals: data modeling, product sense`
Append results (players, outcome, rounds, duration) to a local store — SQLite via
`Microsoft.Data.Sqlite` or plain JSONL to keep the zero-dependency story. Lobby
shows top players by wins/streaks.
*Acceptance:* results survive restarts; leaderboard renders in lobby.

#### F9 — Series score & best-of-N `Effort: S · Signals: product iteration`
Extend the existing rematch flow: cumulative series score (X–O), optional
best-of-3/5 set at creation. Natural evolution of a mechanic already built.
*Acceptance:* series state visible in `GameStateRecord`; resets when room empties.

#### F10 — Configurable server + structured logging `Effort: S · Signals: operability`
Port, grace period, max rooms, name policy, timer defaults via CLI args/environment;
replace ad-hoc strings with `Microsoft.Extensions.Logging` console formatter (or a
minimal equivalent preserving zero-dependency ethos).
*Acceptance:* all knobs documented; logs timestamped and greppable.

#### F11 — Protocol hygiene: error on malformed frames `Effort: S · Signals: correctness`
Fix N5: unparseable lines return targeted `error` envelopes; free-text chat gets an
explicit `chat` envelope type instead of being a fallback bucket.
*Acceptance:* garbage never reaches other users' chat; spec updated.

### P2 — Reach and polish (choose deliberately)

#### F12 — Cross-platform client via Avalonia `Effort: L · Signals: modern .NET, adaptability`
Strategic answer to N8: one XAML-flavored codebase running on Windows/macOS/Linux.
Highest-effort item here; sequence after P0/P1 prove their value. Alternative smaller
step: keep WPF, add a terminal client against the same protocol to prove transport
portability.
*Acceptance:* playable on Linux/macOS; shared game logic untouched.

#### F13 — Optional TLS transport `Effort: M · Signals: security depth`
`SslStream` decorator behind the existing transport interfaces, enabled by flag with
self-signed cert generation for local play. Plaintext remains the default for LAN.
*Acceptance:* encrypted sessions interoperate with existing flows; tests cover both.

#### F14 — Replay files + in-app replay viewer `Effort: M/L · Signals: tooling, diagnostics`
Persist per-match move logs (JSONL); load and step through them in a viewer window.
Pairs naturally with F8 and gives interviewers something to click through solo.

#### F15 — Accessibility & theming pass `Effort: M · Signals: inclusive engineering`
Keyboard-navigable board, AutomationProperties names, high-contrast and dark themes,
focus visuals. Frequently forgotten in portfolio apps; disproportionately noticed.

---

## 5. Prioritization Matrix

| ID | Feature | Portfolio value | Effort | Priority | Phase |
| --- | --- | --- | --- | --- | --- |
| F1 | CI pipeline | ★★★★★ | S | P0 | 1 |
| F2 | Screenshots / GIF demo | ★★★★★ | S | P0 | 1 |
| F3 | Input hardening | ★★★★☆ | S | P0 | 1 |
| F11 | Error on malformed frames | ★★★☆☆ | S | P0 | 1 |
| F4 | Reconnect tokens | ★★★★☆ | M | P0 | 2 |
| F5 | Turn timer | ★★★★☆ | M | P0 | 2 |
| F9 | Series score | ★★★☆☆ | S | P1 | 2 |
| F6 | Headless host + Docker run | ★★★★☆ | M | P1 | 3 |
| F7 | AI opponent | ★★★★☆ | M | P1 | 3 |
| F8 | Persistence + leaderboard | ★★★★☆ | M | P1 | 3 |
| F10 | Config + logging | ★★★☆☆ | S | P1 | 3 |
| F13 | Optional TLS | ★★★☆☆ | M | P2 | 4 |
| F14 | Replays + viewer | ★★★☆☆ | M/L | P2 | 4 |
| F15 | Accessibility & theming | ★★★☆☆ | M | P2 | 4 |
| F12 | Cross-platform (Avalonia) | ★★★★☆ | L | P2 | 4 |

---

## 6. Roadmap

| Phase | Theme | Contents | Outcome |
| --- | --- | --- | --- |
| 1 | Credibility | F1, F2, F3, F11 | Repo verifies itself and sells itself; obvious flaws closed. ~2–3 days. |
| 2 | Correctness depth | F4, F5, F9 | Security story and real-time rigor become interview material. |
| 3 | Deployability & product | F6, F7, F8, F10 | Runs anywhere, demos alone, remembers its games. |
| 4 | Reach (opt-in) | F12–F15, chosen by target roles | Broaden audience; pick per desired specialization (platform vs security vs product). |

---

## 7. Explicit Non-Goals (recommended restraint)

Declining these is part of the analysis — each costs more than it signals:

- **Accounts/OAuth/authentication server** — reconnect tokens (F4) cover identity credibly at this scale.
- **WebSocket/gRPC rewrite** — transport interfaces already isolate the choice; rewriting buys no new signal.
- **Matchmaking queues, Elo ladders as infrastructure** — a static leaderboard (F8) delivers the perception; ranking systems invite scope creep.
- **Microservice decomposition / external database servers** — one process, one file store; splitting adds failure modes with no reviewer-visible benefit.
- **Plugin system for multiple board games** — the pure `TicTacToe` engine boundary already tells that story in one sentence.
- **Localization/i18n** — negligible interviewer value relative to string-extraction churn.

---

## 8. Success Metrics (portfolio terms)

| Metric | Target |
| --- | --- |
| Reviewer time-to-comprehension (README only) | < 60 seconds (F2) |
| CI runs on push, always green | 100% of commits (F1) |
| Known security defects open | 0 (N3/N4/N5 closed by F3/F4/F11) |
| Instances required for a live demo | 3 → 1 (F7) |
| Platforms able to host the referee | 1 → any (F6) |

---

## 9. Assumptions and Risks

- Assumes the portfolio audience includes non-Windows users; if strictly .NET-desktop
  reviewers, demote F12/F13 further.
- Phases 1–2 are safe bets for any engineering role; Phase 4 items should be selected
  against the target role (security → F13; platform → F12; product/data → F14/F15).
- Risk: feature sprawl diluting the "small, flawless core" narrative. Mitigation:
  each phase ends releasable; non-goals list above is binding until revisited.
