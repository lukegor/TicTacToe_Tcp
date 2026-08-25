# Logging Pipeline, Crash Safety Net & MTP Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Land the three Wave-1 infrastructure items from the approved spec: migrate tests onto Microsoft Testing Platform (MTP) with coverage, add a leveled logging pipeline with a rolling file sink, and add a global crash safety net — zero user-visible behavior change.

**Architecture:** Three phases matching the spec's three commits. Phase 2 injects optional `ILogger<T>` parameters into transports and game services while keeping the existing `Action<string>` log events untouched, so all windows keep working unchanged. Phase 3 hooks `Dispatcher` / `AppDomain` / `TaskScheduler` exceptions into the Phase-2 pipeline behind an injectable `ICrashReporter`.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, xunit.v3 4.x on MTP, `Microsoft.Extensions.Logging.Abstractions` 10.x, `Microsoft.Testing.Extensions.CodeCoverage` 18.x.

**Spec:** `docs/superpowers/specs/2026-08-25-logging-crash-mtp-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true` — every build must stay warning-free.
- Central package versions live only in `Directory.Packages.props`; test csproj references are version-less.
- New dependencies allowed in this work: `xunit.v3`, `Microsoft.Testing.Extensions.CodeCoverage`, `Xunit.StaFact` is NOT in scope, `Microsoft.Extensions.Logging.Abstractions`. Nothing else.
- File-scoped namespaces, sealed classes, no comments unless documenting non-obvious behavior (match existing style).
- All UI-affecting callbacks must marshal through `Dispatcher.BeginInvoke` (existing pattern).
- Existing tests may only be edited to fix xunit v2→v3 API deltas — never rewritten.
- Validation commands run from repo root: `dotnet build Client-Server-App.slnx -c Release` and `dotnet test`.

---

### Task 1: Migrate tests to xunit.v3 on MTP + coverage baseline

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj`
- Modify: any test file that fails v2→v3 API migration (expected: none or trivial)
- Modify: `docs/engineering-analysis.md` (record measured coverage baseline)

**Interfaces:**
- Consumes: nothing new.
- Produces: test suite runnable as an MTP executable; coverage command producing `TestResults/coverage.cobertura.xml`; recorded baseline number used later when CI (backlog R1) enforces the gate.

- [x] **Step 1: Swap package pins**

`Directory.Packages.props` ItemGroup becomes:

```xml
  <ItemGroup>
    <PackageVersion Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.10.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.0" />
  </ItemGroup>
```

(Removes `Microsoft.NET.Test.Sdk` 18.9.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.0.)

- [x] **Step 2: Make the test project an MTP executable**

`tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj` PropertyGroup gains `OutputType`; ItemGroup swaps refs:

```xml
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
```

- [x] **Step 3: Build and fix any v2→v3 API deltas**

```bash
dotnet build Client-Server-App.slnx -c Release
dotnet test
```

Expected deltas (fix if hit): `IAsyncLifetime.InitializeAsync/SetUpAsync` returns `ValueTask` instead of `Task`; `Assert.RaisesAsync` argument order. The suites use plain `[Fact]`/`[Theory]` plus hand-rolled fakes (`FakeClientTransport`, `FakeServerTransport`) and `GetAwaiter().GetResult()` blocking, so most likely nothing changes. Do not rewrite passing tests.

- [x] **Step 4: Collect coverage baseline**

.NET 10's `dotnet test` drives MTP directly (extension args need no `--`):

```bash
dotnet test --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

Open `tests/Client-Server-App.Tests/TestResults/coverage.cobertura.xml`, read the top-level `line-rate` attribute (statement coverage = value × 100).

- [x] **Step 5: Record the baseline**

In `docs/engineering-analysis.md`, under section R2, append:

```markdown
Measured baseline (2026-08-25): NN% statement coverage via
`dotnet test --coverage --coverage-output-format cobertura`.
Threshold gate lands with CI (R1) at NN-5%; TODO: areas below target — <list them>.
```

If NN ≥ 80 list only genuinely weak spots; if NN < 80 list the biggest uncovered types. Never pad tests to move the number.

- [x] **Step 6: Commit**

```bash
git add Directory.Packages.props tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj docs/engineering-analysis.md
git commit -m "test: migrate to xunit.v3 on MTP with code coverage"
```

---

### Task 2: Logging pipeline — LogConfig, FileLoggerProvider, service injection

