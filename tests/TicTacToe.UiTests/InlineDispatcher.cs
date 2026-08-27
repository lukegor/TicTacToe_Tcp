using TicTacToe.ViewModels;

namespace TicTacToe.UiTests;

public sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
