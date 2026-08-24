using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class GameJsonTests
{
    [Fact]
    public void Serialize_MoveRequest_UsesCamelCaseWithTypeDiscriminator()
    {
        string json = GameJson.Serialize(new MoveRequestRecord(4));

        Assert.Contains("\"type\":\"moveRequest\"", json);
        Assert.Contains("\"cell\":4", json);
    }

    [Fact]
    public void Serialize_State_IncludesAllFields()
    {
        string json = GameJson.Serialize(new GameStateRecord(
            Board: ["X", "", "O", "", "", "", "", "", ""],
            Turn: "X",
            Status: "inProgress",
            Winner: null,
            WinningLine: null,
            Round: 2));

        Assert.Contains("\"type\":\"state\"", json);
        Assert.Contains("\"board\":", json);
        Assert.Contains("\"turn\":\"X\"", json);
        Assert.Contains("\"status\":\"inProgress\"", json);
        Assert.Contains("\"winner\":null", json);
        Assert.Contains("\"winningLine\":null", json);
        Assert.Contains("\"round\":2", json);
    }

    [Fact]
    public void TryParse_RoundTripsEveryEnvelopeKind()
    {
        GameEnvelope[] originals =
        [
            new MoveRequestRecord(7),
            new RematchOfferRecord(),
            new GameStateRecord(["", "", "", "", "", "", "", "", ""], "X", "inProgress", null, null, 1),
            new GameStateRecord(["X", "X", "X", "", "O", "O", "", "", ""], "O", "won", "X", [0, 1, 2], 3),
        ];

        foreach (GameEnvelope original in originals)
        {
            GameEnvelope parsed = GameJson.TryParse(GameJson.Serialize(original))!;

            Assert.Equal(original.GetType(), parsed.GetType());
            switch (original, parsed)
            {
                case (MoveRequestRecord o, MoveRequestRecord p):
                    Assert.Equal(o.Cell, p.Cell);
                    break;

                case (RematchOfferRecord, RematchOfferRecord):
                    break;

                case (GameStateRecord o, GameStateRecord p):
                    Assert.Equal(o.Board, p.Board);
                    Assert.Equal(o.Turn, p.Turn);
                    Assert.Equal(o.Status, p.Status);
                    Assert.Equal(o.Winner, p.Winner);
                    Assert.Equal(o.WinningLine, p.WinningLine);
                    Assert.Equal(o.Round, p.Round);
                    break;
            }
        }
    }

    [Fact]
    public void TryParse_ReturnsNullForChatText()
    {
        Assert.Null(GameJson.TryParse("Hello from client"));
        Assert.Null(GameJson.TryParse("{\"type\":\"unknown\"}"));
        Assert.Null(GameJson.TryParse("{not json"));
    }
}
