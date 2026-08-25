using ClientServer.Core.Transports;
using Xunit;

namespace ClientServer.Tests.Integration;

public sealed class ServerTcpConnectionEventsTests
{
    [Fact]
    public async Task ClientLifecycle_RaisesIdentityEvents()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource<Guid> connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Guid> disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? registeredId = null;
        server.ClientConnected += id =>
        {
            registeredId = id;
            connected.TrySetResult(id);
        };
        server.ClientDisconnected += id => disconnected.TrySetResult(id);

        server.Start();
        Assert.True(server.Port > 0);

        using ClientTcp client = new();
        await client.ConnectAsync("127.0.0.1", server.Port, TestContext.Current.CancellationToken);
        Guid serverSideId = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotEqual(Guid.Empty, serverSideId);
        Assert.Equal(serverSideId, registeredId);

        client.Dispose();
        Assert.Equal(serverSideId, await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendTo_ReachesExactlyOneClient()
    {
        using ServerTcp server = new(0);
        TaskCompletionSource<Guid> firstConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Guid> secondConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0;
        server.ClientConnected += id =>
        {
            arrivals++;
            _ = arrivals == 1
                ? firstConnected.TrySetResult(id)
                : secondConnected.TrySetResult(id);
        };

        server.Start();

        using ClientTcp first = new();
        TaskCompletionSource<string> firstReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        first.MessageReceived += line => firstReceived.TrySetResult(line);
        await first.ConnectAsync("127.0.0.1", server.Port, TestContext.Current.CancellationToken);
        first.Start();
        Guid firstId = await firstConnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using ClientTcp second = new();
        int secondSeen = 0;
        second.MessageReceived += _ => secondSeen++;
        await second.ConnectAsync("127.0.0.1", server.Port, TestContext.Current.CancellationToken);
        second.Start();
        _ = await secondConnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await server.SendToAsync(firstId, "just-you", TestContext.Current.CancellationToken);

        Assert.Equal("just-you", await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(0, secondSeen);
    }
}
