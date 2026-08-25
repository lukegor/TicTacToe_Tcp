# MVVM Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move essentially all logic from the five windows' code-behind into CommunityToolkit.Mvvm ViewModels, keeping every existing test green and adding headless VM tests for isolated decision logic.

**Architecture:** One ViewModel per window (`Connection`, `Lobby`, `Game`, `Server`) in `src/Client-Server-App/ViewModels/`, all cross-thread event marshaling funneled through an injected `IUiDispatcher`. Windows shrink to construction, `DataContext`, lifecycle plumbing, and the existing opener probes. UI tests stay untouched as the binding-wiring regression net; per-window headless VM tests land alongside each migration commit.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm **8.4.2** (partial-property `[ObservableProperty]`, `[RelayCommand]`), C# 14 `field` keyword where accessor logic is custom.

**Spec:** `docs/superpowers/specs/2026-08-25-mvvm-migration-design.md`

## Global Constraints

- Every commit: warning-free `dotnet build Client-Server-App.slnx -c Release`, full suite green (starts at 122), and after Task 1 the count only grows.
- The 38 existing UiTests are modified **only** where this plan explicitly says so.
- All user-visible strings move byte-for-byte ("Enter an IP address.", "'…' is not a valid port (1-{max}).", "Connection failed:", "Could not start the host:", "Enter a room name first.", "Connected to", "Listening on port", status switch text, rematch labels, banner text).
- VMs never reference `Window`, `Dispatcher`, controls, or `App.` statics — marshaling only via injected `IUiDispatcher`.
- `[ObservableProperty]` partial properties for plain observable state; C# 14 **`field`** keyword preferred over manual backing fields where accessor logic is custom.
- Toolkit pin: `<PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />`.

---

### Task 1: Toolkit pin + dispatcher infrastructure

**Files:**
- Modify: `Directory.Packages.props` (add pin)
- Modify: `src/Client-Server-App/Client-Server-App.csproj` (PackageReference)
- Create: `src/Client-Server-App/ViewModels/IUiDispatcher.cs`
- Create: `src/Client-Server-App/ViewModels/SynchronizationContextDispatcher.cs`
- Create: `tests/Client-Server-App.UiTests/InlineDispatcher.cs`

**Interfaces:**
- Produces: `internal interface IUiDispatcher { void Post(Action action); }`, `internal sealed class SynchronizationContextDispatcher(SynchronizationContext context) : IUiDispatcher`, test double `public sealed class InlineDispatcher : IUiDispatcher` (ns `ClientServer.UiTests`).

- [ ] **Step 1: Pin and reference the toolkit**

`Directory.Packages.props` gains:

```xml
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
```

`src/Client-Server-App/Client-Server-App.csproj` ItemGroup gains:

```xml
    <PackageReference Include="CommunityToolkit.Mvvm" />
```

- [ ] **Step 2: Dispatcher abstraction**

`src/Client-Server-App/ViewModels/IUiDispatcher.cs`:

```csharp
namespace ClientServer.App.ViewModels;

/// <summary>Marshal onto the UI thread.</summary>
internal interface IUiDispatcher
{
    void Post(Action action);
}
```

`src/Client-Server-App/ViewModels/SynchronizationContextDispatcher.cs`:

```csharp
namespace ClientServer.App.ViewModels;

internal sealed class SynchronizationContextDispatcher(SynchronizationContext context) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        context.Post(static state => ((Action)state!).Invoke(), action);
    }
}
```

- [ ] **Step 3: Headless executor for tests**

`tests/Client-Server-App.UiTests/InlineDispatcher.cs`:

```csharp
using ClientServer.App.ViewModels;

namespace ClientServer.UiTests;

public sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
```

- [ ] **Step 4: Validate**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test
```

122/122, warning-free.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/Client-Server-App/Client-Server-App.csproj src/Client-Server-App/ViewModels tests/Client-Server-App.UiTests/InlineDispatcher.cs
git commit -m "chore: pin CommunityToolkit.Mvvm and add ui dispatcher"
```

---

### Task 2: Extract Connection + Server ViewModels

**Files:**
- Create: `src/Client-Server-App/ViewModels/ConnectionViewModel.cs`
- Create: `src/Client-Server-App/ViewModels/ServerViewModel.cs`
- Modify: `src/Client-Server-App/ConnectionWindow.xaml` (command/bindings, `UpdateSourceTrigger=PropertyChanged`)
- Modify: `src/Client-Server-App/ConnectionWindow.xaml.cs`
- Modify: `src/Client-Server-App/ServerWindow.xaml` (RoomsPanel → ItemsControl bound to `Rooms`; OutputTextBox → `LogText`)
- Modify: `src/Client-Server-App/ServerWindow.xaml.cs`

**Interfaces:**
- Consumes: Task 1 dispatchers; Core types (`ServerTcp`, `LobbyService`, `PlayerSession`, `GameJson`, records, `IClientTransport`).
- Produces:
  - `internal sealed class ConnectionViewModel(Func<string,int,CancellationToken,Task<IClientTransport>> connectFactory, Func<int,ServerTcp> hostFactory, Func<ServerTcp,LobbyService> lobbyFactory, Func<Func<Task<IClientTransport>>,string?,PlayerSession> sessionFactory, IUiDispatcher ui)`
    - Observable: `Address="127.0.0.1"`, `Port="1111"`, `PlayerName=""`, `HostPort="1111"`, `LogText=""`
    - Commands: `ConnectCommand`, `HostCommand`
    - Events: `LobbyReady(PlayerSession)`, `RefereeReady(int port, ServerTcp server, LobbyService lobby)`
    - `void Cancel()`
  - `internal sealed class ServerViewModel(LobbyService lobby, IUiDispatcher ui)` — `ObservableCollection<RoomInfoRecord> Rooms`, `string LogText`.

