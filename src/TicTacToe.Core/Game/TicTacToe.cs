namespace TicTacToe.Core.Game;

internal enum Player
{
    X,
    O,
}

internal enum GameStatus
{
    InProgress,
    Won,
    Draw,
}

/// <summary>
/// Pure tic-tac-toe rules engine: no I/O, no events, no threading.
/// Cells are indexed 0-8, row-major.
/// </summary>
internal sealed class TicTacToe
{
    private static readonly int[][] WinningLines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6],
    ];

    private Player?[] _board = new Player?[9];

    public IReadOnlyList<Player?> Board => _board;
    public Player Turn { get; private set; } = Player.X;
    public GameStatus Status { get; private set; } = GameStatus.InProgress;
    public Player? Winner { get; private set; }
    public IReadOnlyList<int>? WinningLine { get; private set; }

    /// <summary>Applies <paramref name="player"/>'s move if it is legal; otherwise returns false.</summary>
    public bool TryApplyMove(int cell, Player player)
    {
        if (Status != GameStatus.InProgress || player != Turn)
        {
            return false;
        }

        if ((uint)cell >= (uint)_board.Length || _board[cell] is not null)
        {
            return false;
        }

        _board[cell] = player;
        FinishOrAdvance();
        return true;
    }

    public void Reset(Player startingPlayer)
    {
        _board = new Player?[9];
        Turn = startingPlayer;
        Status = GameStatus.InProgress;
        Winner = null;
        WinningLine = null;
    }

    /// <summary>Awards the game to <paramref name="winner"/> (disconnect grace expiry).</summary>
    public void DeclareForfeit(Player winner)
    {
        if (Status != GameStatus.InProgress)
        {
            return;
        }

        Status = GameStatus.Won;
        Winner = winner;
        WinningLine = null;
    }

    private void FinishOrAdvance()
    {
        foreach (int[] line in WinningLines)
        {
            if (_board[line[0]] is { } mark && _board[line[1]] == mark && _board[line[2]] == mark)
            {
                Status = GameStatus.Won;
                Winner = mark;
                WinningLine = line;
                return;
            }
        }

        if (_board.All(cell => cell is not null))
        {
            Status = GameStatus.Draw;
            return;
        }

        Turn = Turn == Player.X ? Player.O : Player.X;
    }
}
