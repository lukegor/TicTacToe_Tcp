# UI Testing Strategy — Seams vs. STA-Driven Windows

| | |
| --- | --- |
| Date | 2026-08-25 |
| Status | Adopted — documents the reasoning behind the current test architecture |
| Scope | Which WPF testing strategy this repository uses and why; when that choice should be revisited |
| Related | `docs/superpowers/specs/2026-08-25-r2-coverage-ui-tests-design.md`, backlog items T5/T6 |

---

## 1. The Question

Two WPF repos, two strategies:

> **A** — "All UI interaction sits behind seams (`IMessageService`,
> `IOperationDialogRouter`) faked in tests."

> **B** — "Tests are the UI driver — real windows instantiated on STA threads
> is the feature."

Both apps are WPF. Are both equally valid? If one solution is better, which — and
how would we determine that rather than argue taste?

## 2. What Each Strategy Actually Is

### A — Interaction seams / humble view

Every point where the UI reaches out to the world (message boxes, dialogs,
routers, services) is extracted behind an interface. Tests substitute fakes and
exercise logic headlessly. Often paired with MVVM/presenter extraction so the
view becomes a trivial "humble object" with no logic worth testing.

- **Optimizes:** logic verification with UI removed from the equation.
- **Failure mode:** mock drift plus an *untested humble view* — the exact place
  WPF defects live (`IsEnabled` logic, event wiring, visual states, binding
  mistakes). Interface ceremony grows linearly with interaction count.

### B — Tests as the UI driver (this repo)

Real windows are instantiated in-process on STA threads (`[WpfFact]`);
interactions happen at user level (`ButtonAutomationPeer.Invoke()`); assertions
target user-visible state only (rendered text, list contents, `IsEnabled`,
cell marks, envelopes captured on fake transports).

- **Optimizes:** verification of the *actual artifact* — bindings, handlers,
  enable/disable rules, rendered visuals.
- **Failure mode:** coupling to control names (breaks on renames); modal dialogs
  are genuinely hard in-process (`ShowDialog` blocks the dispatcher pump);
  Windows-only execution; requires threading discipline.

## 3. Side-by-Side