- [ ] **Step 1: ConnectionViewModel**

```csharp
using System.Globalization;
using System.Net;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ClientServer.App.ViewModels;

internal sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly Func<string, int, CancellationToken, Task<IClientTransport>> _connectFactory;
    private readonly Func<int, ServerTcp> _hostFactory;
    private readonly Func<ServerTcp, LobbyService> _lobbyFactory;
    private readonly Func<Func<Task<IClientTransport>>, string?, PlayerSession> _sessionFactory;
    private readonly IUiDispatcher _ui;
    private readonly CancellationTokenSource _closed = new();

    public event Action<PlayerSession>? LobbyReady;
    public event Action<int, ServerTcp, LobbyService>? RefereeReady;

    [ObservableProperty] public partial string Address { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial string Port { get; set; } = "1111";
    [ObservableProperty] public partial string PlayerName { get; set; } = "";
    [ObservableProperty] public partial string HostPort { get; set; } = "1111";
    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ConnectionViewModel(
        Func<string, int, CancellationToken, Task<IClientTransport>> connectFactory,
        Func<int, ServerTcp> hostFactory,
        Func<ServerTcp, LobbyService> lobbyFactory,
        Func<Func<Task<IClientTransport>>, string?, PlayerSession> sessionFactory,
        IUiDispatcher ui)
    {
        _connectFactory = connectFactory;
        _hostFactory = hostFactory;
        _lobbyFactory = lobbyFactory;
        _sessionFactory = sessionFactory;
        _ui = ui;
    }

    /// <summary>Called by the window when it closes; pending connects stop
    /// before raising LobbyReady into a dead view.</summary>
    public void Cancel() => _closed.Cancel();

    private void AppendLog(string message) => LogText += message + Environment.NewLine;

    private static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text?.Trim(), CultureInfo.InvariantCulture, out port)
        && port is > 0 and <= IPEndPoint.MaxPort;

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);

        if (string.IsNullOrWhiteSpace(Address))
        {
            AppendLog("Enter an IP address.");
            return;
        }

        if (!TryParsePort(Port, out int port))
        {
            AppendLog($"'{Port}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        IClientTransport? client = null;
        try
        {
            client = await _connectFactory(Address.Trim(), port, linked.Token).ConfigureAwait(true);
            AppendLog($"Connected to {Address.Trim()}:{port}.");

            bool firstUse = true;
            async Task<IClientTransport> ReconnectFactory()
            {
                if (Interlocked.Exchange(ref firstUse, false))
                {
                    return client!;
                }

                return await _connectFactory(Address.Trim(), port, CancellationToken.None)
                    .ConfigureAwait(true);
            }

            PlayerSession session = _sessionFactory(ReconnectFactory, PlayerName);
            await session.ConnectAsync().ConfigureAwait(true);

            if (linked.Token.IsCancellationRequested)
            {
                session.Dispose();
                return;
            }

            LobbyReady?.Invoke(session);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                        or IOException or SocketException)
        {
            AppendLog($"Connection failed: {ex.Message}");
            (client as IDisposable)?.Dispose();
        }
    }

    [RelayCommand]
    private void Host()
    {
        if (!TryParsePort(HostPort, out int port))
        {
            AppendLog($"'{HostPort}' is not a valid port (1-{IPEndPoint.MaxPort}).");
            return;
        }

        ServerTcp server = _hostFactory(port);
        try
        {
            server.Start();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            AppendLog($"Could not start the host: {ex.Message}");
            server.Dispose();
            return;
        }

        AppendLog($"Listening on port {port}.");
        LobbyService lobby = _lobbyFactory(server);
        lobby.Start();
        RefereeReady?.Invoke(port, server, lobby);
    }
}
```

(`using System.IO; using System.Net.Sockets;` are required for IOException/SocketException.)

- [ ] **Step 2: ServerViewModel**

```csharp
using System.Collections.ObjectModel;
using ClientServer.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientServer.App.ViewModels;

internal sealed partial class ServerViewModel : ObservableObject
{
    private readonly LobbyService _lobby;
    private readonly IUiDispatcher _ui;

    public ObservableCollection<RoomInfoRecord> Rooms { get; } = [];

    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ServerViewModel(LobbyService lobby, IUiDispatcher ui)
    {
        _lobby = lobby;
        _ui = ui;
        RefreshRooms();
        lobby.RoomsChanged += () => _ui.Post(RefreshRooms);
        lobby.LogReceived += message => _ui.Post(() => LogText += message + Environment.NewLine);
    }

    private void RefreshRooms()
    {
        Rooms.Clear();
        foreach (RoomInfoRecord room in _lobby.GetRooms())
        {
            Rooms.Add(room);
        }
    }
}
```

- [ ] **Step 3: Rewire ConnectionWindow**

Code-behind (full replacement of members between ctor and TryParsePort removal):

