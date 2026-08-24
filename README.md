# Client-Server-App

A minimal WPF application that demonstrates newline-delimited TCP messaging between a
client and a server. The connection dialog can act as either side:

- **Connect** – connect to a host/port and play tic-tac-toe (or read chat text).
- **Host** – listen on a port; acts as the authoritative tic-tac-toe referee and
  broadcasts every board update to all connected clients.

## Playing

Start two instances. In the first click **Create Host**; in the second fill in
the host's IP/port and click **Connect**. The host plays X and moves first;
marks swap after each rematch.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build and run

```shell
dotnet build Client-Server-App.slnx -c Release
dotnet run --project Client-Server-App
```

Warnings are treated as errors (`TreatWarningsAsErrors` in `Directory.Build.props`); the build
is expected to be warning-free.

To launch both sides on one machine, start two instances: click **Create Host** in one and
**Connect** in the other.

## Docker

`Dockerfile` produces reproducible, self-contained builds inside Windows containers:

```shell
docker build -t client-server-app .
```

Requires Docker in Windows container mode. The image packages a self-contained win-x64
GUI build; running it needs an interactive Windows session, so its primary purpose is
hermetic CI builds rather than headless execution.

## Repository layout

| Path | Purpose |
| --- | --- |
| `Directory.Build.props` | Shared compile settings (target framework, nullable, analyzer policy). |
| `Directory.Packages.props` | Central Package Management for all NuGet versions. |
| `Client-Server-App.slnx` | XML-based solution file. |
| `Client-Server-App/` | The WPF application project (no external NuGet dependencies). |
