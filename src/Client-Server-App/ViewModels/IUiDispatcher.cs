namespace ClientServer.App.ViewModels;

/// <summary>Marshal onto the UI thread.</summary>
internal interface IUiDispatcher
{
    void Post(Action action);
}