```csharp
using System.Windows;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;

namespace ClientServer.App;

public partial class ConnectionWindow : Window
{
    private readonly Func<LobbyWindow, bool>? _lobbyProbe;
    private readonly Func<ServerWindow, bool>? _refereeProbe;
    private readonly ConnectionViewModel _viewModel;

    public ConnectionWindow() : this(null, null, null)
    {
    }

    internal ConnectionWindow(
        Func<LobbyWindow, bool>? lobbyProbe,
        Func<ServerWindow, bool>? refereeProbe,
        IUiDispatcher? dispatcher)
    {
        _lobbyProbe = lobbyProbe;
        _refereeProbe = refereeProbe;
        SynchronizationContext context = SynchronizationContext.Current
            ?? new SynchronizationContext();
        InitializeComponent();
        _viewModel = new ConnectionViewModel(
            (host, port, ct) =>
            {
                ClientTcp client = new(App.LoggerFactory.CreateLogger<ClientTcp>());
                return client.ConnectAsync(host, port, ct).ContinueWith(
                    _ => (IClientTransport)client, ct, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            },
            port => new ServerTcp(port, App.LoggerFactory.CreateLogger<ServerTcp>()),
            server => new LobbyService(server, logger: App.LoggerFactory.CreateLogger<LobbyService>()),
            (factory, name) => new PlayerSession(factory, displayName: name,
                logger: App.LoggerFactory.CreateLogger<PlayerSession>()),
            dispatcher ?? new SynchronizationContextDispatcher(context));
        DataContext = _viewModel;
        _viewModel.LobbyReady += session =>
            OpenLobbyWindow(() => new LobbyWindow(session), () => session.Dispose());
        _viewModel.RefereeReady += (port, server, lobby) =>
            OpenRefereeWindow(() => new ServerWindow(lobby, port), () =>
            {
                lobby.Dispose();
                server.Dispose();
            });
    }

    private void OpenLobbyWindow(Func<LobbyWindow> createWindow, Action onClose)
    {
        LobbyWindow window = createWindow();
        if (_lobbyProbe?.Invoke(window) == true)
        {
            window.Closed += (_, _) => onClose();
            return;
        }

        if (IsVisible)
        {
            window.Owner = this;
        }

        window.Closed += (_, _) => onClose();
        window.Show();
    }

    private void OpenRefereeWindow(Func<ServerWindow> createWindow, Action onClose)
    {
        ServerWindow window = createWindow();
        if (_refereeProbe?.Invoke(window) == true)
        {
            window.Closed += (_, _) => onClose();
            return;
        }

        if (IsVisible)
        {
            window.Owner = this;
        }

        window.Closed += (_, _) => onClose();
        window.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cancel();
        base.OnClosed(e);
    }
}
```

Deleted from code-behind: `_client`, `_server`, `ConnectButton_Click`, `HostButton_Click`, both old open methods' inline creation, `TryParsePort`, `AppendLog`, and the old internal parameterless-probe ctor body (now chained).

`ConnectionWindow.xaml` control changes (attributes only — layout untouched):

```xml
<TextBox x:Name="AddressTextBox" Width="200" Margin="0,0,0,10"
         Text="{Binding Address, UpdateSourceTrigger=PropertyChanged}" />
<TextBox x:Name="PortTextBox" Width="200" Margin="0,0,0,10"
         Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}" />
<TextBox x:Name="NameTextBox" Width="200" Margin="0,0,0,10"
         Text="{Binding PlayerName, UpdateSourceTrigger=PropertyChanged}" />
<Button x:Name="ConnectButton" Content="Connect" Command="{Binding ConnectCommand}" ... />
<TextBox x:Name="HostPortTextBox" Width="200"
         Text="{Binding HostPort, UpdateSourceTrigger=PropertyChanged}" />
<Button x:Name="HostButton" Content="Create Host" Command="{Binding HostCommand}" ... />
<TextBox x:Name="OutputTextBox" ... Text="{Binding LogText}" IsReadOnly="True" ... />
```

Keep the `x:Name` attributes (tests address controls through them).

`ServerWindow.xaml`: replace the `RoomsPanel` StackPanel with:

```xml
<ItemsControl x:Name="RoomsPanel" ItemsSource="{Binding Rooms}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Grid Margin="0,2,0,2">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="90" />
                    <ColumnDefinition Width="100" />
                </Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Name}" />
                <TextBlock Grid.Column="1" Text="{Binding Players, StringFormat={}{0}/2}" />
                <TextBlock Grid.Column="2" Text="{Binding Spectators}" />
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

OutputTextBox becomes `Text="{Binding LogText}" IsReadOnly="True"`.

`ServerWindow.xaml.cs` full replacement:

```csharp
using System.Windows;
using ClientServer.App.ViewModels;

namespace ClientServer.App;

/// <summary>Read-only referee view: live room table plus an event log.</summary>
public partial class ServerWindow : Window
{
    public ServerWindow(LobbyService lobby, int port)
    {
        InitializeComponent();
        Title = $"Referee — port {port}";
        DataContext = new ServerViewModel(lobby,
            new SynchronizationContextDispatcher(SynchronizationContext.Current!));
    }
}
```

- [ ] **Step 4: Adjust the affected UiTests (mechanical only)**

- Connection/Host tests: unchanged — `.Text=` now flows through bindings thanks
  to `UpdateSourceTrigger=PropertyChanged`; `IsEnabled` on ConnectButton is driven
  by the async relay (re-enabled automatically after failure).
- `TwoPlayersSeated_RendersSingleRoomRow_WithCounts`: rows are now generated
  items — replace direct `RoomsPanel.Children[0]` access with container lookup:

```csharp
    var container = (ContentPresenter?)window.RoomsPanel.ItemContainerGenerator
        .ContainerFromIndex(0);
    Assert.NotNull(container);
    var texts = VisualTreeEx.FindChildren<TextBlock>(container)
        .Select(t => t.Text).ToList();
    Assert.Equal(["duel", "2/2", "0"], texts);
