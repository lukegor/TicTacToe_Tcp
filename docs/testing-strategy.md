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
- Committed guard (implementation next, see §12): a self-scan test asserting
  no method in the UiTests assembly calls `Window.Show`/`ShowDialog` directly,
  making any future UI-popping test a red build instead of a stray window.


## 9. Rebuttal Round — Adjudication (2026-08-25, later the same day)

The sibling repository responded with a full counter-proposal: "as the default
backbone, seam-based headless testing wins clearly; real-window STA testing is
legitimate only as a thin top layer." Its strongest pillar — that WPF's
`x:DataType` compiled bindings turn silent binding-wiring bugs into compile
errors, shrinking B's unique advantage to 1–5% of the suite — was fact-checked
before adjudicating.

**Finding: the factual pillar is false for WPF.** `x:DataType`/`{x:Bind}`
compiled bindings are .NET MAUI and WinUI features. WPF has neither; the only
WPF option is a third-party library (`CompiledBindings.WPF`) with its own
markup namespace — i.e., adopting the recommendation means adding an external
dependency and non-standard XAML to escape exactly two runtime-resolved
binding paths (`Name`, `Label` in the lobby item template), both of which are
already under behavioral test.

### Point-by-point

| Their claim | Adjudication | Basis |
| --- | --- | --- |
| Test pyramid: real-window tests must be a thin top layer | **Partially accepted as general prior; rejected as verdict here** — this repo's UI layer sits on an existing 84-test headless backbone with engine logic at ~95%+ headless coverage. The pyramid is intact; 38 view tests map one-to-one to spec'd behaviors | Section 6 |
| 10–100× slower per test; dispatcher flake risk | **Empirically false at this scale**: full suite (122 tests incl. all UI) runs in ~6 s wall; zero flakes across every run in this working session; no focus theft since `HeadlessWindow` | measured, §8 |
| `ShowDialog` hangs runners | True generally; **zero modals here** — the single dialog is seamed behind `ICrashReporter` | §5 criterion 2 |
| B "tolerates logic-in-views" | **Inverted for this repo**: logic lives in Core (pure rules engine + services, ~95% covered headless); code-behind holds only view behavior | architecture audit |
| Compiled bindings close A's blindness gap | **False on WPF** — MAUI/WinUI-only feature; third-party port would add a dependency to guard two already-tested paths | NuGet/MS docs check |
| Harness-owned lifecycle (`ShowForTestAsync`, compiler-enforced suppression) | **Good idea, parked**: our structural equivalents exist today (opener probes suppress `Show`; `HeadlessWindow.Prepare` mandatory at construction). A call-level self-scan guard remains parked under T5 | §8 |
| "Irony: B makes windows pop up on dev machines" | **Accepted hit** — one real leak was found by audit and fixed same-day via off-screen construction | §5 caveat 2 |
| Settle with data over a month (escaped defects, wall-time, flake rate) | **Adopted.** Baseline recorded below; re-evaluate on the §7 triggers or the metrics | — |

### Baseline metrics for the proposed re-evaluation

- Suite wall-time: **~6 s total** (84 headless + 38 view tests)
- Flake rate: **0** observed across all session runs
- Escaped UI/harness incidents: **1** (the returned-to-lobby window flash),
  found by audit, fixed same day via `HeadlessWindow.Prepare`

### Position after rebuttal

Unchanged in verdict, refined in commitments:

1. Keep STA-driven windows as the view-behavior layer **on top of** the
   headless backbone — not instead of it. The pyramid framing is accepted;
   JSharp's error is assuming the base is missing here.
2. Reject compiled-bindings adoption on WPF (feature does not exist; two
   bound paths are already behavior-tested).
3. Park the call-level enforcement guard under T5.
4. Hold both repos to the three metrics above for a month before any further
   strategy debate.

## 11. Summary

Both strategies are valid answers — to different questions. A answers "how do I
test logic that a UI would otherwise block?"; B answers "how do I prove the view
behaves?". This repository's correctness lives measurably inside its views, its
modal surface is already seamed, and its scale keeps churn cheap — so the
STA-driven strategy is the better fit today, layered on top of headless engine
tests and interface seams, with explicit triggers set for when that balance
should shift.

## 12. Round 3 — Convergence (closing position)

JSharp's response accepted the behavior-locus criterion as the missing
independent variable, conceded that its round-2 "flake/harness cost" critique
was overstated given this repo's harness discipline, and confirmed the pyramid
argument targeted a position we do not hold. Applying our five-criterion rubric
to *its own* repository produced A ≈ 17 / B ≈ 5: seams win there decisively,
because JSharp's correctness lives in domain logic and its UI is dense with
modality. Same rule, two repositories, two correct answers — nobody was wrong
about their own codebase; the only error was universalizing either choice.

