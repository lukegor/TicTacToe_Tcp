# Business Analysis — Product Feature Opportunities

| | |
| --- | --- |
| Date | 2026-08-25 |
| Role | Business Analyst |
| Subject | Client-Server-App — room-based tic-tac-toe product experience |
| Context | Portfolio program — the repository doubles as a product demo; its "customers" are recruiters, interviewers, and anyone who plays it |
| Scope | Pure business analysis: users, value, engagement, and product features. Engineering concerns (CI, security, deployment) are covered separately in `business-technical-analysis.md`. |
| Status | Proposal for review |

---

## 1. Executive Summary

The product today is a **working two-player experience with a hidden cost**: to see
anything happen, a visitor must assemble a second human and a server. Every business
problem identified in this analysis traces back to that single friction, plus the
absence of any reason to come back after one game.

The opportunity is a classic funnel play:

- **Attract** — let strangers experience the product in seconds, alone.
- **Engage** — get them into a real match with minimal ceremony.
- **Retain** — give them an identity, progress, and a reason for "one more game".
- **Grow** — make matches worth watching and sharing; expand who can play at all.

Fifteen features are proposed across those stages. The three highest-leverage:
**Solo Practice Mode** (removes the two-human requirement entirely), **Quick Match**
(cuts time-to-play from about a minute to about five seconds), and **Player
Profiles & Stats** (converts anonymous sessions into a persistent identity).

---

## 2. Business Objectives

| # | Objective | Measure of success |
| --- | --- | --- |
| O1 | Maximize the share of visitors who *experience* the product | % of repo visitors who reach a finished match or watch one |
| O2 | Minimize time-to-first-fun | Median time from opening the app to first move |
| O3 | Create repeat engagement | Visitors returning / sessions per visitor during review season |
| O4 | Generate memorable talking points | Interview conversations that reference specific features |
| O5 | Differentiate from typical portfolio work | Positioning vs. todo-apps/clone-class portfolio projects |

---

## 3. Audience & Personas

### P1 — "The Skimmer" (recruiter, non-technical)
- 30–60 seconds of attention; never builds or runs anything.
- **Need:** instant visual proof this is a real, polished product.
- **Failure mode today:** bounces at the README.

### P2 — "The Evaluator" (technical interviewer)
- Will read code and run things if the payoff is clear.
- **Need:** a product story worth probing — why these features, how were trade-offs reasoned.
- **Value of features:** each shipped feature is evidence of product judgment, not just coding.

### P3 — "The Solo Visitor" (peer developer, career-fair guest)
- Might actually play — but usually alone, spontaneously, briefly.
- **Need:** fun in under two minutes without coordinating with anyone.
- **Failure mode today:** cannot play at all alone; leaves.

### P4 — "The Group" (friends, colleagues, meetup crowd)
- Two or more people together; the natural audience for multiplayer.
- **Need:** fast match setup, light social features, something to cheer.
- **Failure mode today:** manual room coordination works but feels like IT support, not a game lobby.

---

## 4. Current Offering — Business Assessment

| Capability | Status | Business implication |
| --- | --- | --- |
| Real-time multiplayer with spectators | Shipped | Genuine differentiator; almost no portfolio project has live multi-client interaction. |
| Rematch negotiation, reconnect grace, forfeit rules | Shipped | Mature "rules of engagement" — reads as a serious product, great storytelling material. |
| Room lobby (create/join by name) | Shipped | Functional but ceremonial; every player performs manual coordination. |
| Solo play | Missing | The largest single barrier to P3 experiencing anything. |
| Identity & history | Missing | Every session is anonymous and disposable; nothing accumulates. |
| Progression / goals | Missing | No loop beyond "play again"; zero retention mechanics. |
| Onboarding | Missing | New users must infer the flow; P1/P3 have no guided path. |
| Watchability | Weak | Spectators exist technically, but there's no way to discover which match is worth watching. |

**Positioning statement (proposed):**
> "A live, refereed arena you can join from your desk in five seconds — watch,
> play, and build a record, no setup required."

Nothing in the current feature set contradicts this statement; it simply isn't
packaged yet.

---

## 5. Competitive Frame

| Alternative | Their strength | Our edge to press |
| --- | --- | --- |
| Typical portfolio repos (todo, clone apps) | None needed — familiar shapes | Live multiplayer + neutral referee is inherently more demonstrable; lean into "arena" framing. |
| Browser tic-tac-toe toys | Zero-install | They are single-player-only, stateless, forgettable — counter with persistence, rooms, spectators. |
| Commercial gaming platforms | Massive feature depth | Not competing on scale; compete on *clarity* — a complete product story readable end-to-end. |

Conclusion: the moat for a portfolio context is **complete-product narrative +
instant accessibility**, not breadth. Features below are filtered against that.

---

## 6. Proposed Features

Priorities use MoSCoW. Value = impact on objectives O1–O5; Effort is relative
(S/M/L) and deliberately coarse — sizing belongs to delivery planning, not analysis.

