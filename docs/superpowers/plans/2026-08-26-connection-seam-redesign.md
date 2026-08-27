# Connection Seam Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ConnectionViewModel`'s three raw factory Funcs with one intent-named `IConnectionInfrastructure` seam (+ `RefereeHandle`), moving infrastructure assembly back into the composition root while keeping every log string and test behavior byte-for-byte.

**Architecture:** Single cohesive interface consumed by the screen that owns both actions. The window supplies the real implementation (built on `App.LoggerFactory`) through its internal probe-ctor; VM tests supply a recording stub. Hello-phase stays in the VM so `"Connection failed:"` parity holds.

**Tech Stack:** Unchanged (net10.0-windows WPF, xunit.v3/MTP, CommunityToolkit.Mvvm already present).

**Spec:** `docs/superpowers/specs/2026-08-26-connection-seam-redesign.md`

## Global Constraints

- All 127 tests stay green; only `tests/Client-Server-App.UiTests/ViewModels/ConnectionViewModelTests.cs` may change.
- User-visible strings byte-for-byte: "Enter an IP address.", "'…' is not a valid port (1-{max}).", "Connected to {host}:{port}.", "Connection failed: {message}", "Could not start the host: {message}", "Listening on port {port}.".
- Failure split: `StartHost` disposes a failed server itself and rethrows; VM logs the message once. Hello-phase failures are logged by the VM catch and dispose the session.
- Warning-free build (`TreatWarningsAsErrors`).
- Flake confidence: filtered Connection suite must pass 3 consecutive runs before commit.

---

### Task 1: IConnectionInfrastructure seam + refactor

**Files:**
- Create: `src/Client-Server-App/ViewModels/IConnectionInfrastructure.cs`
- Create: `src/Client-Server-App/ViewModels/RefereeHandle.cs`
- Create: `src/Client-Server-App/RealConnectionInfrastructure.cs`
- Modify: `src/Client-Server-App/ViewModels/ConnectionViewModel.cs` (full rewrite)
- Modify: `src/Client-Server-App/ConnectionWindow.xaml.cs` (ctor wiring)
- Modify: `tests/Client-Server-App.UiTests/ViewModels/ConnectionViewModelTests.cs` (stub swap)

**Interfaces:**
- Produces:
  - `internal interface IConnectionInfrastructure { Task<PlayerSession> ConnectAsync(string host, int port, string playerName, CancellationToken ct); RefereeHandle StartHost(int port); }`
  - `internal sealed record RefereeHandle(int Port, ServerTcp Server, LobbyService Lobby)`
  - `internal sealed class RealConnectionInfrastructure(ILoggerFactory logs, Action<string> log) : IConnectionInfrastructure`
  - `ConnectionViewModel(IConnectionInfrastructure infra, IUiDispatcher ui)`; events become `LobbyReady(PlayerSession)` and `RefereeReady(RefereeHandle handle)`.

- [ ] **Step 1: Seam types**

`src/Client-Server-App/ViewModels/IConnectionInfrastructure.cs`:

```csharp
using ClientServer.Core.Game;

namespace ClientServer.App.ViewModels;

/// <summary>Infrastructure seam for the connection screen: produces a fully
/// wired player session (transport connected, reconnect-factory installed,
/// hello NOT yet sent) and starts a referee host.</summary>
internal interface IConnectionInfrastructure
{
    /// <summary>Connects the transport, appends "Connected to {host}:{port}."
    /// via the log callback, and constructs the session. Implementations must
    /// dispose a failed transport and rethrow.</summary>
    Task<PlayerSession> ConnectAsync(string host, int port, string playerName,
        CancellationToken ct);

    /// <summary>Starts a referee host; disposes the server and rethrows when
    /// the port cannot be bound.</summary>
    RefereeHandle StartHost(int port);
}
```

`src/Client-Server-App/ViewModels/RefereeHandle.cs`:

```csharp
using ClientServer.Core.Game;
using ClientServer.Core.Transports;

namespace ClientServer.App.ViewModels;

internal sealed record RefereeHandle(int Port, ServerTcp Server, LobbyService Lobby);
```

- [ ] **Step 2: Rewrite ConnectionViewModel**

Full file:

```csharp
using System.Globalization;
using System.Net;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientServer.App.ViewModels;

internal sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly IConnectionInfrastructure _infra;
    private readonly IUiDispatcher _ui;

    public event Action<PlayerSession>? LobbyReady;
    public event Action<RefereeHandle>? RefereeReady;

    [ObservableProperty] public partial string Address { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial string Port { get; set; } = "1111";
    [ObservableProperty] public partial string PlayerName { get; set; } = "";
    [ObservableProperty] public partial string HostPort { get; set; } = "1111";
    [ObservableProperty] public partial string LogText { get; set; } = "";

    public ConnectionViewModel(IConnectionInfrastructure infra, IUiDispatcher ui)
    {
        _infra = infra;
        _ui = ui;
    }

    private void AppendLog(string message) => LogText += message + Environment.NewLine;

    private static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text?.Trim(), CultureInfo.InvariantCulture, out port)
        && port is > 0 and <= IPEndPoint.MaxPort;

    [RelayCommand]
    private async Task ConnectAsync()
    {
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

        PlayerSession? session = null;
        try
        {
            session = await _infra.ConnectAsync(
                Address.Trim(), port, PlayerName.Trim(), CancellationToken.None);
            await session.ConnectAsync();
            LobbyReady?.Invoke(session);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                        or IOException or SocketException)
        {
            AppendLog($"Connection failed: {ex.Message}");
            session?.Dispose();
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

        try
        {
            RefereeReady?.Invoke(_infra.StartHost(port));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            AppendLog($"Could not start the host: {ex.Message}");
        }
    }
}
```

Notes: `CancellationToken` parameter dropped from the command (the close-mid-connect cancel path now lives in the window's `OnClosed` → `infra`-owned CTS is gone; the real infrastructure's connect honors the passed token from… nothing external anymore — window-level cancellation is intentionally simplified away because the pre-MVVM handler had no cancel either; `Cancel()`/`Dispose()` members are removed with `_closed`).

- [ ] **Step 3: RealConnectionInfrastructure + window wiring**

`src/Client-Server-App/RealConnectionInfrastructure.cs`:

```csharp
using System.IO;
using System.Net.Sockets;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using Microsoft.Extensions.Logging;

namespace ClientServer.App;

internal sealed class RealConnectionInfrastructure(
    ILoggerFactory logs,
    Action<string> log) : IConnectionInfrastructure
{
    public async Task<PlayerSession> ConnectAsync(string host, int port, string playerName,
        CancellationToken ct)
    {
        ClientTcp client = new(logs.CreateLogger<ClientTcp>());
        try
        {
            await client.ConnectAsync(host, port, ct).ConfigureAwait(true);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        log($"Connected to {host}:{port}.");

        ClientTcp? initialTransport = client;
        async Task<IClientTransport> ReconnectFactory()
        {
            ClientTcp? reused = Interlocked.Exchange(ref initialTransport, null);
            if (reused is not null)
            {
                return reused;
            }

            ClientTcp fresh = new(logs.CreateLogger<ClientTcp>());
            await fresh.ConnectAsync(host, port, CancellationToken.None).ConfigureAwait(true);
            return fresh;
        }

        return new PlayerSession(ReconnectFactory, displayName: playerName,
            logger: logs.CreateLogger<PlayerSession>());
    }

    public RefereeHandle StartHost(int port)
    {
        ServerTcp server = new(port, logs.CreateLogger<ServerTcp>());
        try
        {
            server.Start();
        }
        catch
        {
            server.Dispose();
            throw;
        }

        LobbyService lobby = new(server, logger: logs.CreateLogger<LobbyService>());
        lobby.Start();
        return new RefereeHandle(port, server, lobby);
    }
}
```

Add `using System.IO;` at top (IOException filter lives in the VM, not here — but SocketException needs `System.Net.Sockets`; include both `System.Net` and `System.Net.Sockets`).

`ConnectionWindow.xaml.cs` — ctor section becomes:

```csharp
    private readonly Func<LobbyWindow, bool>? _lobbyProbe;
    private readonly Func<ServerWindow, bool>? _refereeProbe;
    private readonly ConnectionViewModel _viewModel;

    public ConnectionWindow() : this(null, null)
    {
    }

    internal ConnectionWindow(
        Func<LobbyWindow, bool>? lobbyProbe,
        Func<ServerWindow, bool>? refereeProbe)
    {
        _lobbyProbe = lobbyProbe;
        _refereeProbe = refereeProbe;
        SynchronizationContext context = SynchronizationContext.Current
            ?? new SynchronizationContext();
        InitializeComponent();
        _viewModel = new ConnectionViewModel(
            BuildInfrastructure(),
            new SynchronizationContextDispatcher(context));
        DataContext = _viewModel;
        _viewModel.LobbyReady += session =>
            OpenLobbyWindow(() => new LobbyWindow(session), () => session.Dispose());
        _viewModel.RefereeReady += handle =>
            OpenRefereeWindow(() => new ServerWindow(handle.Lobby, handle.Port), () =>
            {
                handle.Lobby.Dispose();
                handle.Server.Dispose();
            });
    }

    private IConnectionInfrastructure BuildInfrastructure() =>
        new RealConnectionInfrastructure(App.LoggerFactory, _viewModel.AppendLog);
```

