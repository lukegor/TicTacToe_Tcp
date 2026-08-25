using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace ClientServer.TestSupport;

public static class UiAssert
{
    /// <summary>Presses a button the way a user would.</summary>
    public static void Press(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();

    /// <summary>Attempts a press on a possibly-disabled button. Returns false when
    /// UIA refused because the element is not enabled — i.e., a real user could
    /// not have clicked it either.</summary>
    public static bool TryPress(Button button)
    {
        try
        {
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            return true;
        }
        catch (System.Windows.Automation.ElementNotEnabledException)
        {
            return false;
        }
    }

    /// <summary>Simulates typing into a TextBox: updates the current value
    /// without destroying the active binding (plain assignment would).</summary>
    public static void Type(TextBox box, string text) =>
        box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, text);
}
