using Client_Server_App.Game;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class FakeServerTransport : IServerTransport
{
    public event Action<string>? MessageReceived;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public List<string> BroadcastLines { get; } = [];

    public Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        BroadcastLines.Add(message);
        return Task.CompletedTask;
    }

    public void ReceiveLine(string line) => MessageReceived?.Invoke(line);

    public void SimulateClientConnected() => ClientConnected?.Invoke();

    public void SimulateClientDisconnected() => ClientDisconnected?.Invoke();
}
