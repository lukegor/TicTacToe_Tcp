using System.IO;
using System.Net;
using System.Net.Sockets;
using TicTacToe.ViewModels;
using TicTacToe.Core.Game;
using TicTacToe.Core.Transports;
using Microsoft.Extensions.Logging;

namespace TicTacToe;

internal sealed class RealConnectionInfrastructure(
    ILoggerFactory logs,
    Action<string> log) : IConnectionInfrastructure
{
    public async Task<PlayerSession> ConnectAsync(string host, int port, string playerName,
        CancellationToken ct)
    {
        ClientTcp client = new(logs.CreateLogger<ClientTcp>());
        try
        {
            await client.ConnectAsync(host, port, ct).ConfigureAwait(true);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        log($"Connected to {host}:{port}.");

        ClientTcp? initialTransport = client;
        async Task<IClientTransport> ReconnectFactory()
        {
            ClientTcp? reused = Interlocked.Exchange(ref initialTransport, null);
            if (reused is not null)
            {
                return reused;
            }

            ClientTcp fresh = new(logs.CreateLogger<ClientTcp>());
            await fresh.ConnectAsync(host, port, CancellationToken.None).ConfigureAwait(true);
            return fresh;
        }

        return new PlayerSession(ReconnectFactory, displayName: playerName,
            logger: logs.CreateLogger<PlayerSession>());
    }

    public RefereeHandle StartHost(int port)
    {
        ServerTcp server = new(port, logs.CreateLogger<ServerTcp>());
        try
        {
            server.Start();
        }
        catch
        {
            server.Dispose();
            throw;
        }

        LobbyService lobby = new(server, logger: logs.CreateLogger<LobbyService>());
        lobby.Start();
        return new RefereeHandle(port, server, lobby);
    }
}
