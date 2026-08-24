using Client_Server_App;
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class TicTacToeEndToEndTests
{
    [Fact]
    public async Task FullSession_ClientWins_RematchSwapsMarks()
    {
        using ServerTcp server = new(0);
        server.Start();
        using ClientTcp clientTransport = new();
        await clientTransport.ConnectAsync("127.0.0.1", server.Port);

        TicTacToeHostService host = new(server);
        host.Start();
        TicTacToeClientService client = new(clientTransport);
        client.Start();

        // Client receives the join push for round 1 (host = X, client = O).
        await WaitForAsync(() => client.CurrentState is not null);

        // Round 1: the CLIENT wins to exercise client-request validation:
        // X:0, O:3, X:1, O:4, X:6, O:5 -> O wins on [3,4,5].
        await host.PlayMoveAsync(0);
        await WaitForAsync(() => client.CurrentState!.Turn == GameRoles.ClientMark(1));
        await client.PlayCellAsync(3);
        await WaitForAsync(() => host.CurrentState!.Turn == "X");

        await host.PlayMoveAsync(1);
        await WaitForAsync(() => client.CurrentState!.Turn == GameRoles.ClientMark(1));
        await client.PlayCellAsync(4);
        await WaitForAsync(() => host.CurrentState!.Turn == "X");

        await host.PlayMoveAsync(6);
        await WaitForAsync(() => client.CurrentState!.Turn == GameRoles.ClientMark(1));
        await client.PlayCellAsync(5);

        await WaitForAsync(() => host.CurrentState!.Status == "won");
        await WaitForAsync(() => client.CurrentState!.Status == "won");
        Assert.Equal("O", host.CurrentState!.Winner);
        Assert.Equal("O", client.CurrentState!.Winner);

        // Rematch: client offers first. The host must NOT advance rounds alone.
        await client.SendRematchOfferAsync();
        await Task.Delay(100); // let any (incorrect) premature round advance surface
        Assert.Equal(1, host.CurrentState!.Round);

        // Host's click is the second vote -> round 2 starts, marks swapped.
        await host.RequestRematchAsync();

        await WaitForAsync(() => host.CurrentState!.Round == 2);
        await WaitForAsync(() => client.CurrentState!.Round == 2);
        Assert.All(host.CurrentState!.Board, cell => Assert.Equal("", cell));
        Assert.Equal("X", host.CurrentState!.Turn);   // X always starts; the client holds X in round 2
        Assert.Equal("O", GameRoles.HostMark(2));
        Assert.Equal("X", GameRoles.ClientMark(2));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within 10 seconds.");
            }

            await Task.Delay(25);
        }
    }
}