```

- `MembershipChange_RerendersRows`: same lookup per index; the empty-lobby
  assertion becomes `Assert.Empty(window.RoomsPanel.Items)`.

- [ ] **Step 5: Validate**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test
```

Warning-free; all suites green (Core 84 + UiTests 38 + new VM tests below).

- [ ] **Step 6: Add headless VM tests**

Create `tests/Client-Server-App.UiTests/ViewModels/ConnectionViewModelTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using ClientServer.TestSupport;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class ConnectionViewModelTests
{
    [Fact]
    public async Task EmptyAddress_AppendsHint_SendsNothing()
    {
        int calls = 0;
        ConnectionViewModel vm = new(
            (h, p, ct) => { calls++; return Task.FromResult<IClientTransport>(null!); },
            _ => null!, (_, _) => null!,
            new InlineDispatcher());

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("Enter an IP address.", vm.LogText);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public async Task InvalidPort_AppendsPortError(string port)
    {
        ConnectionViewModel vm = new(
            (_, _, _) => Task.FromResult<IClientTransport>(null!), _ => null!, (_, _) => null!,
            new InlineDispatcher());
        vm.Address = "127.0.0.1";
        vm.Port = port;

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains($"'{port}' is not a valid port", vm.LogText);
    }
}
```

(The remaining happy-path connect/host flows for this VM are covered end-to-end
by the untouched UiTests through real sockets — do not duplicate them here.)

Create `tests/Client-Server-App.UiTests/ViewModels/ServerViewModelTests.cs`:

```csharp
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.TestSupport;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class ServerViewModelTests
{
    [Fact]
    public void RoomEvents_RefreshRooms_Inline()
    {
        FakeServerTransport transport = new();
        using LobbyService lobby = new(transport);
        lobby.Start();
        ServerViewModel vm = new(lobby, new InlineDispatcher());
        Assert.Empty(vm.Rooms);

        Guid a = transport.SimulateClientConnected();
        transport.ReceiveLine(a, GameJson.Serialize(new HelloRecord("Alice")));
        transport.ReceiveLine(a, GameJson.Serialize(new CreateRoomRecord("duel")));

        _ = Assert.Single(vm.Rooms);
        Assert.Contains("connected.", vm.LogText);
    }
}
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: extract connection and server viewmodels"
```

---

### Task 3: Extract LobbyViewModel

**Files:**
- Create: `src/Client-Server-App/ViewModels/LobbyViewModel.cs`
- Modify: `src/Client-Server-App/LobbyWindow.xaml` (bindings, `UpdateSourceTrigger=PropertyChanged`, Join button → command)
- Modify: `src/Client-Server-App/LobbyWindow.xaml.cs`
- Create: `tests/Client-Server-App.UiTests/ViewModels/LobbyViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 dispatchers; `PlayerSession` events (`RoomsUpdated`, `Seated`, `ReturnedToLobby`, `ErrorReceived`, `LogReceived`) and methods (`CreateRoomAsync`, `JoinRoomAsync`, `Dispose`).
- Produces: `internal sealed class LobbyViewModel(PlayerSession session, IUiDispatcher ui)` — `ObservableCollection<RoomInfoRecord> Rooms`, `string RoomNameInput`, `string LogText`, commands `CreateRoomCommand` / `JoinRoomCommand(RoomInfoRecord)`; events `Action<JoinedRecord>? Seated` (passthrough), `Action? ReturnedToLobby`.

- [ ] **Step 1: Implement the ViewModel**

```csharp
using System.Collections.ObjectModel;
using ClientServer.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientServer.App.ViewModels;

internal sealed partial class LobbyViewModel : ObservableObject
{
    private readonly PlayerSession _session;
    private readonly IUiDispatcher _ui;

    public event Action<JoinedRecord>? Seated;
    public event Action<string>? ReturnedToLobby;

    public ObservableCollection<RoomInfoRecord> Rooms { get; } = [];

    [ObservableProperty] public partial string RoomNameInput { get; set; } = "";
    [ObservableProperty] public partial string LogText { get; set; } = "";

