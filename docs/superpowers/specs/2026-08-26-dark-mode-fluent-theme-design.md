# Design: Light/Dark Theme Switching via First-Party Fluent Theme

Date: 2026-08-26
Status: Approved (design), pending implementation plan

## Goal

Add dark mode to the WPF client with a light/dark/system switch, accepting the
Windows 11 Fluent restyle of both modes. Session-only: no persistence.

## Decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Visual language | First-party Fluent theme (ships in net10.0-windows as `PresentationFramework.Fluent`) |
| Light mode | Accepts restyle from classic Aero2 to Fluent |
| Switching UX | Default follows Windows setting; manual System/Light/Dark override on MainWindow |
| Persistence | None — every launch starts at `System` |
| Fallback | lepoco/wpfui library, only if the spike (below) fails |

Rejected alternatives: third-party WPF UI library as primary (unneeded
dependency if first-party works); hand-rolled Aero2 dark dictionaries
(re-templating standard controls is high effort/high maintenance).

## Architecture

No new services, abstractions, or ViewModels. The feature is:

1. **App.xaml** — `ThemeMode="System"` attribute. XAML attribute assignment is
   not experimental-gated; it boots the app following the Windows setting and
   applies Fluent (with dark titlebar/backdrop) to every window.
2. **MainWindow.xaml** — compact ComboBox (System / Light / Dark, default
   System) docked top-right; the existing "Open Connection" button stays
   centered. Code-behind handler assigns `Application.Current.ThemeMode`
   (System | Light | Dark). One line; a ThemeService would be ceremony.
3. **Client-Server-App.csproj** — `<NoWarn>$(NoWarn);WPF0001</NoWarn>` with a
   comment naming the experimental API. `TreatWarningsAsErrors` stays on for
   everything else.

## Switching flow

ComboBox selection → code-behind sets `Application.Current.ThemeMode` → WPF
swaps the Fluent resource dictionaries application-wide → all already-open
windows (Connection, Lobby, Game, Server) re-theme live, including dark
titlebars. No window lifetime changes, no restart.

## Hardcoded color removal

Both existing hardcoded colors become theme-reactive `DynamicResource` Fluent
tokens so live switching re-colors them:

| Location | Today | Becomes |
|---|---|---|
| LobbyWindow room label | `Foreground="Gray"` | `TextFillColorSecondaryBrush` |
| Game cell normal background | `Background="White"` | Fluent default button background (custom cell style removed — a custom style would fall back to Aero2 templates inside a Fluent window) |
| Game cell win highlight | `LightGoldenrodYellow` style trigger | Overlay `Border` with `SystemFillColorCautionBackgroundBrush`, visibility bound to `IsHighlighted` |

(Exact token keys pinned against the Fluent theme sources in dotnet/wpf;
present in Light.xaml, Dark.xaml, and HC.xaml.)

`Themes/Shared.xaml` keeps its current keyed styles; no mode-specific
dictionaries of our own.

Test impact: `WinningLine_HighlightsCells_WhiteElsewhere` compares literal
`Brushes.White` / `Brushes.LightGoldenrodYellow`; it will resolve the same
resources instead of literal brushes.

## Risk gate (spike)

dotnet/wpf#11275 reports broken dark rendering on net10.0-windows
(window always black; .NET 9 was fine), status unresolved.

Implementation step 1 is a spike: set `ThemeMode="Dark"`, launch, inspect all
five windows.

- Pass → proceed with the design as written.
- Fail → try the non-experimental variant (merge `Fluent.Dark.xaml` /
  `Fluent.Light.xaml` dictionaries and swap them at runtime) on .NET 10.
- Still fails → stop and present the WPF UI library fallback to the user
  before switching approaches.

Mica window backdrop stays on by default; the
`Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop` app-context
switch is the documented tunable if it misbehaves.

## Testing & verification

- The 40 existing UI tests are unaffected at runtime: they construct windows
  without an `Application` instance, so application-level theming never
  engages. Behavior/logic coverage stays intact.
- No new automated tests: theming is Application-scoped and the UI test host
  intentionally has no `Application`.
- Manual verification checklist:
  - Each of the 5 windows in System, Light, and Dark.
  - Live switch while Connection → Lobby → Game are open.
  - Windows-set dark mode is followed on a fresh launch.
  - Win11 dark titlebar renders; accent color respected.
  - Board cells and win highlight readable in both modes.
- Build stays warnings-as-errors with only WPF0001 suppressed.

## Out of scope

- Settings persistence (explicitly declined).
- Custom accent color picker (Windows accent is respected automatically).
- High-contrast mode beyond what Fluent applies natively.
