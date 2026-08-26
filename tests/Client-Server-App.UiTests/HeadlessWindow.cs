using System.Windows;

namespace ClientServer.TestSupport;

/// <summary>Makes any window created in tests incapable of appearing on screen:
/// forced-manual positioning far off-screen, non-activating, and hidden from the
/// taskbar. Real <c>Show()</c> calls still execute their full behavior path.</summary>
public static class HeadlessWindow
{
    private const int OffScreen = -32000;

    public static T Prepare<T>(T window) where T : Window
    {
        // Manual must win over XAML defaults like CenterScreen, which would
        // otherwise override Left/Top at Show() time and put the window back
        // on the user's monitor.
        window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
        window.Left = OffScreen;
        window.Top = OffScreen;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        return window;
    }
}
