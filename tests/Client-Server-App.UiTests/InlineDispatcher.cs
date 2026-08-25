using ClientServer.App.ViewModels;

namespace ClientServer.UiTests;

public sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
