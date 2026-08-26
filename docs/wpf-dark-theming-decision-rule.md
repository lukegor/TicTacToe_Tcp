# WPF Dark Theming — Decision Rule & Field Notes for AI Agents

> **Portability:** Repository-agnostic. Copy next to any WPF project that needs
> light/dark theming. Everything was verified live against `net10.0-windows`
> (SDK 10.0.x, August 2026) in a 5-window production app; Fluent token keys were
> pinned against dotnet/wpf sources at commit `48dfd1e6`. Treat behavior claims
> as version-pinned and re-verify after major .NET upgrades.
>
> **If you are an AI agent:** read §5 (Pitfalls) *before* writing any XAML.
> Every item marked **[hit]** produced a real, wasted cycle in one theming
> session; items marked **[prevented]** were caught by research before code was
> written and are cheaper to keep than to rediscover.

Sources: learn.microsoft.com WPF Fluent/ThemeMode docs, dotnet/wpf
`using-fluent.md`, dotnet/wpf issue #11275, Fluent theme XAML sources
(`PresentationFramework.Fluent/Resources/Theme/{Light,Dark,HC}.xaml`).

---

## 1. TL;DR agent checklist

1. The deemed-good system is the **first-party Fluent theme** that ships inside
   .NET itself (`PresentationFramework.Fluent`, in-box since .NET 9). No NuGet
   packages.
2. Enable with `ThemeMode="System"` on `<Application>` (XAML attribute — does
   NOT trigger the experimental diagnostic). Follows the Windows setting.
3. Manual Light/Dark override = one ComboBox assigning
   `Application.Current.ThemeMode` (code access IS experimental → suppress
   `WPF0001` project-wide, the only allowed suppression).
4. **Spike first**: force `ThemeMode="Dark"`, launch, human-verify rendering
   (see §6 P1) before committing to the approach.
5. Every custom keyed control style MUST declare
   `BasedOn="{StaticResource {x:Type ControlType}}"` or it silently renders as
   light Aero2 inside a dark window (§5 P2) — the failure is invisible until
   dark mode exists.
