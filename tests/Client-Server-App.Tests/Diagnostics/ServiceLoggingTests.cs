using ClientServer.Core.Game;
using ClientServer.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClientServer.Tests.Diagnostics;

public sealed class ServiceLoggingTests
{
    [Fact]
    public void Lobby_hello_logs_information_alongside_text_event()
    {
        FakeServerTransport transport = new();
        CapturingLogger<LobbyService> logger = new();
        List<string> eventLines = [];
        LobbyService lobby = new(transport, logger: logger);
        lobby.LogReceived += message => eventLines.Add(message);
        lobby.Start();

        Guid id = transport.SimulateClientConnected();
        transport.ReceiveLine(id, GameJson.Serialize(new HelloRecord("Alice")));

        Assert.Contains(logger.Inner.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("Alice"));
        string line = Assert.Single(eventLines);
        Assert.Contains("Alice", line);
    }
}