**Files:**
- Create: `Client-Server-App/Diagnostics/LogConfig.cs`
- Create: `Client-Server-App/Diagnostics/FileLoggerProvider.cs`
- Modify: `Directory.Packages.props` (add `Microsoft.Extensions.Logging.Abstractions`)
- Modify: `Client-Server-App/Game/LobbyService.cs`
- Modify: `Client-Server-App/Game/PlayerSession.cs`
- Modify: `Client-Server-App/ServerTcp.cs`
- Modify: `Client-Server-App/ClientTcp.cs`
- Modify: `Client-Server-App/App.xaml.cs`
- Modify: `Client-Server-App/ConnectionWindow.xaml.cs` (call sites)
- Modify: `README.md` (log location note)
- Test (create): `tests/Client-Server-App.Tests/Diagnostics/FileLoggerProviderTests.cs`
- Test (create): `tests/Client-Server-App.Tests/TestDoubles/CapturingLogger.cs`
- Test (create): `tests/Client-Server-App.Tests/Diagnostics/ServiceLoggingTests.cs`

**Interfaces:**
- Consumes: `LobbyService(IServerTransport, TimeSpan?, ILogger<LobbyService>?)` etc. after this task.
- Produces:
  - `internal sealed record LogConfig(LogLevel MinimumLevel = LogLevel.Information, string? Directory = null)`
  - `internal sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable` — ctor `FileLoggerProvider(LogConfig config)`
  - `App.LoggerFactory : ILoggerFactory` (static, initialized in `OnStartup`, disposed in `OnExit`)
  - Optional last parameter `ILogger<T>? logger = null` on `LobbyService`, `PlayerSession`, `ServerTcp`, `ClientTcp` constructors.
  - Room is NOT changed: all room activity already flows through `LobbyService.Log`, which now emits both sinks.

- [x] **Step 1: Add the abstractions pin**

In `Directory.Packages.props` ItemGroup add:

```xml
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
```

Add `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` to `Client-Server-App/Client-Server-App.csproj`.

- [x] **Step 2: Implement LogConfig**

Create `Client-Server-App/Diagnostics/LogConfig.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Client_Server_App.Diagnostics;

/// <summary>Immutable logging settings resolved once at startup.</summary>
internal sealed record LogConfig(LogLevel MinimumLevel = LogLevel.Information, string? Directory = null);
```

- [x] **Step 3: Implement FileLoggerProvider**

Create `Client-Server-App/Diagnostics/FileLoggerProvider.cs`:

```csharp
using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Client_Server_App.Diagnostics;

/// <summary>Writes formatted log lines to one file per process session through a
/// single background writer; prunes old session files at startup.</summary>
internal sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private const int RetainedFiles = 5;
    private readonly LogConfig _config;
    private readonly Channel<string> _queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private readonly string _filePath;

    public FileLoggerProvider(LogConfig config)
    {
        _config = config;
        string directory = config.Directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Client-Server-App",
                "logs");
        Directory.CreateDirectory(directory);
        PruneOldSessions(directory);
        _filePath = Path.Combine(directory, $"{DateTimeOffset.Now:yyyy-MM-dd_HH-mm-ss}.log");
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task WriteLoopAsync()
    {
        using StreamWriter writer = new(_filePath, append: true) { AutoFlush = false };
        await foreach (string line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            if (_queue.Reader.Count == 0)
            {
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static void PruneOldSessions(string directory)
    {
        // Timestamped names make lexical order chronological.
        List<FileInfo> files = [.. new DirectoryInfo(directory)
            .EnumerateFiles("*.log")
            .OrderByDescending(static f => f.Name)];
        foreach (FileInfo stale in files.Skip(RetainedFiles))
        {
            try
            {
                stale.Delete();
            }
            catch (IOException)
            {
                // A concurrent process may hold the file; retention is best-effort.
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel is not LogLevel.None && logLevel >= owner._config.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line =
                $"{DateTimeOffset.Now:HH:mm:ss.fff} [{logLevel.ToString().ToUpperInvariant(),-7}] [{category}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            owner.Enqueue(line);
        }
    }
}
```

- [x] **Step 4: Composition root — build the factory in App**

`Client-Server-App/App.xaml.cs` becomes:

```csharp
using System.Windows;
using Client_Server_App.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Client_Server_App;

public partial class App : Application
{
    internal static ILoggerFactory LoggerFactory { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(new LogConfig())));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggerFactory.Dispose();
        base.OnExit(e);
    }
}
```

(The qualified `Microsoft.Extensions.Logging.LoggerFactory.Create` avoids colliding with the property name.)

- [x] **Step 5: Inject optional loggers into the four services**

`LobbyService.cs`:

```csharp
using Microsoft.Extensions.Logging;
```

Constructor and field:

