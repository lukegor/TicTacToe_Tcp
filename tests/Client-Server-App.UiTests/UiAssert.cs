using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace ClientServer.TestSupport;

public static class UiAssert
{
    /// <summary>Presses a button the way a user would.</summary>
    public static void Press(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
}
