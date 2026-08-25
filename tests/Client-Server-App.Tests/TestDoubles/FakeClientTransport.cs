using Client_Server_App.Game;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class FakeClientTransport : IClientTransport
{
    public event Action<string>? MessageReceived;
    public event Action? Disconnected;

    public List<string> SentLines { get; } = [];

    public Task SendLineAsync(string message, CancellationToken cancellationToken = default)
    {
        SentLines.Add(message);
        return Task.CompletedTask;
    }

    public void Start()
    {
    }

    public void ReceiveLine(string line) => MessageReceived?.Invoke(line);

    public void SimulateDisconnect() => Disconnected?.Invoke();
}