```csharp
    private readonly ILogger<LobbyService> _logger;

    public LobbyService(IServerTransport server, TimeSpan? gracePeriod = null, ILogger<LobbyService>? logger = null)
    {
        _server = server;
        _gracePeriod = gracePeriod ?? DefaultGracePeriod;
        _logger = logger ?? NullLogger<LobbyService>.Instance;
    }
```

The private `Log` helper (currently `private void Log(string message) => LogReceived?.Invoke(message);`) becomes dual-emission:

```csharp
    private void Log(string message)
    {
        _logger.LogInformation("{Message}", message);
        LogReceived?.Invoke(message);
    }
```

`PlayerSession.cs`: same pattern — field `ILogger<PlayerSession>`, optional trailing parameter `ILogger<PlayerSession>? logger = null`. Upgrade the two reconnect-path strings to `LogWarning` ("Connection lost - rejoining...") and keep the other call sites at `Information`; every existing `LogReceived?.Invoke(...)` line gains a sibling `_logger.LogXxx(...)` above it.

`ServerTcp.cs` / `ClientTcp.cs`: add the same optional trailing `ILogger<ServerTcp>? logger = null` / `ILogger<ClientTcp>? logger = null` constructor parameters (defaulting to `NullLogger<...>.Instance`) plus Debug-level entries at lifecycle points only:
- `ServerTcp.Start()` → `"Referee listening on port {Port}."`
- accept callback per connection → `"Client {Id} connected."`
- disconnect raise site → `"Client {Id} disconnected."`
- `ClientTcp.ConnectAsync` success/failure → `"Connected to {Host}:{Port}."` / `"Connect to {Host}:{Port} failed."`
No behavior changes; events stay exactly as they are.

- [x] **Step 6: Update composition-root call sites**

`ConnectionWindow.xaml.cs` — three constructions gain logger arguments:

```csharp
        ClientTcp client = new(App.LoggerFactory.CreateLogger<ClientTcp>());
```

```csharp
                ClientTcp fresh = new(App.LoggerFactory.CreateLogger<ClientTcp>());
```

```csharp
        PlayerSession session = new(
            ConnectFactory,
            displayName: NameTextBox.Text,
            logger: App.LoggerFactory.CreateLogger<PlayerSession>());
```

```csharp
        ServerTcp server = new(port, App.LoggerFactory.CreateLogger<ServerTcp>());
```

```csharp
            LobbyService lobby = new(server, logger: App.LoggerFactory.CreateLogger<LobbyService>());
```

All other construction sites are tests using positional/optional args — unchanged.

- [x] **Step 7: Test doubles and tests**

Create `tests/Client-Server-App.Tests/TestDoubles/CapturingLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class CapturingLogger(string category) : ILogger
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public CapturingLogger Inner { get; } = new(typeof(T).Name);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => Inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Inner.Log(logLevel, eventId, state, exception, formatter);
}
```

Add `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` to the test csproj.

Create `tests/Client-Server-App.Tests/Diagnostics/FileLoggerProviderTests.cs`:

```csharp
using System.IO;
using Client_Server_App.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Client_Server_App.Tests.Diagnostics;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "csa-log-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Writes_formatted_lines_and_flushes_on_dispose()
    {
        await using FileLoggerProvider provider = new(new LogConfig(Directory: _dir));
        ILogger logger = provider.CreateLogger("test");

        logger.LogInformation("hello {Who}", "alice");
        await provider.DisposeAsync();

        string text = File.ReadAllText(FindSingleFile());
        Assert.Contains("[INFO   ]", text);
        Assert.Contains("[test] hello alice", text);
    }

    [Fact]
    public async Task Filters_below_minimum_level()
    {
        await using FileLoggerProvider provider =
            new(new LogConfig(LogLevel.Warning, Directory: _dir));
        ILogger logger = provider.CreateLogger("test");

        logger.LogInformation("ignored");
        await provider.DisposeAsync();

        Assert.DoesNotContain("ignored", File.ReadAllText(FindSingleFile()));
    }

    [Fact]
    public void Prunes_to_newest_five_session_files()
    {
        for (int i = 1; i <= 7; i++)
        {
            File.WriteAllText(Path.Combine(_dir, $"2020-01-0{i}_00-00-00.log"), "old");
        }

        await using (FileLoggerProvider _ = new(new LogConfig(Directory: _dir)))
        {
        }

        Assert.Equal(6, Directory.GetFiles(_dir, "*.log").Length); // 5 retained + current session file
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FindSingleFile() =>
        Assert.Single(Directory.GetFiles(_dir, "*.log"));
}
```

