using Client_Server_App.Game;
using Xunit;

namespace Client_Server_App.Tests.Game;

public sealed class TicTacToeTests
{
    private readonly TicTacToe _game = new();

    [Fact]
    public void NewGame_StartsWithEmptyBoardAndXTurn()
    {
        Assert.Equal(9, _game.Board.Count);
        Assert.All(_game.Board, cell => Assert.Null(cell));
        Assert.Equal(Player.X, _game.Turn);
        Assert.Equal(GameStatus.InProgress, _game.Status);
        Assert.Null(_game.Winner);
        Assert.Null(_game.WinningLine);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(3, 4, 5)]
    [InlineData(6, 7, 8)]
    [InlineData(0, 3, 6)]
    [InlineData(1, 4, 7)]
    [InlineData(2, 5, 8)]
    [InlineData(0, 4, 8)]
    [InlineData(2, 4, 6)]
    public void ThreeInARow_WinsWithWinningLine(int a, int b, int c)
    {
        // Two filler cells off the winning line keep the alternation legal
        // without letting either side win early.
        int[] fillers = Enumerable.Range(0, 9).Except([a, b, c]).ToArray();

        PlaySequence(
            (a, Player.X),
            (fillers[0], Player.O),
            (b, Player.X),
            (fillers[1], Player.O),
            (c, Player.X));

        Assert.Equal(GameStatus.Won, _game.Status);
        Assert.Equal(Player.X, _game.Winner);
        Assert.Equal(new[] { a, b, c }, _game.WinningLine);
    }

    [Fact]
    public void FullBoardWithoutWinner_Draws()
    {
        // Final board:   X O X
        //                X O O
        //                O X X     (verified: no completed line for either side,
        //                           strict X/O/X/O... alternation throughout)
        (int cell, Player player)[] moves =
        [
            (0, Player.X), (1, Player.O), (2, Player.X),
            (4, Player.O), (3, Player.X), (5, Player.O),
            (7, Player.X), (6, Player.O), (8, Player.X),
        ];

        foreach ((int cell, Player player) in moves)
        {
            Assert.True(_game.TryApplyMove(cell, player));
        }

        Assert.Equal(GameStatus.Draw, _game.Status);
        Assert.Null(_game.Winner);
    }

    [Fact]
    public void TryApplyMove_RejectsWrongTurn()
    {
        Assert.False(_game.TryApplyMove(0, Player.O));
        Assert.Equal(GameStatus.InProgress, _game.Status);
        Assert.Null(_game.Board[0]);
    }

    [Fact]
    public void TryApplyMove_RejectsOccupiedCell()
    {
        Assert.True(_game.TryApplyMove(4, Player.X));
        Assert.False(_game.TryApplyMove(4, Player.O));
        Assert.Equal(Player.X, _game.Board[4]);
    }

    [Fact]
    public void TryApplyMove_RejectsOutOfRangeCell()
    {
        Assert.False(_game.TryApplyMove(-1, Player.X));
        Assert.False(_game.TryApplyMove(9, Player.X));
    }

    [Fact]
    public void TryApplyMove_RejectsMovesAfterGameOver()
    {
        PlaySequence((0, Player.X), (3, Player.O), (1, Player.X), (4, Player.O), (2, Player.X));

        Assert.Equal(GameStatus.Won, _game.Status);
        Assert.False(_game.TryApplyMove(8, Player.X));
    }

    [Fact]
    public void Reset_ClearsBoardAndSetsStarter()
    {
        PlaySequence((0, Player.X), (3, Player.O), (1, Player.X));

        _game.Reset(Player.X);

        Assert.All(_game.Board, cell => Assert.Null(cell));
        Assert.Equal(Player.X, _game.Turn);
        Assert.Equal(GameStatus.InProgress, _game.Status);
        Assert.Null(_game.Winner);
        Assert.Null(_game.WinningLine);
    }

    private void PlaySequence(params (int cell, Player player)[] moves)
    {
        foreach ((int cell, Player player) in moves)
        {
            Assert.True(_game.TryApplyMove(cell, player));
        }
    }
}
