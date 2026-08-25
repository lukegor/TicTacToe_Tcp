# WPF UI Testing Strategy — Decision Rule

Use this rule to decide how any WPF project should test its UI layer. It is
deliberately mechanical: answer five questions about the project, score them,
and let the total pick the strategy. Re-score whenever the answers change.

---

## The two strategies

| | Strategy A — Seams / headless | Strategy B — STA-driven real windows |
| --- | --- | --- |
| How it works | Every UI interaction (message boxes, dialogs, routers) sits behind an interface; tests substitute fakes and exercise logic headlessly. Views are kept "humble". | Real windows are instantiated on STA threads (`[WpfFact]`); tests press buttons via AutomationPeers and assert user-visible state (text, lists, enablement, rendered marks). |
| Verifies | Logic, with UI removed from the equation | The view itself: bindings, wiring, enable/disable rules, visual states |
| Characteristic blind spot | The view shell — binding typos, event wiring, visual states fail silently | Coupling to control names; modal dialogs (`ShowDialog`) block the dispatcher |
| Runs on | Any OS/agent, fully parallel | Windows agents with an STA thread |

## The five questions

Score each **0–5** about *this* project, then sum each column's favored side.

| # | Question | High score favors |
| --- | --- | --- |
| 1 | Does correctness live inside the screens themselves (rendered state, which controls are enabled, visual feedback)? | B |
| 2 | Does the app use many pop-up dialogs / message boxes (`ShowDialog`)? | A |
| 3 | Must the test suite run on Linux, containers, or desktop-less build agents? | A |
| 4 | Do many people edit XAML frequently enough that renamed controls would constantly break tests? | A |
| 5 | Do data bindings, styles, or templates carry behavior that fails silently when wrong? | B |

## Decision

- Higher total wins.
- Tie-breaker: choose the lightest mechanism that can actually observe the
  behaviors you care about, at the layer where they live.
- On WPF specifically, remember: **binding-path errors are invisible to the
  compiler.** Only real-view construction observes them; if you pick A, accept
  that risk knowingly (or mitigate with off-screen view construction plus a
  data-binding trace listener asserted empty).

## Discipline requirements for B (if chosen)

- Every constructed window passes through a headless-preparation helper
  (off-screen position, no activation, no taskbar entry) so test runs never
  show windows on a developer's machine.
- Button presses go through AutomationPeers, never direct private-handler calls;
  pressing a disabled button is expected to fail (`TryPress` semantics).
- No sleeps: async UI work drains through a dispatcher-flush helper.
- Modals are never driven in-process — extract each behind an interface seam
  with a recording fake.

## Validation

Applying this rule to two sibling WPF repositories produced opposite, correct
answers:

- A tic-tac-toe client/server app whose correctness lives in board rendering,
  cell enablement, and rematch states → scored **B ≈ 18 vs A ≈ 7** → B adopted
  (38 view tests, ~92–100% coverage on all windows).
- An image-tooling app whose correctness lives in domain operations, dense with
  message boxes, file dialogs, and tool windows → scored **A ≈ 17 vs B ≈ 5** →
  A adopted (seams faked, `[StaFact]` used only to construct STA-typed types).

## Re-score triggers

Re-run the five questions when: window count roughly doubles; a second modal
appears; a Linux/shared-agent CI gate becomes mandatory; control renames break
more than a couple of view tests per feature; or binding/style density grows
markedly.