Create `tests/Client-Server-App.Tests/Diagnostics/ServiceLoggingTests.cs`:

```csharp
using Client_Server_App.Game;
using Client_Server_App.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Client_Server_App.Tests.Diagnostics;

public sealed class ServiceLoggingTests
{
    [Fact]
    public void Lobby_hello_logs_information_alongside_text_event()
    {
        FakeServerTransport transport = new();
        CapturingLogger<LobbyService> logger = new();
        string[] eventLines = [];
        LobbyService lobby = new(transport, logger: logger);
        lobby.LogReceived += message => eventLines = [.. eventLines, message];
        lobby.Start();

        Guid id = transport.SimulateClientConnected();
        transport.ReceiveLine(id, GameJson.Serialize(new HelloRecord("Alice")));

        Assert.Contains(logger.Inner.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("Alice"));
        Assert.Single(eventLines);
    }
}
```

- [x] **Step 8: README note**

Under "Build and run" in `README.md` append:

```markdown
Session logs are written to `%LOCALAPPDATA%\Client-Server-App\logs` (one file per
run; the newest five are kept).
```

- [x] **Step 9: Validate and commit**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test && git add -A && git commit -m "feat: leveled logging pipeline with rolling file sink"
```

(Stage explicitly if `git add -A` would sweep unrelated files.)

---

### Task 3: Crash safety net

**Files:**
- Create: `Client-Server-App/Diagnostics/CrashHandler.cs` (includes `ICrashReporter`)
- Create: `Client-Server-App/Diagnostics/MessageBoxCrashReporter.cs`
- Modify: `Client-Server-App/App.xaml.cs` (attach hooks)
- Test (create): `tests/Client-Server-App.Tests/Diagnostics/CrashHandlerTests.cs`

**Interfaces:**
- Consumes: `App.LoggerFactory` from Task 2.
- Produces:
  - `internal interface ICrashReporter { void Report(Exception exception); }` — performs dialog + any clipboard/folder side effects itself.
  - `internal sealed class CrashHandler` — ctor `(ILogger<CrashHandler>, ICrashReporter)`; methods `Attach()`, `bool Handle(string origin, Exception exception, bool fatal)`; returns whether a fatal report was performed. Reentrancy-safe.

- [x] **Step 1: Implement CrashHandler**

Create `Client-Server-App/Diagnostics/CrashHandler.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Client_Server_App.Diagnostics;

/// <summary>Performs crash-dialog follow-ups (copy details, open folder).</summary>
internal interface ICrashReporter
{
    void Report(Exception exception);
}

/// <summary>Global crash net. Fatal escapes (UI dispatcher, terminating AppDomain)
/// produce a critical entry, a user-facing report, then caller-initiated shutdown.
/// Unobserved task exceptions are logged as errors without taking the process down
/// (they are not fatal since .NET 4.5). A fatal hit latches the handler so
/// cascading failures during teardown cannot re-open dialogs.</summary>
internal sealed class CrashHandler(ILogger<CrashHandler> logger, ICrashReporter reporter)
{
    private bool _handling;

    public bool Handle(string origin, Exception exception, bool fatal)
    {
        if (_handling)
        {
            return false;
        }

        try
        {
            if (fatal)
            {
                _handling = true;
                logger.LogCritical(exception, "Unhandled exception at {Origin}; shutting down.", origin);
                reporter.Report(exception);
                return true;
            }

            logger.LogError(exception, "Unobserved task exception at {Origin}.", origin);
            return false;
        }
        finally
        {
            if (!fatal)
            {
                _handling = false;
            }
        }
    }
}
```

Create `Client-Server-App/Diagnostics/MessageBoxCrashReporter.cs`:

```csharp
using System.IO;
using System.Windows;

namespace Client_Server_App.Diagnostics;

