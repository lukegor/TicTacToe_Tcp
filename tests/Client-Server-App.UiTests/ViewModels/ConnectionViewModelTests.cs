using ClientServer.App.ViewModels;
using ClientServer.UiTests;
using Xunit;

namespace ClientServer.UiTests.ViewModels;

public sealed class ConnectionViewModelTests
{
    [Fact]
    public async Task EmptyAddress_AppendsHint_FactoryNotReached()
    {
        int calls = 0;
        ConnectionViewModel vm = new(
            (_, _, _) => { calls++; throw new InvalidOperationException("factory reached"); },
            _ => null!, _ => null!,
            new InlineDispatcher());
        vm.Address = ""; // otherwise the constructor defaults satisfy validation

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("Enter an IP address.", vm.LogText);
        Assert.Equal(0, calls);
    }

    public static TheoryData<string> InvalidPorts => new() { "abc", "0", "70000" };

    [Theory]
    [MemberData(nameof(InvalidPorts))]
    public async Task InvalidPort_AppendsPortError(string port)
    {
        int calls = 0;
        ConnectionViewModel vm = new(
            (_, _, _) => { calls++; throw new InvalidOperationException("factory reached"); },
            _ => null!, _ => null!,
            new InlineDispatcher());
        vm.Address = "127.0.0.1";
        vm.Port = port;

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains($"'{port}' is not a valid port", vm.LogText);
        Assert.Equal(0, calls);
    }
}