### Must-have (funnel-critical)

#### B1 — Instant Play Demo
**Problem:** P1 and P3 cannot experience the product without setup (N2/N8 in the technical doc).
**Proposal:** A "see it in action" package at the top of the README: short gameplay
video/GIF, pre-built downloadable release, and a scripted demo mode inside the app
(two AI seats playing while a visitor watches). One click → watching a live match.
**Business value:** Directly serves O1/O5; converts skimmers into viewers.
**Effort:** S–M · **Priority:** Must

#### B2 — Solo Practice Mode
**Problem:** The product requires a second human; most visitors are alone (P3).
**Proposal:** "Practice" rooms vs a computer opponent, offered right beside
multiplayer in the lobby. Difficulty selection framed as opponent personality.
**Business value:** Removes the #1 usage barrier; widens addressable audience from
"pairs" to "anyone". Highest value-per-effort item in this backlog.
**Effort:** M · **Priority:** Must

#### B3 — Quick Match
**Problem:** Creating/joining rooms by name is coordination overhead before any fun.
**Proposal:** A single prominent "Quick Match" button: enter queue, get seated
automatically against the next waiting player; rooms become an advanced option.
**Business value:** Cuts time-to-first-move (O2) from ~a minute to seconds; matches
expectations set by every mainstream game lobby.
**Effort:** M · **Priority:** Must

#### B4 — Player Profiles & Career Stats
**Problem:** Sessions are anonymous and disposable; wins vanish; nothing attaches a
player to the product (retention gap, O3).
**Proposal:** Persistent lightweight profile — chosen name, avatar mark/color,
lifetime wins/losses/draws, streaks, games played — visible in lobby and rooms.
**Business value:** Identity is the foundation every retention feature (B5, B8)
builds on; makes the lobby feel inhabited.
**Effort:** M · **Priority:** Must

### Should-have (engagement & social)

#### B5 — Progression & Recognition
**Proposal:** Light progression layered on profiles: win-streak badges, milestone
counters (first win, 10 games, 5-game streak), a simple skill indicator in the
lobby. No grind, no currency.
**Value:** Gives P4 a reason for "one more round" and gives interviewers a
product-thinking story (what to reward, what to avoid — e.g., punishing losses).
**Effort:** S–M · **Priority:** Should

#### B6 — Onboarding Flow
**Proposal:** First-run path: name → choose Play Solo / Quick Match / Browse Rooms,
with a one-screen primer. Returning players skip straight to the lobby.
**Value:** Protects the O2 investment; ensures P3 never stalls on the first screen.
**Effort:** S · **Priority:** Should

#### B7 — Richer Spectating
**Proposal:** A "Live now" view listing active matches with names and move counts;
one click to spectate; lightweight reactions (applause/emotes) visible to players.
**Value:** Makes the product watchable (P1 can consume it too) and plants the
tournament/streaming narrative used later by B9.
**Effort:** M · **Priority:** Should

#### B8 — Match History & Shareable Results
**Proposal:** Personal history screen (past opponents, outcomes); post-match summary
card designed to be screenshotted ("Alice def. Bob · 3–2 series").
**Value:** Extends each match's life beyond the session; generates authentic
marketing material for the repository itself.
**Effort:** M · **Priority:** Should

### Could-have (growth levers)

#### B9 — Tournaments & Seasons
**Proposal:** Scheduled knockout brackets (8-player default) with a visible bracket
screen; optional monthly season leaderboard reset.
**Value:** The single most demo-worthy spectacle for events and group settings;
strong O4 talking point. Meaningful design effort (scheduling, byes, no-shows).
**Effort:** L · **Priority:** Could

#### B10 — Direct Challenges & Presence
**Proposal:** Challenge a named online player from the lobby; presence indicators
(in lobby / in match / away). Friend-style familiarity without full social graph.
**Value:** Converts the lobby from a notice board into a place people check.
**Effort:** M · **Priority:** Could

#### B11 — Expanded Game Catalog (variants)
**Proposal:** Additional modes on the same referee foundation — e.g., larger boards
(4×4/5×5), misère tic-tac-toe, Ultimate TTT — selectable at room creation.
**Value:** Replayability and an extensibility narrative. Caution: this is the
classic scope trap; only variants sharing the existing room/match model qualify.
**Effort:** M each variant · **Priority:** Could

#### B12 — Trust & Comfort Suite
**Proposal:** Quick-chat phrases and emotes instead of free-text by default; mute
controls; basic reporting; name filtering. Free-text chat remains opt-in.
**Value:** Broadens who feels welcome playing with strangers; a mature product
answer reviewers rarely expect from a small codebase.
**Effort:** M · **Priority:** Could

#### B13 — Localization & Inclusive Presentation
**Proposal:** UI strings externalized with 1–2 additional languages; marks distinguishable
beyond color; scalable text.
**Value:** Reach signal (O5) and inclusivity story; low controversy, moderate churn.
**Effort:** M · **Priority:** Could