/// <summary>Yes copies details to the clipboard, No opens the log folder,
/// Cancel just closes. The application shuts down afterwards either way.</summary>
internal sealed class MessageBoxCrashReporter : ICrashReporter
{
    public void Report(Exception exception)
    {
        MessageBoxResult choice = MessageBox.Show(
            $"An unexpected error occurred and the application must close.\n\n{exception.Message}\n\n"
            + "Yes: copy details · No: open log folder · Cancel: close",
            "Unexpected error",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Error);

        switch (choice)
        {
            case MessageBoxResult.Yes:
                Clipboard.SetDataObject(exception.ToString());
                break;
            case MessageBoxResult.No:
                string logs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Client-Server-App",
                    "logs");
                if (Directory.Exists(logs))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{logs}\"") { UseShellExecute = true });
                }

                break;
        }
    }
}
```

- [x] **Step 2: Attach hooks in App.xaml.cs**

Extend `App` from Task 2 (add `using Client_Server_App.Diagnostics;` and a `_crash` field). The full startup section:

```csharp
    private CrashHandler? _crash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(new LogConfig())));

        _crash = new CrashHandler(LoggerFactory.CreateLogger<CrashHandler>(), new MessageBoxCrashReporter());
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (_crash.Handle("UI dispatcher", args.Exception, fatal: true))
            {
                Current.Shutdown();
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _crash.Handle("AppDomain", (Exception)args.ExceptionObject, fatal: true);
            if (args.IsTerminating)
            {
                Environment.Exit(1);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _crash.Handle("UnobservedTask", args.Exception, fatal: false);
            args.SetObserved();
        };
    }
```

Notes: the terminating AppDomain path exits with a failure code because WPF teardown may itself be broken; `SetObserved()` prevents the secondary finalizer-thread crash for the logged exception.

- [x] **Step 3: Tests**

Create `tests/Client-Server-App.Tests/Diagnostics/CrashHandlerTests.cs`:

```csharp
using Client_Server_App.Diagnostics;
using Client_Server_App.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Client_Server_App.Tests.Diagnostics;

public sealed class CrashHandlerTests
{
    private sealed class RecordingReporter(Action? onReport = null) : ICrashReporter
    {
        public int Calls { get; private set; }

        public void Report(Exception exception)
        {
            Calls++;
            onReport?.Invoke();
        }
    }

    [Fact]
    public void Fatal_logs_critical_reports_and_returns_true()
    {
        CapturingLogger<CrashHandler> logger = new();
        RecordingReporter reporter = new();
        CrashHandler handler = new(logger, reporter);

        bool handled = handler.Handle("test", new InvalidOperationException("boom"), fatal: true);

        Assert.True(handled);
        Assert.Equal(1, reporter.Calls);
        Assert.Contains(logger.Inner.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public void Non_fatal_logs_error_without_report()
    {
        CapturingLogger<CrashHandler> logger = new();
        RecordingReporter reporter = new();
        CrashHandler handler = new(logger, reporter);

        bool handled = handler.Handle("task", new InvalidOperationException("slip"), fatal: false);

        Assert.False(handled);
        Assert.Equal(0, reporter.Calls);
        Assert.Contains(logger.Inner.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Second_fatal_during_report_is_suppressed()
    {
        CapturingLogger<CrashHandler> logger = new();
        CrashHandler handler = null!;
        RecordingReporter reporter = new(onReport: () =>
            Assert.False(handler.Handle("nested", new Exception(), fatal: true)));
        handler = new CrashHandler(logger, reporter);

        bool handled = handler.Handle("outer", new InvalidOperationException(), fatal: true);

        Assert.True(handled);
        Assert.Equal(1, reporter.Calls);
    }

    [Fact]
    public void Non_fatal_does_not_latch_handler()
    {
        CapturingLogger<CrashHandler> logger = new();
        RecordingReporter reporter = new();
        CrashHandler handler = new(logger, reporter);

        handler.Handle("a", new Exception(), fatal: false);
        handler.Handle("b", new Exception(), fatal: false);

        Assert.Equal(2, logger.Inner.Entries.Count);
    }
}
```

- [x] **Step 4: Manual smoke check**

Run one instance, click Create Host, then kill it via a deliberately thrown exception is NOT acceptable — instead verify wiring by launching normally, confirming no dialog appears, closing cleanly, and checking today's session log contains the referee listening entry from Task 2. (Automated coverage of the WPF hook registration itself is out of reach without UI automation; the decision logic is fully unit-tested.)

- [x] **Step 5: Validate and commit**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test && git add -A && git commit -m "feat: global crash safety net with graceful shutdown"
```

---

## Self-Review Notes

- Spec coverage: Phase 1 → Task 1; Phase 2 (LogConfig/FileLoggerProvider/injection/UI-compat/exclusions) → Task 2; Phase 3 (hooks table policy, flow, accepted limit) → Tasks 3 Steps 1–2; testing matrix rows map to Task 2 Step 7 and Task 3 Step 3; rollout commits 1–3 match the three task commits; README note included.
- No placeholders; all code shown in full.
- Type consistency: `CapturingLogger<T>.Inner`, `LogConfig(LogLevel, string?)`, `FileLoggerProvider(LogConfig)`, `CrashHandler(ILogger<CrashHandler>, ICrashReporter)` used identically across tasks.

