using System.Text.Json;

namespace TicTacToe.Core.Game;

internal static class GameJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(GameEnvelope message) =>
        JsonSerializer.Serialize(message, Options);

    /// <summary>Parses an envelope; returns null when the line is not a known game message (chat fallback).</summary>
    public static GameEnvelope? TryParse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<GameEnvelope>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
