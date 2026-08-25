using System.Net;
using System.Net.Sockets;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ClientServer.App;
using ClientServer.App.ViewModels;
using ClientServer.Core.Game;
using ClientServer.Core.Transports;
using ClientServer.TestSupport;
using Xunit;

namespace ClientServer.UiTests;

public sealed class ConnectionWindowTests
{
    private sealed class TestServer : IDisposable
    {
        public ServerTcp Transport { get; } = new(0);
        public LobbyService Lobby { get; }

        public TestServer()
        {
            Transport.Start();
            Lobby = new LobbyService(Transport);
            Lobby.Start();
        }

        public int Port => Transport.Port;

        public void Dispose()
        {
            Lobby.Dispose();
            Transport.Dispose();
        }
    }

    private static ConnectionWindow NewWindow(
        Func<LobbyWindow, bool>? lobbyProbe = null,
        Func<ServerWindow, bool>? refereeProbe = null)
    {
        var window = HeadlessWindow.Prepare(new ConnectionWindow(lobbyProbe, refereeProbe, null));
        window.Show(); // off-screen: activates the full binding pipeline
        return window;
    }

    private static void Press(Button button) =>
        ((System.Windows.Automation.Provider.IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();

    private static async Task WaitForAsync(Func<bool> condition, int seconds = 10)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("UI condition not met within 10s");
            }

            await TestDispatcher.FlushAsync();
            await Task.Delay(20);
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class PortHolder : IDisposable
    {
        private readonly TcpListener _listener;

        public PortHolder()
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public void Dispose() => _listener.Stop();
    }

    [WpfFact]
    public async Task Connect_AgainstLiveServer_OpensLobbyOnce_ReenablesButton()
    {
        using TestServer server = new();
        LobbyWindow? opened = null;
        ConnectionWindow window = NewWindow(lobbyProbe: w => { opened = w; return true; });

        UiAssert.Type(window.AddressTextBox, "127.0.0.1");
        UiAssert.Type(window.PortTextBox, server.Port.ToString());
        Press(window.ConnectButton);

        await WaitForAsync(() => opened is not null);

        Assert.NotNull(opened);
        Assert.Contains("Connected to", window.OutputTextBox.Text);
        Assert.True(window.ConnectButton.IsEnabled);
        opened!.Close();   // production cleanup: disposes the session
        window.Close();    // disposes client-side transport
        await TestDispatcher.FlushAsync();
    }

    [WpfFact]
    public async Task Connect_EmptyAddress_ShowsHint_OpensNothing()
    {
        bool openedAny = false;
        ConnectionWindow window = NewWindow(
            lobbyProbe: _ => { openedAny = true; return true; },
            refereeProbe: _ => { openedAny = true; return true; });

        UiAssert.Type(window.AddressTextBox, "");
        UiAssert.Type(window.PortTextBox, "1111");
        Press(window.ConnectButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains("Enter an IP address.", window.OutputTextBox.Text);
        Assert.False(openedAny);
        window.Close();
    }

    public static TheoryData<string> InvalidPorts => new() { "abc", "0", "70000" };

    [WpfTheory]
    [MemberData(nameof(InvalidPorts))]
    public async Task Connect_InvalidPort_ShowsPortError(string port)
    {
        ConnectionWindow window = NewWindow();
        UiAssert.Type(window.AddressTextBox, "127.0.0.1");
        UiAssert.Type(window.PortTextBox, port);
        Press(window.ConnectButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains("not a valid port", window.OutputTextBox.Text);
        window.Close();
    }

    [WpfFact]
    public async Task Connect_UnreachableEndpoint_LogsFailure_KeepsButtonEnabled_OpensNothing()
    {
        int deadPort = GetFreePort();
        bool openedAny = false;
        ConnectionWindow window = NewWindow(
            lobbyProbe: _ => { openedAny = true; return true; },
            refereeProbe: _ => { openedAny = true; return true; });

        UiAssert.Type(window.AddressTextBox, "127.0.0.1");
        UiAssert.Type(window.PortTextBox, deadPort.ToString());
        Press(window.ConnectButton);

        await WaitForAsync(() => window.OutputTextBox.Text.Contains("Connection failed:"), seconds: 30);
        await WaitForAsync(() => window.ConnectButton.IsEnabled, seconds: 30);

        Assert.Contains("Connection failed:", window.OutputTextBox.Text);
        Assert.False(openedAny);
        Assert.True(window.ConnectButton.IsEnabled);
        window.Close();
    }

    [WpfFact]
    public async Task Host_FreePort_OpensReferee_LogsListening()
    {
        ServerWindow? opened = null;
        ConnectionWindow window = NewWindow(refereeProbe: w => { opened = w; return true; });
        int port = GetFreePort();

        UiAssert.Type(window.HostPortTextBox, port.ToString());
        Press(window.HostButton);

        await WaitForAsync(() => opened is not null);

        Assert.NotNull(opened);
        Assert.Contains($"Listening on port {port}", window.OutputTextBox.Text);
        opened!.Close();  // disposes lobby + server transport
        window.Close();
        await TestDispatcher.FlushAsync();
    }

    [WpfFact]
    public async Task Host_BoundPort_LogsCouldNotStart()
    {
        using PortHolder holder = new(); // occupies a live port
        ConnectionWindow window = NewWindow();
        UiAssert.Type(window.HostPortTextBox, holder.Port.ToString());

        Press(window.HostButton);
        await TestDispatcher.FlushAsync();

        Assert.Contains("Could not start the host:", window.OutputTextBox.Text);
        window.Close();
    }

    [WpfFact]
    public async Task Connect_LongName_LobbyTitleCarriesTrimmedName()
    {
        using TestServer server = new();
        LobbyWindow? opened = null;
        ConnectionWindow window = NewWindow(lobbyProbe: w => { opened = w; return true; });

        UiAssert.Type(window.AddressTextBox, "127.0.0.1");
        UiAssert.Type(window.PortTextBox, server.Port.ToString());
        UiAssert.Type(window.NameTextBox, "              Alice              ");
        Press(window.ConnectButton);

        await WaitForAsync(() => opened is not null);

        Assert.EndsWith("— Alice", opened!.Title);
        opened.Close();
        window.Close();
        await TestDispatcher.FlushAsync();
    }
}
