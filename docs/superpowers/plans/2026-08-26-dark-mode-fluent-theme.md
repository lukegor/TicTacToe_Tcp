# Dark Mode (Fluent Theme) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add light/dark/system theme switching to the WPF client using the first-party .NET Fluent theme, with no persistence.

**Architecture:** `ThemeMode="System"` on the Application enables Fluent app-wide; a ComboBox on MainWindow assigns `Application.Current.ThemeMode` for the manual override; the two hardcoded colors become Fluent `DynamicResource` tokens; the game-cell highlight becomes an overlay `Border` so cells keep Fluent's native button styling.

**Tech Stack:** WPF on net10.0-windows, `PresentationFramework.Fluent` (in-box), CommunityToolkit.Mvvm (untouched), xunit.v3 + Xunit.StaFact UI tests.

**Spec:** `docs/superpowers/specs/2026-08-26-dark-mode-fluent-theme-design.md`

## Global Constraints

- Target framework: `net10.0-windows` (do not change).
- `TreatWarningsAsErrors=true` stays; the ONLY allowed suppression is `WPF0001` (experimental `ThemeMode` API), added in Task 3.
- No new NuGet packages. Fallback (only if Task 1 spike fails): lepoco/wpfui — requires explicit user approval first.
- Theme choice is session-only: no settings file, no registry, no persistence code.
- All 40 existing UI tests must stay green (the one in Task 4 is a deliberate modification).
- Full-suite validation command: `./scripts/run-tests.ps1` (plain `dotnet test` on the UI project reports zero tests — always use the script).
- Repo commit style: conventional commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`).

---

### Task 1: Spike — verify Fluent Dark renders correctly on net10.0-windows (RISK GATE)

**Files:**
- Modify: `src/Client-Server-App/App.xaml` (temporary, reverted within this task)

**Interfaces:**
- Consumes: nothing.
- Produces: go/no-go decision for the first-party Fluent approach. On "no-go", STOP and consult the user (fallback options in Step 4).

- [ ] **Step 1: Temporarily force Dark mode**

Change `App.xaml` (the file currently has no `ThemeMode` attribute):

```xml
<Application x:Class="ClientServer.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:ClientServer.App"
             StartupUri="MainWindow.xaml"
             ThemeMode="Dark">
    <Application.Resources>

    </Application.Resources>
</Application>
```

- [ ] **Step 2: Build and launch**

```bash
dotnet build Client-Server-App.slnx -c Release --nologo
Start-Process "src\Client-Server-App\bin\Release\net10.0-windows\Client-Server-App.exe"
```

- [ ] **Step 3: Human visual gate — ask the user to confirm**

The executor cannot judge visuals; ask the user to check the launched window:
window renders dark (not solid black), button/text legible, dark titlebar, no
broken control layouts. Reference: dotnet/wpf#11275 reports "always black"
windows on net10.0-windows.

- [ ] **Step 4: Decision rule**

- PASS → revert `App.xaml` to the original (no `ThemeMode` attribute) and
  proceed to Task 2. Do NOT commit the Dark state.
- FAIL → try the non-experimental variant before giving up: remove the
  `ThemeMode` attribute and instead merge
  `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml`
  into `Application.Resources.MergedDictionaries`, relaunch, re-gate. If that
  also fails → STOP, revert all changes, and present the fallback
  (lepoco/wpfui, Approach B in the spec) to the user for approval.

---

### Task 2: Enable Fluent System mode permanently

**Files:**
- Modify: `src/Client-Server-App/App.xaml`

**Interfaces:**
- Consumes: Task 1 pass.
- Produces: application-wide Fluent theming following the Windows setting; all later tasks assume it.

- [ ] **Step 1: Set ThemeMode to System**

`App.xaml` final state (this is the permanent change; the only diff vs. the
original file is the `ThemeMode` attribute):

```xml
<Application x:Class="ClientServer.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:ClientServer.App"
             StartupUri="MainWindow.xaml"
             ThemeMode="System">
    <Application.Resources>

    </Application.Resources>