| Dimension | A — seams | B — STA driver |
| --- | --- | --- |
| Verifies | Logic, headless | The view itself |
| Untested residue | The humble view shell | Very little of the view; some flow orchestration |
| Scales with | Screen/flow count — linear, predictable ceremony | Window count — acceptable, but rename churn grows faster |
| Modals (`ShowDialog`) | Trivially fakeable — the reason these seams get invented | Genuinely hard in-process |
| CI topology | Any OS/agent, parallel | Windows agents, sequential-per-thread |
| Adoption cost on an existing code-behind app | Rewrite every window | Near zero |
| Rename churn cost | None (fakes don't know control names) | One test edit per renamed control |

## 4. The Decision Rule

Not taste — five falsifiable criteria, scored 0–5 per repo; higher total wins:

| # | Criterion | High value favors |
| --- | --- | --- |
| 1 | **Behavior locus** — how much correctness lives *inside* the view (enablement, visuals, wiring) vs. in services/flows? | B |
| 2 | **Modal/dialog density** — many `ShowDialog` interactions? | A |
| 3 | **CI topology** — must run on Linux/shared agents without desktop sessions? | A |
| 4 | **Rename-churn tolerance** — many contributors touching XAML regularly? | A |
| 5 | **Binding/style density** — do styles, triggers, data-binding carry correctness? | B |

The frameworks' tie-breaker: choose the lightest mechanism that tests the
behavior you care about, at the layer where the behavior lives. If behavior
lives in logic, removing UI wins. If behavior lives *in* the view, driving the
real view wins. Modality forces seams regardless of anything else.

## 5. Applied to This Repository

| # | Criterion | Evidence | Score A | Score B |
| --- | --- | --- | --- | --- |
| 1 | Behavior locus | Board marks, winning-line highlight, rematch tri-state button, reconnect banner, cell enablement **are** the product | 1 | 5 |
| 2 | Modal density | Effectively one modal (crash box) — already seamed behind `ICrashReporter` | 2 | 2 |
| 3 | CI topology | Solo portfolio repo; Windows runner accepted deliberately; Core suite stays portable `net10.0` | 2 | 3 |
| 4 | Churn tolerance | Solo developer, stable XAML surface | 1 | 4 |
| 5 | Binding/style density | Light markup, code-behind-driven state; driven assertions catch real regressions | 1 | 4 |
| | **Total** | | **7** | **18** |

**Verdict: B for this repository.** Lower adoption cost by roughly an order of
magnitude (two delegate probes vs. a five-window rewrite), aimed exactly at the
risk surface the app actually has.

Two honest caveats recorded with the verdict:

1. **A is not wrong in general.** Its technique was already applied here where
   it earns its keep: the single true modal sits behind an interface
   (`ICrashReporter`) faked in tests. The disagreement is only about doing that
   to *every* interaction preemptively.
2. **B imposes discipline that A gets for free.** Real windows can escape onto
   the screen. During rollout, an audit found exactly one leak — the
   returned-to-lobby auto-show path flashed a real window during test runs —
   fixed by funneling every constructed window through `HeadlessWindow.Prepare`
   (off-screen at −32000, `ShowActivated=false`, `ShowInTaskbar=false`), so even
   genuine `Show()` calls execute their full behavior path invisibly. That
   helper is now mandatory at every construction site; production code is
   untouched by it.

## 6. What This Repo Actually Implements: All Three Layers

The either/or framing dissolves once the layers are named. Each layer of the
system is tested with the lightest mechanism that reaches its behavior:

| Layer | Mechanism | Suite |
| --- | --- | --- |
| Engine (rules, rooms, protocol, transports) | Headless unit/integration tests, hand-rolled transport fakes | `Client-Server-App.Tests` (`net10.0`, runs anywhere) |
| Modality (the crash dialog) | Interface seam (`ICrashReporter`) with recording fake | same suite |
| View behavior | STA-driven real windows, AutomationPeer presses, visible-state assertions | `Client-Server-App.UiTests` (`net10.0-windows`) |

So the practical answer to "A or B?" in this codebase is: **A where A pays
(modality), B everywhere else** — and the seam list stays short because the
app's interaction model is currently seam-free apart from the crash dialog.

## 7. Flip Triggers — When to Move Toward A

Revisit this document when any of these fire:

1. **A second modal appears** — extract its router seam the way `ICrashReporter`
   was extracted; do not attempt to drive `ShowDialog` in-process.
2. **Window count roughly doubles** — presenter extraction starts paying for
   itself; keep thin B smoke-tests per window.
3. **A Linux/shared-agent CI gate becomes mandatory** — Core/UI split already
   isolates the damage; flows may need headless seams.
4. **Rename churn breaks ≥ a couple of UI tests per feature** — the coupling
   tax has arrived; extract presenters for the hot windows.

Until one fires, switching strategies is cost without payoff.

## 8. Guard Rails Keeping B Honest

- **Headless construction is mandatory**: all windows pass through
  `HeadlessWindow.Prepare` (off-screen, non-activating, taskbar-hidden).
- **Presses go through AutomationPeers** (`IInvokeProvider.Invoke()`), never
  direct private-handler invocation — assertions stay behavioral.
- **Disabled buttons are asserted via `TryPress`**, which mirrors what a real
  user experiences (UIA refuses disabled elements) rather than forcing clicks.
- **No sleeps anywhere**; async UI work drains via `TestDispatcher.FlushAsync()`.
- Candidate future guard (backlog T5 territory): a self-scan test asserting no
  method in the UiTests assembly calls `Window.Show`/`ShowDialog` directly,
  making any future UI-popping test a red build instead of a stray window.

## 9. Summary

Both strategies are valid answers — to different questions. A answers "how do I
test logic that a UI would otherwise block?"; B answers "how do I prove the view
behaves?". This repository's correctness lives measurably inside its views, its
modal surface is already seamed, and its scale keeps churn cheap — so the
STA-driven strategy is the better fit today, layered on top of headless engine
tests and interface seams, with explicit triggers set for when that balance
should shift.
