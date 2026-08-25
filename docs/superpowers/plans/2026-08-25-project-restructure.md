# Project Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Split the solution into portable `Client-Server-App.Core` (net10.0) plus a thin WPF shell under `src/`, align namespaces with assembly names, and add a win-x64 single-file publish profile — every commit buildable and 84/84 green.

**Architecture:** Three commits mirroring the spec's migration sequence: (1) physical extraction keeping old namespaces so the diff is pure moves, (2) mechanical namespace sweep, (3) publish profile. The single enforced boundary is App → Core; tests compile against Core alone as `net10.0`, making any Windows coupling in engine code a restore failure on every OS.

**Tech Stack:** .NET 10 SDK, WPF (shell only), xunit.v3/MTP, central package management.

**Spec:** `docs/superpowers/specs/2026-08-25-project-restructure-design.md`

## Global Constraints

- `TreatWarningsAsErrors` / `EnforceCodeStyleInBuild` stay global in `Directory.Build.props`; builds remain warning-free.
- Core TFM is exactly `net10.0` (no `-windows`, no `UseWPF`); it references no project.
- Test assembly name stays `Client-Server-App.Tests` (Core's `InternalsVisibleTo` depends on it).
- No test logic changes anywhere in this plan.
- Moves use `git mv` to preserve history; renames live in their own commit.
- Validation per commit: `dotnet build Client-Server-App.slnx -c Release` then `dotnet test` (84/84).

---

### Task 1: Extract portable core library

**Files:**
- Create: `src/Client-Server-App.Core/Client-Server-App.Core.csproj`
- Move (`git mv`): `Client-Server-App/Game/*.cs` → `src/Client-Server-App.Core/Game/`
- Move (`git mv`): `Client-Server-App/ServerTcp.cs`, `Client-Server-App/ClientTcp.cs` → `src/Client-Server-App.Core/Transports/`
- Move (`git mv`): `Client-Server-App/Diagnostics/{LogConfig,FileLoggerProvider,SingleProviderLoggerFactory,CrashHandler}.cs` → `src/Client-Server-App.Core/Diagnostics/`
- Move (`git mv`): whole `Client-Server-App/` folder → `src/Client-Server-App/`
- Keep in place: `src/Client-Server-App/Diagnostics/MessageBoxCrashReporter.cs`
- Modify: `Directory.Build.props`, both remaining csproj files, `Client-Server-App.slnx`, `Dockerfile`, `README.md`

**Interfaces:**
- Produces: `Client-Server-App.Core.dll` exposing all engine types under *unchanged* namespaces (`Client_Server_App.Game`, `Client_Server_App`, `Client_Server_App.Diagnostics`) — zero source edits inside moved files during this task.

- [x] **Step 1: Create the Core project**

`src/Client-Server-App.Core/Client-Server-App.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Client_Server_App</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <InternalsVisibleTo Include="Client-Server-App.Tests" />
  </ItemGroup>

</Project>
```

(`RootNamespace` keeps the old value for this task; Task 2 changes it.)

- [x] **Step 2: Move sources**

```bash
New-Item -ItemType Directory -Force src\Client-Server-App.Core\Game, src\Client-Server-App.Core\Transports, src\Client-Server-App.Core\Diagnostics | Out-Null
git mv Client-Server-App/Game/TicTacToe.cs Client-Server-App/Game/Room.cs Client-Server-App/Game/LobbyService.cs Client-Server-App/Game/PlayerSession.cs Client-Server-App/Game/GameEnvelope.cs Client-Server-App/Game/GameJson.cs Client-Server-App/Game/Transports.cs src/Client-Server-App.Core/Game/
git mv Client-Server-App/ServerTcp.cs Client-Server-App/ClientTcp.cs src/Client-Server-App.Core/Transports/
git mv Client-Server-App/Diagnostics/LogConfig.cs Client-Server-App/Diagnostics/FileLoggerProvider.cs Client-Server-App/Diagnostics/SingleProviderLoggerFactory.cs Client-Server-App/Diagnostics/CrashHandler.cs src/Client-Server-App.Core/Diagnostics/
git mv Client-Server-App src/Client-Server-App
```

Note: `Game/Transports.cs` holds the two transport *interfaces*; it moves again into `Transports/` during Task 2 per the spec map.

- [x] **Step 3: Rewire the app project**

`src/Client-Server-App/Client-Server-App.csproj` becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RootNamespace>Client_Server_App</RootNamespace>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Client-Server-App.Core\Client-Server-App.Core.csproj" />
  </ItemGroup>

</Project>
```

(The previous `InternalsVisibleTo` item is deleted — tests stop referencing this assembly.)

- [x] **Step 4: Retarget tests at Core only**

`tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj` becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>Client_Server_App.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Client-Server-App.Core\Client-Server-App.Core.csproj" />
  </ItemGroup>

</Project>
```

(`UseWPF` and the app `ProjectReference` are gone.)

- [x] **Step 5: Shared props, solution, Dockerfile, README**

`Directory.Build.props`: delete the line `<TargetFramework>net10.0-windows</TargetFramework>`; everything else stays.

`Client-Server-App.slnx` becomes:

```xml
<Solution>
  <Project Path="src/Client-Server-App.Core/Client-Server-App.Core.csproj" />
  <Project Path="src/Client-Server-App/Client-Server-App.csproj" />
  <Folder Name="/tests/">
    <Project Path="tests/Client-Server-App.Tests/Client-Server-App.Tests.csproj" />
  </Folder>
</Solution>
```

`Dockerfile`: replace the copy/restore block and the publish path:

```dockerfile
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/Client-Server-App.Core/Client-Server-App.Core.csproj", "src/Client-Server-App.Core/"]
COPY ["src/Client-Server-App/Client-Server-App.csproj", "src/Client-Server-App/"]
RUN dotnet restore "src/Client-Server-App/Client-Server-App.csproj"
```

```dockerfile
RUN dotnet publish "src/Client-Server-App/Client-Server-App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:TreatWarningsAsErrors=true `
    -o /app
```

(`ENTRYPOINT ["Client-Server-App.exe"]` unchanged.)

`README.md` repository-layout table rows become:

```markdown
| `src/Client-Server-App.Core/` | Portable engine: game rules, rooms, protocol, transports, diagnostics (net10.0). |
| `src/Client-Server-App/` | WPF application shell referencing the core (net10.0-windows). |
| `tests/` | xunit.v3 (MTP) suite against the core. |
```

- [x] **Step 6: Validate**

```bash
dotnet build Client-Server-App.slnx -c Release
dotnet test
```

Warning-free build and 84/84 required. If ghost `CS` errors from stale WPF temp projects appear, delete `obj/`+`bin/` under moved projects and rebuild.

- [x] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: extract portable core library"
```

---

### Task 2: Align namespaces with assembly names

**Files:**
- Modify (namespace/using lines): all `.cs` under `src/Client-Server-App.Core`, `src/Client-Server-App`, `tests/Client-Server-App.Tests`
- Modify csproj `<RootNamespace>`: Core → `ClientServer.Core`; app → `ClientServer.App`; tests → `ClientServer.Tests`
- Modify XAML: `App.xaml` and the five window `.xaml` files (`x:Class`)
- Move (`git mv`): `src/Client-Server-App.Core/Game/Transports.cs` → `src/Client-Server-App.Core/Transports/Transports.cs`

**Interfaces:**
- Produces: the spec's final namespace map — `ClientServer.Core.Game`, `ClientServer.Core.Transports`, `ClientServer.Core.Diagnostics`, `ClientServer.App`, `ClientServer.App.Diagnostics`, `ClientServer.Tests.*`.

- [x] **Step 1: Apply the mapping**

Namespace declarations (apply per file group):

| Where | Find | Replace |
| --- | --- | --- |
| Core `Game/*.cs` (except Transports.cs) | `namespace Client_Server_App.Game;` | `namespace ClientServer.Core.Game;` |
| Core `Transports/ServerTcp.cs`, `ClientTcp.cs` | `namespace Client_Server_App;` | `namespace ClientServer.Core.Transports;` |
| Core `Game/Transports.cs` (after its move) | `namespace Client_Server_App.Game;` | `namespace ClientServer.Core.Transports;` |
| Core `Diagnostics/*` | `namespace Client_Server_App.Diagnostics;` | `namespace ClientServer.Core.Diagnostics;` |
| App windows + `App.xaml.cs` | `namespace Client_Server_App` / `.Game` usages remain via using-fix below | `namespace ClientServer.App;` |
| App `Diagnostics/MessageBoxCrashReporter.cs` | `namespace Client_Server_App.Diagnostics;` | `namespace ClientServer.App.Diagnostics;` |
| Tests (all files) | `namespace Client_Server_App.Tests…` | `namespace ClientServer.Tests…` |

Using directives:

| Find | Replace |
| --- | --- |
| `using Client_Server_App.Game;` | `using ClientServer.Core.Game;` |
| `using Client_Server_App.Tests.TestDoubles;` | `using ClientServer.Tests.TestDoubles;` |
| `using Client_Server_App.Diagnostics;` in Core files and tests | `using ClientServer.Core.Diagnostics;` |
| `using Client_Server_App.Diagnostics;` in `src/Client-Server-App/App.xaml.cs` | `using ClientServer.Core.Diagnostics;` **and** `using ClientServer.App.Diagnostics;` (reporter type) |
| bare `using Client_Server_App;` | replaced by what the file actually consumes: engine files get `using ClientServer.Core.Game; using ClientServer.Core.Transports;`; window code-behind gets `using ClientServer.Core.Game;` (plus its own namespace is implicit) |

Set `<RootNamespace>` per csproj as listed above. Move the interfaces file:

```bash
git mv src/Client-Server-App.Core/Game/Transports.cs src/Client-Server-App.Core/Transports/Transports.cs
```

Concrete known hot-spots to verify after sweep:
- `LobbyService.cs` uses `NullLogger<LobbyService>` (`Microsoft.Extensions.Logging.Abstractions`) — unchanged.
- Tests' `FakeServerTransport : IServerTransport` now needs `using ClientServer.Core.Transports;`.
- `ConnectionWindow.xaml.cs` needs `using ClientServer.Core.Game; using ClientServer.Core.Transports; using ClientServer.Core.Diagnostics; using Microsoft.Extensions.Logging; using ClientServer.App.Diagnostics;`.

- [x] **Step 2: Update XAML class references**

In each XAML file, change only the `x:Class` value (and drop nothing else):

```xml
<!-- App.xaml -->
x:Class="ClientServer.App.App"
StartupUri="MainWindow.xaml"
```

```xml
<!-- MainWindow.xaml / ConnectionWindow.xaml / LobbyWindow.xaml / GameWindow.xaml / ServerWindow.xaml -->
x:Class="ClientServer.App.MainWindow"        <!-- etc., matching each code-behind -->
```

`xmlns:local` declarations already point at the code-behind's own namespace and stay syntactically valid after the rename.

- [x] **Step 3: Validate**

```bash
dotnet build Client-Server-App.slnx -c Release
dotnet test
```

Warning-free + 84/84. Markup compile failures here mean a missed `x:Class`.

- [x] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: align namespaces with assembly names"
```

---

### Task 3: Win-x64 single-file publish profile

**Files:**
- Create: `src/Client-Server-App/Properties/PublishProfiles/win-x64.pubxml`
- Modify: `README.md` (one artifact line)

**Interfaces:**
- Produces: publish command usable by future release workflow (R3): `dotnet publish src/Client-Server-App -c Release -p:PublishProfile=win-x64` → one self-contained exe.

- [x] **Step 1: Create the profile**

`src/Client-Server-App/Properties/PublishProfiles/win-x64.pubxml`:

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishDir>bin\Release\net10.0-windows\publish\win-x64\</PublishDir>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
</Project>
```

- [x] **Step 2: README artifact note**

Append under the existing Docker section:

```markdown
## Distributable build

A single-file Windows executable is produced with:

​```shell
dotnet publish src/Client-Server-App -c Release -p:PublishProfile=win-x64
​```

Output lands in `src/Client-Server-App/bin/Release/net10.0-windows/publish/win-x64/`.
```

(Remove the zero-width markers around the fence when pasting.)

- [x] **Step 3: Validate**

```bash
dotnet build Client-Server-App.slnx -c Release && dotnet test && dotnet publish src/Client-Server-App -c Release -p:PublishProfile=win-x64
Get-ChildItem src/Client-Server-App/bin/Release/net10.0-windows/publish/win-x64/Client-Server-App.exe   # exists
Test-Path src/Client-Server-App/bin/Release/net10.0-windows/publish/win-x64/Client-Server-App.Core.dll  # must be False (bundled)
```

Launch smoke on an interactive desktop session is a manual bonus check, not a gate.

- [x] **Step 4: Commit**

```bash
git add src/Client-Server-App/Properties README.md
git commit -m "chore: add win-x64 single-file publish profile"
```

(Stage exactly the pubxml and README.)

---

## Self-Review Notes

- Spec coverage: target layout & dependency rule → Task 1 Steps 1–5; namespace map incl. `Transports.cs` relocation → Task 2 Step 1–2; migration sequence of three green commits → Tasks 1–3; validation gates (build+84, net10.0 portability proof, single-file/no-loose-Core.dll) → Task 1 Step 6, Task 2 Step 3, Task 3 Step 3; README/Dockerfile/slnx updates → Task 1 Step 5; non-goals respected (no tools/, no UiTests scaffolding).
- Placeholders: none — every step shows exact content or exact commands.
- Type consistency: namespaces used in Task 3 paths and README match Task 2 output; assembly names stable across tasks.
