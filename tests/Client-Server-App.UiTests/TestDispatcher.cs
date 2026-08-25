using System.Windows;
using System.Windows.Threading;

namespace ClientServer.TestSupport;

/// <summary>Drains pending WPF dispatcher work on the STA test thread.</summary>
public static class TestDispatcher
{
    public static Task FlushAsync()
    {
        Task completion = Dispatcher.CurrentDispatcher.InvokeAsync(
            () => { }, DispatcherPriority.Background).Task;
        return completion;
    }
}
