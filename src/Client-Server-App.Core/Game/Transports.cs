namespace Client_Server_App.Game;

/// <summary>The referee-side view of the server transport: identity-aware pipes.</summary>
internal interface IServerTransport
{
    event Action<Guid>? ClientConnected;
    event Action<Guid>? ClientDisconnected;
    event Action<Guid, string>? MessageReceived;

    Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default);
    Task SendToAsync(Guid id, string message, CancellationToken cancellationToken = default);
}

/// <summary>The client-side view of the client transport.</summary>
internal interface IClientTransport
{
    event Action<string>? MessageReceived;
    event Action? Disconnected;

    Task SendLineAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins reading incoming lines. Call only after subscribing to the events:
    /// nothing is read (and nothing raised) before it, so early server pushes
    /// are buffered by the socket instead of being lost.
    /// </summary>
    void Start();
}

/// <summary>Lifecycle phases of a client-side <see cref="PlayerSession"/>.</summary>
public enum PlayerSessionState
{
    Connecting,
    Lobby,
    Seated,
    Reconnecting,
    Disconnected,
}