</Application>
```

- [ ] **Step 2: Run full validation**

```bash
dotnet build Client-Server-App.slnx -c Release --nologo
./scripts/run-tests.ps1
```

Expect: build 0 warnings/errors, 88 core + 40 UI tests pass. UI tests do not
exercise theming (no `Application` in the test host) — they prove no
collateral damage.

- [ ] **Step 3: Commit**

```bash
git add src/Client-Server-App/App.xaml
git commit -m "feat: enable fluent theme following system mode"
```

---

### Task 3: Theme selector on MainWindow (System / Light / Dark)

**Files:**
- Modify: `src/Client-Server-App/MainWindow.xaml`
- Modify: `src/Client-Server-App/MainWindow.xaml.cs`
- Modify: `src/Client-Server-App/Client-Server-App.csproj`

**Interfaces:**
- Consumes: `Application.ThemeMode` / `ThemeMode` struct (`System.Windows`, experimental, `WPF0001`).
- Produces: working manual override; nothing downstream consumes it.

- [ ] **Step 1: Suppress WPF0001 project-wide**

In `src/Client-Server-App/Client-Server-App.csproj`, add to the existing
`<PropertyGroup>`:

```xml
    <!-- Application.ThemeMode / Window.ThemeMode are experimental (WPF0001). -->
    <NoWarn>$(NoWarn);WPF0001</NoWarn>
```

- [ ] **Step 2: Add the selector to MainWindow.xaml**

Full new content (selector is top-right; `SelectedIndex` is declared BEFORE
`SelectionChanged` so parse-time selection does not fire the handler):

```xml
<Window x:Class="ClientServer.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:ClientServer.App"
        mc:Ignorable="d"
        Title="Client-Server App" Height="450" Width="800"
        WindowStartupLocation="CenterScreen">
    <Grid>
        <Button Content="Open Connection" Width="140" Height="30"
                HorizontalAlignment="Center" VerticalAlignment="Center"
                Click="OpenConnectionButton_Click" />
        <ComboBox x:Name="ThemeSelector"
                  HorizontalAlignment="Right" VerticalAlignment="Top"
                  Margin="0,12,12,0" Width="110"
                  SelectedIndex="0"
                  SelectionChanged="ThemeSelector_SelectionChanged">
            <ComboBoxItem Content="System" />
            <ComboBoxItem Content="Light" />
            <ComboBoxItem Content="Dark" />
        </ComboBox>
    </Grid>
</Window>
```

- [ ] **Step 3: Handle selection in code-behind**

Full new content of `MainWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace ClientServer.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectionWindow connectionWindow = new();
        connectionWindow.Show();
    }

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
}
```

- [ ] **Step 4: Run full validation**

```bash
dotnet build Client-Server-App.slnx -c Release --nologo
./scripts/run-tests.ps1
```

Expect: 0 warnings (WPF0001 suppressed), all tests green.

- [ ] **Step 5: Manual check with the user**

Launch the app, open the Connection window, toggle Light → Dark → System and
confirm every open window re-themes live.

- [ ] **Step 6: Commit**

```bash
git add src/Client-Server-App/Client-Server-App.csproj src/Client-Server-App/MainWindow.xaml src/Client-Server-App/MainWindow.xaml.cs
git commit -m "feat: add system/light/dark theme selector"
```

---

### Task 4: Theme-reactive tokens (lobby label + game cell highlight overlay)

**Files:**
- Modify: `src/Client-Server-App/Themes/Shared.xaml`
- Modify: `src/Client-Server-App/LobbyWindow.xaml` (room label line)
- Modify: `src/Client-Server-App/GameWindow.xaml` (cell DataTemplate)
- Modify: `tests/Client-Server-App.UiTests/GameWindowTests.cs` (WinningLine test)

**Interfaces:**
- Consumes: Fluent resource keys `TextFillColorSecondaryBrush`,
  `SystemFillColorCautionBackgroundBrush` (present in Fluent Light/Dark/HC
  dictionaries, verified in dotnet/wpf sources); `BooleanToVisibilityConverter`.
- Produces: `BooleanToVisibilityConverter` keyed resource in Shared.xaml
  (reusable); game cells styled purely by Fluent defaults + highlight overlay.

- [ ] **Step 1: Register the converter in Shared.xaml**

Add inside `<ResourceDictionary>`, before `</ResourceDictionary>`:

```xml
    <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
