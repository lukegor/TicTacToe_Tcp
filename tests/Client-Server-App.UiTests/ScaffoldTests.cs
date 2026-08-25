using System.Threading;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.UiTests;

public sealed class ScaffoldTests
{
    [WpfFact]
    public async Task WpfFact_runs_on_sta_thread_with_dispatcher()
    {
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        await TestDispatcher.FlushAsync();
    }
}