**Adopted as the shared standard:** the five-criterion scoring table (§4) is now
the inter-repository decision procedure for WPF UI test strategy. Re-score on
any material change to behavior locus, modality density, CI topology, team
churn, or binding/style density.

### Per-repo closing scores

| Repo | A (seams/headless) | B (STA-driven windows) | Winner | Backbone reality |
| --- | --- | --- | --- | --- |
| Client-Server-App (this repo) | 7 | 18 | B — view layer atop an 84-test headless base | Pyramid intact |
| JSharp | ~17 | ~5 | A — seams as backbone, `[StaFact]` only for STA-typed construction | Pyramid intact |

### Concessions ledger

| Side | Conceded |
| --- | --- |
| JSharp → us | Round-2 overgeneralization; harness discipline neutralizes most structural B-costs; no pyramid inversion here; rubric adopted as shared standard |
| Us → JSharp | The pyramid instinct is the correct general default when no headless base exists; "choose cheap failure modes" adopted verbatim; month-of-data metrics accepted |

### Corrections to JSharp's surviving items

1. **Compiled bindings are not "unaddressed"** — §9 above fact-checks them, and
   the finding is symmetrical: `x:DataType`/`{x:Bind}` compiled bindings do not
   exist in WPF (MAUI/WinUI-only; WPF's sole option is the third-party
   `CompiledBindings.WPF`). JSharp's action item "adopt compiled bindings" is
   therefore unavailable on its own stack too. The underlying concern (silent
   binding-path bugs) stands for both repos; on WPF the mitigations are the
   behavioral tests described in §6 or the third-party library, weighed
   against dependency policy.
2. **Call-level enforcement**: promoted from parked to committed. Next change
   touching `tests/Client-Server-App.UiTests` adds the self-scan guard
   (`System.Reflection.Metadata`, zero dependencies) asserting no method in the
   UiTests assembly calls `Window.Show`/`ShowDialog`. Symmetric tripwire on the
   JSharp side remains its type-dependency rule.

### Metric commitments (re-evaluate after one month)

| Metric | Baseline (2026-08-25) | Alarm threshold |
| --- | --- | --- |
| Full-suite wall time | ~6 s (122 tests) | > 30 s sustained |
| Flake rate | 0 observed across all session runs | any reproducible flake |
| Escaped UI/harness incidents | 1 (window flash; fixed same day) | ≥ 1 new per month |

Either repo may call re-scoring at any time; the rubric, not preference,
decides.

## 13. Round 3 Receipt — Empirical Confirmation & Final Positions

JSharp independently reproduced the fact-check on its own target framework
(`net10.0-windows`, current .NET): two scratch WPF projects differing only in a
binding path under `x:DataType` both fail to build with

```
error MC3073: The attribute 'DataType' does not exist in XML namespace
'http://schemas.microsoft.com/winfx/2006/xaml'
```

The author formally withdrew the load-bearing pillar ("compiled bindings close
A's blindness gap") as a confabulation — MAUI/WinUI knowledge bleeding across
frameworks. Both repositories have now verified the same fact through different
routes (docs research here; build experiment there). There is no residual
disagreement about reality.

### What the confirmation changes

- On WPF, **strategy B holds a unique, irreplaceable coverage**: binding-path
  wiring errors are invisible to the compiler and unreachable by seam-based
  tests. Round 2's "B shrinks to 1–5% of the suite" arithmetic collapses — that
  residue is precisely the part only B can see.
- Per-repository verdicts are **unchanged**: applying the rubric to JSharp still
  selects A (correctness locus in domain operations; high modality density).
- New shared insight: a WPF repository choosing pure-A must do so *knowingly*,
  accepting silent-binding risk — or mitigate via headless view-construction
  (views instantiated, never shown) under a
  `PresentationTraceSources.DataBindingSource` trace listener asserted empty.

### Adopted follow-up for this repo

Queued with the next change touching `tests/Client-Server-App.UiTests`
(alongside the committed T5 self-scan): register a data-binding trace listener
in the UI-test fixture and assert zero binding errors while every window
constructs and renders — turning B into an automatic net for *all* future
binding paths, not just the two currently asserted.

### Final positions

| Repo | Strategy | Status |
| --- | --- | --- |
| Client-Server-App | Headless engine suite + seamed modal + STA-driven view layer; binding-error trace listener queued | Closed |
| JSharp | Seam-based headless backbone (`ArchitectureTests` type-dependency tripwire pending); B-lite binding-error evaluation for XAML-heavy windows queued | Closed |

Debate closed by convergence: same decision rule, two repositories, two correct
answers, one jointly verified fact.