    public LobbyViewModel(PlayerSession session, IUiDispatcher ui)
    {
        _session = session;
        _ui = ui;
        RefreshRooms(session.LatestRooms);

        session.RoomsUpdated += rooms => _ui.Post(() => RefreshRooms(rooms));
        session.Seated += joined => _ui.Post(() => Seated?.Invoke(joined));
        session.ReturnedToLobby += reason =>
        {
            _ui.Post(() =>
            {
                RefreshRooms(_session.LatestRooms);
                ReturnedToLobby?.Invoke(reason);
            });
        };
        session.ErrorReceived += message => AppendNotice(message);
        session.LogReceived += message => AppendNotice(message);
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        string name = RoomNameInput.Trim();
        if (name.Length == 0)
        {
            AppendNotice("Enter a room name first.");
            return;
        }

        try
        {
            await _session.CreateRoomAsync(name);
            RoomNameInput = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    [RelayCommand]
    private async Task JoinRoomAsync(RoomInfoRecord? room)
    {
        if (room is null)
        {
            return;
        }

        try
        {
            await _session.JoinRoomAsync(room.Name);
        }
        catch (InvalidOperationException ex)
        {
            AppendNotice(ex.Message);
        }
    }

    private void RefreshRooms(IReadOnlyList<RoomInfoRecord> rooms)
    {
        Rooms.Clear();
        foreach (RoomInfoRecord room in rooms)
        {
            Rooms.Add(room);
        }
    }

    private void AppendNotice(string message) => LogText += message + Environment.NewLine;
}
```

- [ ] **Step 2: Rewire the window**

Code-behind becomes:

```csharp
using System.Windows;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;

namespace ClientServer.App;

public partial class LobbyWindow : Window
{
    private readonly PlayerSession _session;
    private GameWindow? _gameWindow;

    internal LobbyWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        Title = $"Lobby — {session.DisplayName}";
        var vm = new LobbyViewModel(session,
            new SynchronizationContextDispatcher(SynchronizationContext.Current!));
        DataContext = vm;
        vm.Seated += _ => Dispatcher.BeginInvoke(OpenGameWindow);
        vm.ReturnedToLobby += _ => Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible && _gameWindow is null)
            {
                Show();
            }
        });
        RoomsList.ItemsSource = vm.Rooms;
    }
    // OpenGameWindow / OnClosed unchanged
}
```

XAML changes:

```xml
<TextBox x:Name="RoomNameBox" Width="180" VerticalAlignment="Center"
         Text="{Binding RoomNameInput, UpdateSourceTrigger=PropertyChanged}" />
<Button x:Name="CreateButton" Content="Create room" Width="100" Margin="8,0,0,0"
        Command="{Binding CreateRoomCommand}" />
<ListBox x:Name="RoomsList" Grid.Row="1" ItemsSource="{Binding Rooms}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <DockPanel Margin="2">
                <Button DockPanel.Dock="Right" Content="Join" Width="70"
                        Command="{Binding DataContext.JoinRoomCommand,
                                  RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}" />
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{Binding Name}" FontWeight="Bold" Margin="0,0,10,0" />
                    <TextBlock Text="{Binding Label}" Foreground="Gray" />
                </StackPanel>
            </DockPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
<TextBox x:Name="OutputTextBox" Grid.Row="2" Margin="0,8,0,0" IsReadOnly="True"
         FontFamily="Consolas" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
         Text="{Binding LogText}" />