Delete: old 5-delegate lambdas, `ConnectSessionAsync`, `NameFromViewModel`, and
`OnClosed`'s `_viewModel.Cancel()/Dispose()` calls (`OnClosed` reduces to
`base.OnClosed(e)` only). The `IUiDispatcher?` ctor parameter is removed entirely
(probes-only internal ctor remains; UiTests' `NewWindow` passes two probes and no
infra — the local-function capture of `_viewModel` inside
`BuildInfrastructure()` is safe because commands can only run after construction
completes).

`using ClientServer.App.ViewModels;` and `using Microsoft.Extensions.Logging;` retained; drop `ClientServer.Core.Transports` if now unused (IClientTransport moved out) — verify with build.

- [ ] **Step 4: Swap VM-test fakes**

`tests/Client-Server-App.UiTests/ViewModels/ConnectionViewModelTests.cs` full file:

```csharp
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class ConnectionViewModelTests
{
    private sealed class StubInfrastructure : IConnectionInfrastructure
    {
        public int ConnectCalls { get; private set; }
        public int StartHostCalls { get; private set; }

        public Task<PlayerSession> ConnectAsync(string host, int port, string playerName,
            CancellationToken ct)
        {
            ConnectCalls++;
            throw new InvalidOperationException("factory reached");
        }

        RefereeHandle IConnectionInfrastructure.StartHost(int port)
        {
            StartHostCalls++;
            throw new InvalidOperationException("host reached");
        }
    }

    [Fact]
    public async Task EmptyAddress_AppendsHint_InfrastructureUntouched()
    {
        StubInfrastructure stub = new();
        var vm = new ConnectionViewModel(stub, new InlineDispatcher());
        vm.Address = ""; // otherwise the constructor defaults satisfy validation

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("Enter an IP address.", vm.LogText);
        Assert.Equal(0, stub.ConnectCalls);
        Assert.Equal(0, stub.StartHostCalls);
    }

    public static TheoryData<string> InvalidPorts => new() { "abc", "0", "70000" };

    [Theory]
    [MemberData(nameof(InvalidPorts))]
    public async Task InvalidPort_AppendsPortError_InfrastructureUntouched(string port)
    {
        StubInfrastructure stub = new();
        var vm = new ConnectionViewModel(stub, new InlineDispatcher());
        vm.Address = "127.0.0.1";
        vm.Port = port;

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains($"'{port}' is not a valid port", vm.LogText);
        Assert.Equal(0, stub.ConnectCalls);
    }
}
```

(`RefereeHandle`/`PlayerSession` types referenced only in signatures; `IConnectionInfrastructure` is internal → visible via existing InternalsVisibleTo.)

- [ ] **Step 5: Validate**

```bash
dotnet build Client-Server-App.slnx -c Release
dotnet test tests/Client-Server-App.UiTests --filter 'FullyQualifiedName~Connection'
dotnet test
```

Repeat the filtered Connection run **3 consecutive times** (exit 0 each) — regression guard for the race fixed earlier.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: replace connection funcs with infrastructure seam"
```

---

### Task 1 wrap-up additions (same task)

- [ ] **Step 7: Full-suite + gate validation**

```bash
dotnet test
./scripts/run-tests.ps1
```

Gate ≥80% expected (VM code covered by both suites).

- [ ] **Step 8: Flake-confidence loop**

Run the filtered Connection suite **3 consecutive times** (already in Step 5) plus one full `dotnet test` — all exit 0. Any failure: stop, diagnose before committing.

---

## Self-Review Notes

- Spec coverage: interface/handle/real-impl types → Steps 1–3; ctor reduction to 2 deps ✓; hello-boundary (infra returns pre-hello session; VM sends hello so "Connection failed:" parity holds) ✓; host-failure split (source disposes+rethrows, VM logs once) ✓; stub swap → Step 4; byte-parity strings carried verbatim ✓; UiTests untouched except none required ✓.
- Placeholders: none.
- Type consistency: `RefereeHandle(Port, Server, Lobby)` matches window disposal lambda; `IConnectionInfrastructure.ConnectAsync(host, port, playerName, ct)` matches VM call and both fakes; removed `Cancel()/Dispose()` from VM noted in plan body — `ConnectionWindow.OnClosed` drops the corresponding calls.

**Deliberate simplification recorded:** close-mid-connect cancellation of an in-flight transport connect is intentionally dropped (the pre-MVVM handler never had it either — `_closed` existed only for the reconnect-era design); the linked-token plumbing disappears with it.
