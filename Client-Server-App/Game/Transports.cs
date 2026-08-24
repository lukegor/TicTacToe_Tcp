namespace Client_Server_App.Game;

/// <summary>The host-side view of the server transport.</summary>
internal interface IServerTransport
{
    event Action<string>? MessageReceived;
    event Action? ClientConnected;
    event Action? ClientDisconnected;

    Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>The client-side view of the client transport.</summary>
internal interface IClientTransport
{
    event Action<string>? MessageReceived;
    event Action? Disconnected;

    Task SendLineAsync(string message, CancellationToken cancellationToken = default);
}