```

- [ ] **Step 2: Lobby label token**

In `LobbyWindow.xaml`, change:

```xml
<TextBlock Text="{Binding Label}" Foreground="Gray" />
```

to:

```xml
<TextBlock Text="{Binding Label}" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
```

- [ ] **Step 3: Game cell overlay (replaces the custom style)**

In `GameWindow.xaml`, replace the entire `<DataTemplate>` inside
`BoardGrid.ItemTemplate` with:

```xml
                <DataTemplate>
                    <Grid Margin="2">
                        <Button FontSize="32" FontWeight="Bold"
                                Content="{Binding Mark}"
                                IsEnabled="{Binding IsEnabled}"
                                Command="{Binding DataContext.MoveCommand,
                                          RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding CellIndex}" />
                        <Border Background="{DynamicResource SystemFillColorCautionBackgroundBrush}"
                                IsHitTestVisible="False"
                                Visibility="{Binding IsHighlighted,
                                             Converter={StaticResource BooleanToVisibilityConverter}}" />
                    </Grid>
                </DataTemplate>
```

The old `<Button.Style>` block (White/LightGoldenrodYellow triggers) is
deleted — a custom style would fall back to Aero2 templates inside a Fluent
window. The overlay sits above the button, ignores mouse input, and is only
visible while `IsHighlighted` is true.

- [ ] **Step 4: Update the WinningLine test**

In `tests/Client-Server-App.UiTests/GameWindowTests.cs`: add
`using System.Windows;` to the usings, then replace the
`WinningLine_HighlightsCells_WhiteElsewhere_AnnouncesWin` test with:

```csharp
    [WpfFact]
    public async Task WinningLine_HighlightsWinningCells_KeepsOthersClear_AnnouncesWin()
    {
        // The test host has no Application, so Fluent tokens are unavailable
        // until the theme dictionary is merged into the window explicitly.
        _window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml")
        });

        string[] won = ["X", "X", "X", "", "O", "", "", "", ""];
        _transport.ReceiveLine(GameJson.Serialize(new GameStateRecord(
            won, "O", "won", "X", [0, 1, 2], 1, Room: "duel", XName: "Alice", OName: "Bob")));
        await TestDispatcher.FlushAsync();

        IReadOnlyList<System.Windows.Controls.Border> overlays =
            VisualTreeEx.FindChildren<System.Windows.Controls.Border>(_window.BoardGrid).ToList();
        Assert.Equal(9, overlays.Count);
        Assert.Contains("You win!", _window.StatusText.Text);

        foreach (int i in Enumerable.Range(0, 9))
        {
            Assert.Equal(i <= 2 ? Visibility.Visible : Visibility.Collapsed,
                overlays[i].Visibility);
        }

        Assert.Equal((Brush)overlays[0].FindResource("SystemFillColorCautionBackgroundBrush"),
            overlays[0].Background);
    }
```

(`FindChildren` yields cells in board order, so indexes align; the
`FindResource` call resolves in the same scope as the production
`DynamicResource`.)

- [ ] **Step 5: Run full validation**

```bash
dotnet build Client-Server-App.slnx -c Release --nologo
./scripts/run-tests.ps1
```

Expect: all tests green (40 UI including the updated one), coverage gate pass.

- [ ] **Step 6: Commit**

```bash
git add src/Client-Server-App/Themes/Shared.xaml src/Client-Server-App/LobbyWindow.xaml src/Client-Server-App/GameWindow.xaml tests/Client-Server-App.UiTests/GameWindowTests.cs
git commit -m "feat: theme-reactive lobby label and game cell highlight"
```

---

### Task 5: Manual verification checklist + wrap-up

**Files:**
- Modify: only whatever the checklist flushes out (expected: nothing)

**Interfaces:**
- Consumes: Tasks 2–4.
- Produces: verified feature; final green suite.

- [ ] **Step 1: Execute the spec checklist with the user**

Launch via two instances (host + guest) and verify:
- Each of the 5 windows (Main, Connection, Lobby, Game, Server/Referee) in
  System, Light, and Dark.
- Live switch while Connection → Lobby → Game are open.
- Fresh launch follows the Windows setting (System default).
- Win11 dark titlebar renders; accent color respected.
- Board cells and win highlight readable in both modes.
- If Mica backdrop misbehaves on any window: add
  `<Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop value="true" />`
  (app-context switch in `ConfigurationManager`-style app config) and re-check
  — flag to the user before applying.

- [ ] **Step 2: Final full validation**

```bash
./scripts/run-tests.ps1
```

- [ ] **Step 3: Commit any checklist-driven fixes**

Only if Step 1 produced changes:

```bash
git add -A
git commit -m "fix: address dark mode verification findings"
```
