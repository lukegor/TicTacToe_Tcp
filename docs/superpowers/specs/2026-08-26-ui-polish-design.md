# Design: UI Polish — Fluent Minimal, Content-First

Date: 2026-08-26
Status: Approved

## Goal

Make the client visibly nicer to the human eye using the Fluent design language
already in place — grouping, typography hierarchy, spacing rhythm, one primary
action per flow — without touching behavior, names, bindings, or commands.

## Design language

| Dimension | Standard |
|---|---|
| Color | Fluent semantic tokens only; zero hardcoded hex (inherits `wpf-dark-theming-decision-rule.md`) |
| Typography | Segoe UI scale: page title 20 SemiBold · card header 14 SemiBold · body 14 · caption 12 secondary · board marks 42 SemiBold |
| Spacing | 4/8 rhythm: outer margin 20, card padding 16, element gaps 8–12 |
| Structure | Rounded cards (CornerRadius 8, `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush`) |
| Primary action | One accent-filled CTA per flow (`AccentFillColorDefaultBrush` + `TextOnAccentFillColorPrimaryBrush`) |
| Motion | None custom — Fluent built-ins only |
| Empty states | Centered secondary captions where lists can be empty |

Pinned tokens (dotnet/wpf `48dfd1e6`, Light/Dark/HC): `CardBackgroundFillColorDefaultBrush`,
`CardStrokeColorDefaultBrush`, `LayerFillColorDefaultBrush`,
`AccentFillColorDefaultBrush`, `TextOnAccentFillColorPrimaryBrush`,
`TextFillColorSecondaryBrush`.

## Shared.xaml additions

`PageTitle` (20/SemiBold), `PageSubtitle` (12/secondary), `SectionCard`
(Border: card fill + stroke + radius 8 + padding 16), `PrimaryButton`
(BasedOn Button: accent fill, on-accent text, SemiBold). All keyed styles
`BasedOn` the implicit type style (theming playbook P2 rule).

## Per-window changes

- **MainWindow** — launcher: centered title (28 SemiBold), subtitle caption,
  single accent Open Connection button; theme selector stays top-right;
  window 800×450 → 560×360.
- **ConnectionWindow** — page title; two `SectionCard`s side by side
  ("Join a game" / "Host a new game"), inputs stretch full card width
  (`Width=200` dropped from `FormField`), accent CTA per card; log spans below;
  window 560×480 → 600×540.
- **LobbyWindow** — roomier create row; padded room rows via
  `ItemContainerStyle`; centered empty-state caption driven purely in XAML
  (`Items.Count` trigger on `RoomsList`).
- **GameWindow** — status banner (16 SemiBold, centered); board wrapped in a
  card, centered `MaxWidth=336`; marks 42 SemiBold in theme text color (X/O
  differ by glyph — `color-not-only`); Rematch becomes the accent CTA, Leave
  stays secondary.
- **ServerWindow** — column headers added (Room / Players / Spectators);
  roomier rows; empty-state caption. Header lives OUTSIDE `RoomsPanel`; item
  root remains a `Grid` with exactly 3 direct `TextBlock` children —
  ServerWindowTests `RowTexts` depends on both.

## Guardrails

- Visual-only diff: every `x:Name`, binding, command, and event preserved.
- Known test tripwires: `ServerWindowTests.RowTexts` (grid/textblock shape),
  `LobbyWindowTests.RealizeRoomsList` (manual measure of `RoomsList`),
  `GameWindowTests.OverlayOf` (cell root must stay `Grid` parenting Button +
  overlay Border).
- Both light and dark verified; contrast guaranteed by tokens.

## Out of scope

Icon packs, custom animations, log auto-scroll (behavioral), responsive
layout work, app icon, settings persistence.