6. Hardcoded colors become `DynamicResource` Fluent tokens (never
   `StaticResource`, or live switching won't re-color). Verified keys in §4.
7. Tests that assert token-driven visuals must merge the Fluent dictionary
   explicitly — the headless test host has no `Application` (§5 P5).

## 2. The decision (what was deemed good, and why)

| Option | Verdict | Reasoning |
|---|---|---|
| **First-party Fluent (`ThemeMode` / `PresentationFramework.Fluent`)** | **Adopted** | In-box (no dependency), official direction of WPF, System-follow + live switching + dark titlebars/backdrop for free, ~50 lines of app code total. |
| lepoco/wpfui library | Fallback only | Stable non-experimental API, polished — but a third-party dependency plus a pile of unused controls. Adopt only if the spike (§6 P1) fails on your target. |
| Hand-rolled Aero2 dark dictionaries | Rejected | Default WPF control templates hardcode hover/pressed colors; true dark mode means re-templating every standard control. High effort, permanent maintenance tax. |

**Hard truth to state upfront:** there is *no* way to add real dark mode while
keeping the classic Aero2 light look pixel-identical. Adding dark mode is a
restyle; get the human to accept the Fluent restyle of BOTH modes before
starting (it also changes light mode).

Decision inputs that picked column 1 here: target framework ≥ .NET 9; small
control surface (standard controls only); no pixel-preservation requirement;
`TreatWarningsAsErrors` acceptable with one scoped `NoWarn`.

## 3. Adoption procedure (verified order)

**Step 1 — Spike gate (never skip).** Set `ThemeMode="Dark"` on the
Application, build, launch, and have a human confirm: window dark (not solid
black), controls legible, dark titlebar. Then revert to the permanent setting:

```xml
<Application x:Class="ClientServer.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml"
             ThemeMode="System">
```

XAML attribute assignment does not fire the `WPF0001` experimental diagnostic;
only code access does.

**Step 2 — Manual override.** A ComboBox (System/Light/Dark, default System)
plus a one-line handler. No service abstraction, no ViewModel for this — it is
one assignment:

```csharp
private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (Application.Current is not { } app)
    {
        return;
    }

    app.ThemeMode = ThemeSelector.SelectedIndex switch
    {
        1 => ThemeMode.Light,
        2 => ThemeMode.Dark,
        _ => ThemeMode.System,
    };
}
```

csproj (the ONLY suppression this approach needs):

```xml
<!-- Application.ThemeMode / Window.ThemeMode are experimental (WPF0001). -->
<NoWarn>$(NoWarn);WPF0001</NoWarn>
```

Declare `SelectedIndex` BEFORE `SelectionChanged` in the XAML so parse-time
selection doesn't fire the handler.

**Step 3 — Token migration.** Replace every hardcoded color with
`DynamicResource` Fluent tokens (§4). All open windows re-theme live on mode
switch, including dark titlebars; `DynamicResource` is what makes custom
colors participate in that switch.

## 4. Verified Fluent token keys (present in Light.xaml, Dark.xaml, HC.xaml)

| Token | Use | Notes |
|---|---|---|
| `TextFillColorSecondaryBrush` | Secondary labels (was `Gray`) | |
| `SystemFillColorCautionBackgroundBrush` | Soft yellow surface (was `LightGoldenrodYellow`) | `#FFF4CE` light / `#433519` dark |
| `ApplicationBackgroundBrush` | Window-background-matching surfaces | |
| `SystemFillColorAttentionBrush` | "Attention" | **Trap:** it is ACCENT-colored in WPF Fluent, not yellow like WinUI — don't reach for it expecting yellow |

Pin keys against the Fluent theme sources (dotnet/wpf repo, path
`src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Resources/Theme/`)
— do not guess from WinUI documentation; the WPF port diverges (see
`SystemFillColorAttentionBrush` above).

## 5. Pitfalls catalogue

Format: **Symptom → Root cause → Rule.** **[hit]** = bit a real agent;
**[prevented]** = caught by research before code was written.

### P1 — [prevented] .NET 10 dark mode can render broken/black
Symptom: with dark mode forced, windows render solid black or controls
misbehave (dotnet/wpf#11275, reported on net10.0-windows; net9 was fine).
Root cause: Fluent-on-.NET-10 regression, resolution tracked upstream.
Rule: the spike (Step 1) is a hard gate on every project and every major .NET
upgrade. Verified passing on net10.0-windows SDK 10.0.x, August 2026 — but
re-verify after SDK upgrades. If it fails: try the non-experimental variant
(merge `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml`
instead of `ThemeMode`), then escalate to the fallback library.

### P2 — [hit] Custom styles render as light Aero2 inside a dark window
Symptom: after enabling dark mode, specific textboxes/buttons stay glaringly
light ("textboxes and tiles contrast too much") while the rest of the window
is correctly dark.
Root cause: assigning ANY custom `Style` to a control replaces the implicit
style. Fluent is loaded as *application resources*, not as a theme assembly —
so the template fallback goes to the actual THEME style (Aero2, light), not to
Fluent. The bug is invisible until dark mode exists.
Rule: every keyed control style declares
`BasedOn="{StaticResource {x:Type ControlType}}"`. This resolves to the Fluent
implicit style in the app and to the theme style in headless test hosts (no
crash there — the type-key lookup falls through to the theme dictionary).
Prefer not restyling standard controls at all (see P3).

### P3 — [prevented] Background-trigger styling fights the theme
Symptom (predicted): a cell button styled via `Background` setters + triggers
needs a custom style → triggers P2; adding `BasedOn` fixes templates but the
hardcoded `White` still fights dark mode.
Root cause: same as P2, plus hardcoded colors.
Rule: for highlight/state visuals, prefer an overlay element
(`Border`, `IsHitTestVisible="False"`, `Visibility` bound through
`BooleanToVisibilityConverter`) and let the control keep its native implicit
style entirely. Zero custom style = zero fallback problem.

### P4 — [prevented] Experimental API friction
Symptom (predicted): build fails with `WPF0001` when code touches
`Application.ThemeMode`.
Root cause: `ThemeMode` APIs are marked `[Experimental]`; XAML attribute set
is exempt (BAML path), code access is not.
Rule: one project-wide `<NoWarn>$(NoWarn);WPF0001</NoWarn>` with a comment.
Keep `TreatWarningsAsErrors` for everything else. Re-check the API surface on
each major .NET upgrade — "experimental" means it can change.

### P5 — [hit] Token assertions fail in the headless test host
Symptom: UI test asserts a token-driven brush; the property is null/unset.
Root cause: test windows are constructed without an `Application`, so
app-level Fluent dictionaries (and their tokens) don't exist;
`DynamicResource` silently leaves the property unset.
Rule: tests that assert token-driven visuals merge the dictionary explicitly
first:
`window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml") });`
Behavior-only tests need nothing — they never touch theming.

### P6 — [hit] Visual-tree searches over-count Fluent template parts
Symptom: test expects 9 `Border` elements (one overlay per board cell); finds
19.
Root cause: Fluent control templates contain their own `Border` elements;
generic visual-tree searches match template internals too.
Rule: locate custom overlays structurally (e.g., sibling of the known control:
`cell.Parent is Grid g ? g.Children.OfType<Border>().First()`), never by type
alone.

### P7 — [prevented] Mica backdrop surprises
Symptom (predicted): windows get the Mica/backdrop treatment automatically
under Fluent; some environments or tastes reject it.
Root cause: Fluent applies a backdrop to windows by design.
Rule: know the escape hatch — app-context switch
`Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop`. Leave
default (on) unless a human objects.

### P8 — [prevented] Semantic traps in mode/window precedence
- High-contrast Windows themes override everything: the HC Fluent dictionary
  applies and light/dark distinctions vanish. Don't "fix" this; it's correct.
- `Window.ThemeMode=None` still inherits Fluent when the Application's
  ThemeMode is not None. Per-window opt-out is not possible while the app is
  themed.
- `ThemeMode` values are only the four statics (`Light/Dark/System/None`);
  it's a struct, not an enum — no parsing from config strings for free.

## 6. Verification gates (run all after any theming change)

1. **Build:** 0 warnings with only `WPF0001` suppressed.
2. **Full test suite:** theming must not move behavior tests (they run without
   an `Application`); only token-asserting tests may change, deliberately.
3. **Human visual gate, per window × per mode:** dark titlebar, legible
   controls, no light islands (the P2 symptom), highlight/readability of
   custom-colored elements.
4. **Live-switch gate:** with every window of the app open, toggle
   Light → Dark → System and confirm all windows re-theme without restart.

## 7. Adoption checklist for a new WPF repository

- [ ] Human accepted the Fluent restyle of BOTH modes (no Aero2 preservation).
- [ ] Spike executed and human-passed on the exact target TFM/SDK (P1).
- [ ] `ThemeMode="System"` on Application; selector + handler + `WPF0001`
      suppression in place; no persistence unless asked.
- [ ] Zero hardcoded colors in XAML; zero keyed control styles without
      `BasedOn` (P2); highlight visuals via overlays, not style triggers (P3).
- [ ] Token keys pinned against Fluent sources, not WinUI docs (§4).
- [ ] Token-asserting tests merge the Fluent dictionary (P5); visual-tree
      assertions scoped structurally (P6).
- [ ] Four verification gates recorded.

## 8. Deliberately out of scope

Custom accent-color pickers (Windows accent is respected automatically);
per-window theming (app-level only — per-window opt-out is impossible while
the app is themed, P8); high-contrast customization (native HC handling);
settings persistence (add only if a human asks; it is ~30 lines and not
theming-specific).
