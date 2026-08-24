using System.IO;
using Client_Server_App;
using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Integration;

public sealed class TicTacToeRoomsEndToEndTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);
    private static readonly int[] ExpectedWinningLine = [3, 4, 5];

    private sealed class TestServer : IDisposable
    {
        public ServerTcp Transport { get; } = new(0);
        public LobbyService Lobby { get; }

        public TestServer(TimeSpan? grace = null)
        {
            Transport.Start();
            Lobby = new LobbyService(Transport, grace ?? Grace);
            Lobby.Start();
        }

        public int Port => Transport.Port;

        public void Dispose()
        {
            Lobby.Dispose();
            Transport.Dispose();
        }
    }

    private static PlayerSession Connect(TestServer server)
    {
        PlayerSession session = new(async () =>
        {
            ClientTcp transport = new();
            await transport.ConnectAsync("127.0.0.1", server.Port);
            return transport;
        });
        session.ConnectAsync().GetAwaiter().GetResult();
        return session;
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within 10s: {what}");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Moves with <paramref name="mover"/> and waits until both sessions observe the cell.</summary>
    private static async Task MoveAndAwait(PlayerSession a, PlayerSession b, PlayerSession mover, int cell)
    {
        await mover.PlayCellAsync(cell);
        await WaitForAsync(() => a.CurrentState!.Board[cell] != "", $"{a.MyMark} saw {cell}");
        await WaitForAsync(() => b.CurrentState!.Board[cell] != "", $"{b.MyMark} saw {cell}");
    }

    [Fact]
    public async Task TwoPlayersAndSpectator_FullGame_RematchSwapsMarks()
    {
        using TestServer server = new(TimeSpan.FromSeconds(10));
        using PlayerSession host = Connect(server);
        using PlayerSession guest = Connect(server);
        using PlayerSession spectator = Connect(server);

        await host.CreateRoomAsync("duel");
        await WaitForAsync(() => host.CurrentState is not null && host.MyMark == "X", "host seated as X");

        await guest.JoinRoomAsync("duel");
        await WaitForAsync(() => guest.CurrentState is not null && guest.MyMark == "O", "guest seated as O");

        await spectator.JoinRoomAsync("duel");
        await WaitForAsync(() => spectator.CurrentState is not null, "spectator watching");
        Assert.True(spectator.IsSpectator);

        // X:0, O:3, X:1, O:4, X:6, O:5 -> O wins [3,4,5].
        await MoveAndAwait(host, guest, host, 0);
        await MoveAndAwait(host, guest, guest, 3);
        await MoveAndAwait(host, guest, host, 1);
        await MoveAndAwait(host, guest, guest, 4);
        await MoveAndAwait(host, guest, host, 6);
        await MoveAndAwait(host, guest, guest, 5);

        await WaitForAsync(() => host.CurrentState!.Status == "won", "host sees result");
        await WaitForAsync(() => guest.CurrentState!.Status == "won", "guest sees result");
        await WaitForAsync(() => spectator.CurrentState!.Status == "won", "spectator sees result");
        Assert.Equal("O", guest.CurrentState!.Winner);
        Assert.Equal(ExpectedWinningLine, guest.CurrentState!.WinningLine!.ToArray());

        // Rematch: both vote (order-independent); marks swap; X starts round 2.
        await guest.SendRematchOfferAsync();
        await host.SendRematchOfferAsync();

        await WaitForAsync(() => host.CurrentState!.Round == 2, "round 2 on host");
        await WaitForAsync(() => guest.CurrentState!.Round == 2, "round 2 on guest");
        await WaitForAsync(() => spectator.CurrentState!.Round == 2, "round 2 on spectator");
        await WaitForAsync(() => host.MyMark == "O" && guest.MyMark == "X", "marks swapped");
        Assert.Equal("X", host.CurrentState!.Turn);
        Assert.All(host.CurrentState!.Board, cell => Assert.Equal("", cell));
    }

    [Fact]
    public async Task Drop_ReconnectRestoresSeat_AndCancelsGrace()
    {
        using TestServer server = new(TimeSpan.FromSeconds(3));
        List<ClientTcp> transports = [];
        using PlayerSession dropper = new(async () =>
        {
            ClientTcp transport = new();
            await transport.ConnectAsync("127.0.0.1", server.Port);
            transports.Add(transport);
            return transport;
        });
        try
        {
            await dropper.ConnectAsync();
            using PlayerSession opponent = Connect(server);

            await dropper.CreateRoomAsync("resume");
            await WaitForAsync(() => dropper.CurrentState is not null, "dropper seated");

            await opponent.JoinRoomAsync("resume");
            await WaitForAsync(() => opponent.CurrentState is not null, "opponent seated");

            await dropper.PlayCellAsync(4); // X center
            await WaitForAsync(() => opponent.CurrentState!.Board[4] == "X", "opponent sees move");

            transports[^1].Dispose(); // kill the pipe

            await WaitForAsync(
                () =>
                    dropper.State == PlayerSessionState.Seated
                    && dropper.MyMark == "X"
                    && dropper.CurrentState!.Board[4] == "X",
                "seat restored within grace");

            // Grace was cancelled: the game is still alive well past the deadline.
            await Task.Delay(Grace + TimeSpan.FromMilliseconds(500));
            Assert.Equal("inProgress", opponent.CurrentState!.Status);
            Assert.Null(opponent.CurrentState!.WinnerReason);
        }
        finally
        {
            dropper.Dispose();
        }
    }

    [Fact]
    public async Task Drop_NeverReconnects_GraceForfeitsToOpponent()
    {
        using TestServer server = new(Grace);
        bool allowConnections = true;
        ClientTcp? live = null;
        using PlayerSession quitter = new(async () =>
        {
            if (!allowConnections)
            {
                await Task.Yield();
                throw new IOException("no network");
            }

            ClientTcp transport = new();
            await transport.ConnectAsync("127.0.0.1", server.Port);
            live = transport;
            return transport;
        }, TimeSpan.FromMilliseconds(500));
        using PlayerSession survivor = Connect(server);

        await quitter.ConnectAsync(); // consumes the single live transport
        await quitter.CreateRoomAsync("gone");
        await WaitForAsync(() => quitter.CurrentState is not null, "quitter seated");

        await survivor.JoinRoomAsync("gone");
        await WaitForAsync(() => survivor.CurrentState is not null, "survivor seated");

        await quitter.PlayCellAsync(0); // one move, then the pipe dies forever
        await WaitForAsync(() => survivor.CurrentState!.Board[0] == "X", "move visible");

        allowConnections = false;
        live?.Dispose();

        await WaitForAsync(() =>
            survivor.CurrentState!.Status == "won"
            && survivor.CurrentState!.WinnerReason == "forfeit", "forfeit awarded");

        // The quitter's reconnect attempts all fail; it falls back to the lobby.
        await WaitForAsync(() => quitter.State == PlayerSessionState.Lobby, "quitter back in lobby");
    }
}
