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

/// <summary>The client-side view of the client transport (unchanged).</summary>
internal interface IClientTransport
{
    event Action<string>? MessageReceived;
    event Action? Disconnected;

    Task SendLineAsync(string message, CancellationToken cancellationToken = default);
}
