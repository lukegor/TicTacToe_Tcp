# Builds and publishes the WPF application inside Windows containers.
#
# Requirements: Docker with Windows container mode enabled
# (Docker Desktop: "Switch to Windows containers...").
#
# A published WPF app is a desktop GUI; the final image packages a
# self-contained win-x64 build that must be launched from an interactive
# Windows session (e.g. via `docker run -it` on a session-enabled host or by
# copying the artifact out of the image). This file guarantees reproducible,
# hermetic builds of the application.

FROM mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2022 AS build

WORKDIR /src

# Copy project + central build config first for better layer caching on restore.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/TicTacToe.Core/TicTacToe.Core.csproj", "src/TicTacToe.Core/"]
COPY ["src/TicTacToe/TicTacToe.csproj", "src/TicTacToe/"]
RUN dotnet restore "src/TicTacToe/TicTacToe.csproj"

COPY . .
RUN dotnet publish "src/TicTacToe/TicTacToe.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:TreatWarningsAsErrors=true `
    -o /app

FROM mcr.microsoft.com/windows/servercore:ltsc2022 AS final

WORKDIR /app
COPY --from=build ["/app", "."]

ENTRYPOINT ["TicTacToe.exe"]
