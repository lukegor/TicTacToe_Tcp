using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class ConnectionViewModelTests
{
    private sealed class StubInfrastructure : IConnectionInfrastructure
    {
        public int ConnectCalls { get; private set; }
        public int StartHostCalls { get; private set; }

        public Task<PlayerSession> ConnectAsync(string host, int port, string playerName,
            CancellationToken ct)
        {
            ConnectCalls++;
            throw new InvalidOperationException("factory reached");
        }

        RefereeHandle IConnectionInfrastructure.StartHost(int port)
        {
            StartHostCalls++;
            throw new InvalidOperationException("host reached");
        }
    }

    [Fact]
    public async Task EmptyAddress_AppendsHint_InfrastructureUntouched()
    {
        StubInfrastructure stub = new();
        var vm = new ConnectionViewModel(stub, new InlineDispatcher());
        vm.Address = ""; // otherwise the constructor defaults satisfy validation

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("Enter an IP address.", vm.LogText);
        Assert.Equal(0, stub.ConnectCalls);
        Assert.Equal(0, stub.StartHostCalls);
    }

    public static TheoryData<string> InvalidPorts => new() { "abc", "0", "70000" };

    [Theory]
    [MemberData(nameof(InvalidPorts))]
    public async Task InvalidPort_AppendsPortError_InfrastructureUntouched(string port)
    {
        StubInfrastructure stub = new();
        var vm = new ConnectionViewModel(stub, new InlineDispatcher());
        vm.Address = "127.0.0.1";
        vm.Port = port;

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains($"'{port}' is not a valid port", vm.LogText);
        Assert.Equal(0, stub.ConnectCalls);
    }
}