#### B14 — Wider Availability
**Proposal:** Meet audiences where they are — playable from other desktop platforms
or a browser companion view for spectators (watch a live match from a phone).
**Value:** Multiplies potential audience (O1); strongest growth lever long-term.
Business note: significant investment; undertake only after Must/Should layers prove
their pull. Technical counterpart analyzed in the sibling document.
**Effort:** L · **Priority:** Could

#### B15 — Feedback Loop
**Proposal:** Opt-in, privacy-light feedback: an in-app "how was that?" pulse after
matches and a visible issue-suggestion channel in-app linking to the tracker.
**Value:** Demonstrates evidence-driven iteration (BA maturity signal); produces
real data for roadmap calls during the portfolio period.
**Effort:** S · **Priority:** Could

---

## 7. Prioritization Summary

| ID | Feature | Funnel stage | Primary objective | Effort | MoSCoW |
| --- | --- | --- | --- | --- | --- |
| B1 | Instant Play Demo | Attract | O1, O5 | S–M | Must |
| B2 | Solo Practice Mode | Engage | O1, O2 | M | Must |
| B3 | Quick Match | Engage | O2 | M | Must |
| B4 | Profiles & Career Stats | Retain | O3 | M | Must |
| B5 | Progression & Recognition | Retain | O3 | S–M | Should |
| B6 | Onboarding Flow | Engage | O2 | S | Should |
| B7 | Richer Spectating | Grow | O1, O5 | M | Should |
| B8 | History & Shareable Results | Retain/Grow | O3, O5 | M | Should |
| B9 | Tournaments & Seasons | Grow | O4, O5 | L | Could |
| B10 | Challenges & Presence | Retain | O3 | M | Could |
| B11 | Game Catalog Variants | Grow | O5 | M/var | Could |
| B12 | Trust & Comfort Suite | Engage | O1, O3 | M | Could |
| B13 | Localization & Inclusivity | Attract | O5 | M | Could |
| B14 | Wider Availability | Attract | O1 | L | Could |
| B15 | Feedback Loop | All | O4 | S | Could |

Sequencing logic: **Must items unblock the funnel; Should items deepen it; Could
items are selected per opportunity** (career fair → B9; web-audience push → B14).

---

## 8. Roadmap (business view)

| Wave | Theme | Contents | Exit criteria |
| --- | --- | --- | --- |
| 1 | "Anyone can play" | B1, B2, B3, B6 | A stranger goes from landing to enjoying a match unaided, alone. |
| 2 | "It remembers me" | B4, B5, B8, B15 | Second visits feel personal; results accumulate visibly. |
| 3 | "It's a place" | B7, B10, B12 | Lobby shows life: presence, live matches, safe interactions. |
| 4 | "It's an event" | B9 + selective B11/B13/B14 | Spectacle and reach, chosen by upcoming opportunities. |

Each wave is independently shippable and tells a complete story — matching how a
portfolio is actually consumed (in slices, under time pressure).

---

## 9. Success Metrics

| Metric | Baseline today | Target after Wave 1–2 |
| --- | --- | --- |
| Visitors reaching a completed match or watched match | ~0% (setup required) | Majority of visitors who try |
| Time from launch to first move | Requires 2 humans + server | Under 30 seconds solo |
| Solo-playable sessions | 0% | 100% of sessions possible alone |
| Persistent identity across sessions | None | Standard path |
| Distinct features citable in interviews | ~3 (rooms, grace, rematch) | 10+ |

Measurement is intentionally lightweight (B15): observation notes, opt-in pulses,
and issue-tracker signals — no surveillance-grade analytics for a portfolio app.

---

## 10. Risks & Assumptions

- **Assumption:** primary consumption is remote review of the repository, punctuated
  by occasional live demos. If live demos dominate, promote B9 earlier.
- **Risk: scope dilution.** Fifteen features would bury a small codebase. Mitigation:
  waves ship as slices; Could-items stay optional by design.
- **Risk: retention features feel bolted on.** Profiles/progression must serve the
  arena fantasy, not imitate mobile-game metrics theater. Keep rewards intrinsic.
- **Risk: variants/catalog sprawl (B11).** Each new mode must reuse the existing
  match lifecycle untouched; otherwise decline it.
- **Dependency:** several features (persistence-backed profiles, wider availability)
  have engineering prerequisites tracked in `business-technical-analysis.md` (F8, F12);
  business sequencing here assumes those land in parallel where noted.

---

## 11. Explicit Non-Goals (recommended restraint)

- **Monetization** (ads, purchases, premium rooms) — wrong context entirely; would poison the portfolio narrative.
- **Full accounts/email/SSO registration** — friction without audience need; lightweight profiles suffice.
- **Open free-text chat with strangers by default** — moderation burden outweighs value at this scale (see B12's guarded alternative).
- **Esports-grade anti-cheat/ranking mathematics** — a clean skill indicator beats a pretend Elo system nobody audits.
- **Mobile native client** — spectator web view (B14) captures the reach benefit at a fraction of the cost.
