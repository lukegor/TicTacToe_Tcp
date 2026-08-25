using ClientServer.Core.Game;
using ClientServer.Core.Transports;

namespace ClientServer.TestSupport;

internal static class UiTestSession
{
    public static async Task<(PlayerSession Session, FakeClientTransport Transport)> ConnectSeatedAsync(
        string room = "duel", string? mark = "X", string? name = null, string status = "inProgress")
    {
        FakeClientTransport transport = new();
        PlayerSession session = new(
            () => Task.FromResult<IClientTransport>(transport),
            displayName: name);
        await session.ConnectAsync();

        GameStateRecord state = new(
            ["", "", "", "", "", "", "", "", ""],
            mark ?? "X",
            status,
            Winner: null,
            WinningLine: null,
            1,
            Room: room);
        transport.ReceiveLine(GameJson.Serialize(new JoinedRecord(room, mark, false, state)));
        return (session, transport);
    }
}
