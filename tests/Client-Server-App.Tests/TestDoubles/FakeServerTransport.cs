using Client_Server_App.Game;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class FakeServerTransport : IServerTransport
{
    private readonly Dictionary<Guid, List<string>> _inboxes = [];

    public event Action<Guid>? ClientConnected;
    public event Action<Guid>? ClientDisconnected;
    public event Action<Guid, string>? MessageReceived;

    public List<(Guid? Target, string Line)> Sent { get; } = [];

    public Task BroadcastLineAsync(string message, CancellationToken cancellationToken = default)
    {
        Sent.Add((null, message));
        foreach (Guid id in _inboxes.Keys)
        {
            Deliver(id, message);
        }

        return Task.CompletedTask;
    }

    public Task SendToAsync(Guid id, string message, CancellationToken cancellationToken = default)
    {
        Sent.Add((id, message));
        Deliver(id, message);
        return Task.CompletedTask;
    }

    public Guid SimulateClientConnected()
    {
        Guid id = Guid.NewGuid();
        _inboxes[id] = [];
        ClientConnected?.Invoke(id);
        return id;
    }

    public void SimulateClientDisconnected(Guid id)
    {
        _inboxes.Remove(id);
        ClientDisconnected?.Invoke(id);
    }

    public void ReceiveLine(Guid id, string line) => MessageReceived?.Invoke(id, line);

    public IReadOnlyList<string> Inbox(Guid id) =>
        _inboxes.GetValueOrDefault(id, []);

    public void ClearInbox(Guid id) => _inboxes[id].Clear();

    private void Deliver(Guid id, string message)
    {
        if (!_inboxes.TryGetValue(id, out List<string>? list))
        {
            list = [];
            _inboxes[id] = list;
        }

        list.Add(message);
    }
}
