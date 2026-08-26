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
