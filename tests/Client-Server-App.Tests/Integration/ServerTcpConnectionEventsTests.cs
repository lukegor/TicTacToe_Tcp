using Client_Server_App;
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class ServerTcpConnectionEventsTests
{
    [Fact]
    public async Task ClientLifecycle_RaisesConnectedThenDisconnected()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += () => connected.TrySetResult();
        server.ClientDisconnected += () => disconnected.TrySetResult();

        server.Start();
        Assert.True(server.Port > 0);

        using ClientTcp client = new();
        await client.ConnectAsync("127.0.0.1", server.Port);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        client.Dispose();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Broadcast_ReachesConnectedClient()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += () => connected.TrySetResult();
        server.Start();

        using ClientTcp client = new();
        TaskCompletionSource<string> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += line => received.TrySetResult(line);

        await client.ConnectAsync("127.0.0.1", server.Port);
        // ClientConnected fires after the writer is registered, so a subsequent
        // broadcast is guaranteed to reach the client.
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await server.BroadcastLineAsync("ping");
        Assert.Equal("ping", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
