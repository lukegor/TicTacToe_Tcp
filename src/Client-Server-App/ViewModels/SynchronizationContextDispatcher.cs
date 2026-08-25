namespace ClientServer.App.ViewModels;

internal sealed class SynchronizationContextDispatcher(SynchronizationContext context) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        context.Post(static state => ((Action)state!).Invoke(), action);
    }
}