```

Delete `CreateButton_Click`, `JoinButton_Click`, `AppendNotice`, and the
`OnRoomsUpdated` handler from code-behind.

- [ ] **Step 3: Adjust affected UiTests (mechanical only)**

In `LobbyWindowTests`:
- `ItemsSource` is now `ObservableCollection<RoomInfoRecord>` — replace
  `(IReadOnlyList<RoomInfoRecord>)window.RoomsList.ItemsSource` with
  `window.RoomsList.ItemsSource.Cast<RoomInfoRecord>().ToList()` in
  `Construction_And_RoomListPush_RendersRooms`.
- The recovery test's final assertion likewise switches to `.Cast<...>()`.
- `JoinRow_DisconnectedSession_SurfaceOperationError`: after
  `SimulateDisconnect`, the session enters reconnecting/disconnected state —
  pressing Join now throws inside the VM command and appends the exception
  message to `LogText`; assertion stays `Assert.NotEqual(string.Empty, ...)`.
  If it proves flaky because the command swallows before flush, assert on
  `window.OutputTextBox.Text` length after an extra `FlushAsync()`.

- [ ] **Step 4: Add headless VM tests**

`tests/Client-Server-App.UiTests/ViewModels/LobbyViewModelTests.cs`:

```csharp
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.TestSupport;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class LobbyViewModelTests : IDisposable
{
    private readonly PlayerSession _session;
    private readonly FakeClientTransport _transport;

    public LobbyViewModelTests()
    {
        (_session, _transport) = UiTestSession.ConnectSeatedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public void InitialRooms_AreLoadedFromSession()
    {
        LobbyViewModel vm = new(_session, new InlineDispatcher());
        Assert.Single(vm.Rooms);
    }

    [Fact]
    public async Task CreateRoom_Valid_SendsEnvelope_ClearsInput()
    {
        LobbyViewModel vm = new(_session, new InlineDispatcher());
        vm.RoomNameInput = "friday";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Contains(_transport.SentLines, l =>
            l.Contains("\"createRoom\"") && l.Contains("friday"));
        Assert.Equal(string.Empty, vm.RoomNameInput);
    }

    [Fact]
    public async Task CreateRoom_Empty_ShowsNotice_SendsNothing()
    {
        LobbyViewModel vm = new(_session, new InlineDispatcher());
        int before = _transport.SentLines.Count;

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Contains("Enter a room name first.", vm.LogText);
        Assert.Equal(before, _transport.SentLines.Count);
    }

    [Fact]
    public async Task JoinRoom_SendsEnvelope()
    {
        LobbyViewModel vm = new(_session, new InlineDispatcher());

        await vm.JoinRoomCommand.ExecuteAsync(new RoomInfoRecord("duel", 1, 0));

        Assert.Contains(_transport.SentLines, l =>
            l.Contains("\"joinRoom\"") && l.Contains("duel"));
    }

    [Fact]
    public void ErrorEnvelope_AppendsNotice()
    {
        LobbyViewModel vm = new(_session, new InlineDispatcher());

        _transport.ReceiveLine(GameJson.Serialize(new ErrorRecord("nope")));

        Assert.Contains("nope", vm.LogText);
    }

    [Fact]
    public void RoomsUpdate_ReplacesCollection()
    {
        LobbyViewModel vm = new(_session, new InlineDispatcher());

        _transport.ReceiveLine(GameJson.Serialize(new RoomListRecord(
            [new RoomInfoRecord("a", 1, 0), new RoomInfoRecord("b", 2, 3)])));

        Assert.Equal(2, vm.Rooms.Count);
    }
}
```

Note: these run headless because the inline dispatcher executes session events
synchronously on the test thread.

- [ ] **Step 5: Validate and commit**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test
git add -A
git commit -m "refactor: extract lobby viewmodel"
```

---

### Task 4: Extract GameViewModel

**Files:**
- Create: `src/Client-Server-App/ViewModels/GameViewModel.cs` (+ `CellViewModel` nested file)
- Modify: `src/Client-Server-App/GameWindow.xaml` (board → bound `ItemsControl`, status/rematch/output bindings)
- Modify: `src/Client-Server-App/GameWindow.xaml.cs`
- Create: `tests/Client-Server-App.UiTests/ViewModels/GameViewModelTests.cs`

**Interfaces:**
- Consumes: dispatchers; `PlayerSession` (`StateReceived`, `Seated`, `ErrorReceived`, `LogReceived`, `ReconnectingStarted`, `CurrentState`, `MyMark`, `IsSpectator`, `PlayCellAsync`, `SendRematchOfferAsync`, `LeaveRoomAsync`); records.
- Produces:
  - `internal sealed class CellViewModel(int index) : ObservableObject` — `int CellIndex { get; }`, `[ObservableProperty]` `string Mark = ""`, `bool IsEnabled`, `bool IsHighlighted`.
  - `internal sealed partial class GameViewModel(PlayerSession session, IUiDispatcher ui)` — `ObservableCollection<CellViewModel> Cells`, `[ObservableProperty]` `StatusText="Joining..."`, `TitleText="Tic-Tac-Toe"`, `OutputText=""`, `RematchLabel="Offer Rematch"`, `bool RematchEnabled`, `bool BannerVisible`; commands `MoveCommand(int)`, `OfferRematchCommand`, `LeaveCommand`.

- [ ] **Step 1: Implement CellViewModel and GameViewModel**

`src/Client-Server-App/ViewModels/CellViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientServer.App.ViewModels;

internal sealed partial class CellViewModel(int index) : ObservableObject
{
    public int CellIndex { get; } = index;

    [ObservableProperty] public partial string Mark { get; set; } = "";
    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial bool IsHighlighted { get; set; }
}
```

`src/Client-Server-App/ViewModels/GameViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using ClientServer.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientServer.App.ViewModels;

internal sealed partial class GameViewModel : ObservableObject
{
    private readonly PlayerSession _session;
    private readonly IUiDispatcher _ui;
    private GameStateRecord? _rendered;

    public ObservableCollection<CellViewModel> Cells { get; } = [];

    [ObservableProperty] public partial string StatusText { get; set; } = "Joining...";
    [ObservableProperty] public partial string TitleText { get; set; } = "Tic-Tac-Toe";
    [ObservableProperty] public partial string OutputText { get; set; } = "";
    [ObservableProperty] public partial string RematchLabel { get; set; } = "Offer Rematch";
    [ObservableProperty] public partial bool RematchEnabled { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LeaveCommand))]
    public partial bool LeaveEnabled { get; set; } = true;

    [ObservableProperty] public partial bool BannerVisible { get; set; }

    public GameViewModel(PlayerSession session, IUiDispatcher ui)
    {
        _session = session;
        _ui = ui;

        for (int i = 0; i < 9; i++)
        {
            Cells.Add(new CellViewModel(i));
        }

        session.StateReceived += state => _ui.Post(() => ApplyState(state));
        session.Seated += joined => _ui.Post(() =>
        {
            if (joined.Restored)
            {
                AppendOutput("Reconnected — your seat was restored.");
            }

            ApplyState(joined.State);
        });
        session.ErrorReceived += message => AppendOutput(message);
        session.LogReceived += AppendOutput;
        session.ReconnectingStarted += () => _ui.Post(ShowBanner);

        if (session.CurrentState is { } initial)
        {
            ApplyState(initial);
        }
    }

    private void ShowBanner()
    {
        BannerVisible = true;
        StatusText = "Connection lost — rejoining...";
        foreach (CellViewModel cell in Cells)
        {
            cell.IsEnabled = false;
        }

        RematchEnabled = false;
    }

    private void ApplyState(GameStateRecord state)
    {
        _rendered = state;
        string? myMark = _session.MyMark;
        bool spectator = myMark is null;
        bool inProgress = state.Status == "inProgress";
        bool myTurn = inProgress && !spectator && state.Turn == myMark;

        TitleText = $"Tic-Tac-Toe — {state.Room}" + DescribeSeats(state, myMark, spectator);

        for (int i = 0; i < Cells.Count; i++)
        {
            Cells[i].Mark = state.Board[i];
            Cells[i].IsEnabled = myTurn && state.Board[i] == "";
            Cells[i].IsHighlighted = false;
        }

        if (state.WinningLine is not null)
        {
            foreach (int cell in state.WinningLine)
            {
                Cells[cell].IsHighlighted = true;
            }
        }

        string status = (spectator ? "[Spectating] " : string.Empty) + state.Status switch
        {
            "waiting" => "Waiting for an opponent to join...",
            "won" when state.WinnerReason == "forfeit" => $"{state.Winner} wins by forfeit.",
            "won" when state.Winner == myMark => "You win!",
            "won" when spectator => $"{state.Winner} wins!",
            "won" => "You lose.",
            "draw" => "It's a draw.",
            _ when myTurn => $"Your move ({myMark}).",
            _ when spectator => $"{state.Turn}'s move.",
            _ => "Opponent's move.",
        };

        if (RematchOfferedByOpponent(state) is { } challenger)
        {
            status += $"{Environment.NewLine}{challenger} offers a rematch.";
        }

        StatusText = status;
        RefreshRematch(state);
    }

    private void RefreshRematch(GameStateRecord state)
    {
        bool decided = state.Status is "won" or "draw";
        bool player = !_session.IsSpectator;
        bool mine = decided && player && state.RematchOfferedBy == _session.MyMark;
        RematchEnabled = decided && player && !mine;
        RematchLabel = mine
            ? "Rematch offered..."
            : RematchOfferedByOpponent(state) is not null ? "Accept Rematch"
            : "Offer Rematch";
    }

    private string? RematchOfferedByOpponent(GameStateRecord state) =>
        state.RematchOfferedBy is { } mark && mark != _session.MyMark ? SeatName(state, mark) : null;

    private static string SeatName(GameStateRecord state, string mark) =>
        mark == "X" ? state.XName ?? "X" : state.OName ?? "O";

    private static string DescribeSeats(GameStateRecord state, string? myMark, bool spectator)
    {
        if (state.XName is not null && state.OName is not null)
        {
            return $" — {state.XName} (X) vs {state.OName} (O)";
        }

        return spectator ? " (spectator)" : $" ({myMark})";
    }

    private void AppendOutput(string message) => OutputText += message + Environment.NewLine;

    private bool MoveCanExecute(int cell) =>
        _rendered is { Status: "inProgress" }
        && !_session.IsSpectator
        && _session.MyMark is { } myMark
        && _rendered.Turn == myMark
        && Cells[cell].IsEnabled;

    [RelayCommand(CanExecute = nameof(MoveCanExecute))]
    private async Task MoveAsync(int cell)
    {
        await SafeCallAsync(() => _session.PlayCellAsync(cell), "Move failed");
    }

    [RelayCommand]
    private async Task OfferRematchAsync()
    {
        await SafeCallAsync(() => _session.SendRematchOfferAsync(), "Rematch failed");
    }

    private bool LeaveCanExecute() => LeaveEnabled;

    private async Task SafeCallAsync(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                                        or IOException or System.Net.Sockets.SocketException)
        {
            AppendOutput($"{failurePrefix}: {ex.Message}");
        }
    }
}
```

Required usings at top of the file: `System.IO` (IOException). Note the
`field`-keyword candidate documented as a comment: `LeaveEnabled` currently
keeps a manual backing pair because it also raises a command notification —
if this pattern repeats, convert to `partial bool LeaveEnabled { get; set; }`
with `[NotifyCanExecuteChangedFor(nameof(LeaveCommand))]` on a separate
`[ObservableProperty] LeaveEnabled` declaration instead. Per user preference,
prefer:

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(LeaveCommand))]
public partial bool LeaveEnabled { get; set; } = true;
```

and drop `_leaveEnabled`/manual property — `LeaveAsync` simply sets
`LeaveEnabled = false;` first. **Use this toolkit form in the implementation**
(the verbose form above exists only to show the equivalent semantics).

Add `using System.IO;` at the top.

- [ ] **Step 2: Rewire GameWindow**

`GameWindow.xaml` — board and bound controls:

```xml
<TextBlock x:Name="StatusText" Text="{Binding StatusText}" TextWrapping="Wrap" ... />

