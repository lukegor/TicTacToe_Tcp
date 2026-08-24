namespace Client_Server_App.Game;

/// <summary>Marks swap between rounds; X always starts a round.</summary>
internal static class GameRoles
{
    public static string HostMark(int round) => round % 2 == 1 ? "X" : "O";

    public static string ClientMark(int round) => round % 2 == 1 ? "O" : "X";
}