<ItemsControl x:Name="BoardGrid" ItemsSource="{Binding Cells}"
              Focusable="False" Margin="...">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <UniformGrid Rows="3" Columns="3" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Button FontSize="32" FontWeight="Bold"
                    Content="{Binding Mark}"
                    IsEnabled="{Binding IsEnabled}"
                    Command="{Binding DataContext.MoveCommand,
                              RelativeSource={RelativeSource AncestorType=Window}}"
                    CommandParameter="{Binding CellIndex}">
                <Button.Style>
                    <Style TargetType="Button">
                        <Setter Property="Background" Value="White" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsHighlighted}" Value="True">
                                <Setter Property="Background" Value="LightGoldenrodYellow" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Button.Style>
            </Button>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>

<Button x:Name="RematchButton" Content="{Binding RematchLabel}"
        IsEnabled="{Binding RematchEnabled}"
        Command="{Binding OfferRematchCommand}" ... />
<Button x:Name="LeaveButton" Content="Leave"
        Command="{Binding LeaveCommand}" ... />
<TextBox x:Name="OutputTextBox" Text="{Binding OutputText}" IsReadOnly="True" ... />
```

Window `Title="{Binding TitleText, Mode=OneWay}"` (remove code-behind Title set).

`GameWindow.xaml.cs` full replacement:

```csharp
using System.Windows;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;

namespace ClientServer.App;

public partial class GameWindow : Window
{
    private readonly PlayerSession _session;

    internal GameWindow(PlayerSession session)
    {
        _session = session;
        InitializeComponent();
        DataContext = new GameViewModel(session,
            new SynchronizationContextDispatcher(SynchronizationContext.Current!));
        session.ReturnedToLobby += _ => Dispatcher.BeginInvoke(Close);
    }

    protected override void OnClosed(EventArgs e)
    {
        _ = _session.LeaveRoomAsync();
        base.OnClosed(e);
    }
}
```

(VM subscriptions die with the VM; the session is disposed by the lobby's
existing close chain, so no manual unsubscription is needed.)

- [ ] **Step 3: Adjust affected UiTests (mechanical only)**

In `GameWindowTests`:
- Add helper + call it after construction in every test:

```csharp
    private void RealizeBoard()
    {
        _window.BoardGrid.Measure(new Size(300, 300));
        _window.BoardGrid.Arrange(new Rect(0, 0, 300, 300));
        _window.BoardGrid.UpdateLayout();
    }
```

- Replace `_window.BoardGrid.Children.OfType<Button>()` with
  `VisualTreeEx.FindChildren<Button>(_window.BoardGrid)` in the `Cells` property.
- `RematchButton.Content` assertions become `_window.RematchLabel`.
- Everything else (marks via Content, IsEnabled, Background from the
  DataTrigger, TryPress semantics, banner text, restore log, leave envelope)
  asserts identically.

- [ ] **Step 4: Add headless VM tests**

`tests/Client-Server-App.UiTests/ViewModels/GameViewModelTests.cs`:

```csharp
using ClientServer.App.ViewModels;
using ClientServer.TestSupport;
using System.Windows;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class GameViewModelTests : IDisposable
{
    private readonly PlayerSession _session;
    private readonly FakeClientTransport _transport;
    private readonly GameViewModel _vm;

    public GameViewModelTests()
    {
        (_session, _transport) = UiTestSession.ConnectSeatedAsync(mark: "X").GetAwaiter().GetResult();
        _vm = new GameViewModel(_session, new InlineDispatcher());
    }

    public void Dispose() => _session.Dispose();

    [Theory]
    [InlineData("inProgress", "X", 0, true)]
    [InlineData("inProgress", "O", 4, false)]
    [InlineData("won", "X", 0, false)]
    [InlineData("draw", "O", 8, false)]
    public void MoveCanExecute_MatchesTurnAndState(string status, string turn, int cell, bool expected)
    {
        SendState(turn, status);

        Assert.Equal(expected, _vm.MoveCommand.CanExecute(cell));
    }

    [Fact]
    public void ForfeitStatus_UsesWinnerName()
    {
        SendState(turn: "O", status: "won", winner: "O", winnerReason: "forfeit");

        Assert.Equal("O wins by forfeit.", _vm.StatusText);
    }

    [Fact]
    public void Title_DescribesSeats_WhenBothKnown_AndSpectatorVariant()
    {
        SendState(turn: "X", status: "inProgress");
        Assert.Contains("Alice (X) vs Bob (O)", _vm.TitleText);
    }

    [Fact]
    public void SpectatorSession_TitleHasSpectatorSuffix()
    {
        var (session, transport) = UiTestSession.ConnectSeatedAsync(mark: null).GetAwaiter().GetResult();
        try
        {
            GameViewModel vm = new(session, new InlineDispatcher());

            Assert.EndsWith(" (spectator)", vm.TitleText);
        }
        finally
        {
            session.Dispose();
        }
    }

    private void SendState(string turn, string status, string? winner = null,
        string? winnerReason = null)
    {
        string[] board = ["", "", "", "", "", "", "", "", ""];
        _transport.ReceiveLine(GameJson.Serialize(new GameStateRecord(
            board, turn, status, winner, null, 1, Room: "duel",
            XName: "Alice", OName: "Bob", WinnerReason: winnerReason)));
    }
}
```

(`MoveCanExecute(cell)` is the generated command's CanExecute delegate —
asserting it directly replaces pressing disabled buttons at VM level.)

- [ ] **Step 5: Validate and commit**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test
git add -A
git commit -m "refactor: extract game viewmodel"
```

---

### Task 5: Final sweep

**Files:**
- Modify: `README.md`

**Interfaces:** none.

- [ ] **Step 1: README architecture line**

Under the repository-layout table append:

```markdown
Windows follow MVVM: per-window ViewModels live in
`src/Client-Server-App/ViewModels/` (CommunityToolkit.Mvvm), and the windows'
code-behind only wires DataContext/lifecycle. UI behavior is covered twice:
headless ViewModel tests and STA-driven window tests (`tests/Client-Server-App.UiTests`).
```

- [ ] **Step 2: Full validation**

```bash
dotnet build Client-Server-App.slnx -c Release && ./scripts/run-tests.ps1
```

Gate must pass (≥83%); suite count = 122 Core+UI baseline plus ~20 new VM tests.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: note mvvm viewmodels in readme"
```

---

## Self-Review Notes

- Spec coverage: toolkit pin/dispatcher infra → Task 1; Connection+Server extraction incl. ctor-delegate contract & Cancel-on-close → Task 2; Lobby → Task 3; Game (+CellVm, DataTrigger highlight, hybrid code-behind grid generation replaced by ItemsControl) → Task 4; field-keyword preference honored via toolkit `[ObservableProperty]` partials + documented swap for LeaveEnabled; README/final gate → Task 5.
- Placeholders: none; every task carries complete file content or exact edits.
- Type consistency: `IUiDispatcher`, `InlineDispatcher`, `HeadlessWindow` untouched from prior work; command names (`ConnectCommand`, `HostCommand`, `CreateRoomCommand`, `JoinRoomCommand`, `MoveCommand`, `OfferRematchCommand`, `LeaveCommand`) consistent between XAML, VMs, and tests; `UiTestSession.ConnectSeatedAsync` reused unchanged.




