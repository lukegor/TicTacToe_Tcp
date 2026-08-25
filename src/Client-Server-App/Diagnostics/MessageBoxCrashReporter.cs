using System.IO;
using System.Windows;

using ClientServer.Core.Diagnostics;

namespace ClientServer.App.Diagnostics;

/// <summary>Yes copies details to the clipboard, No opens the log folder,
/// Cancel just closes. The application shuts down afterwards either way.</summary>
internal sealed class MessageBoxCrashReporter : ICrashReporter
{
    public void Report(Exception exception)
    {
        MessageBoxResult choice = MessageBox.Show(
            $"An unexpected error occurred and the application must close.\n\n{exception.Message}\n\n"
            + "Yes: copy details · No: open log folder · Cancel: close",
            "Unexpected error",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Error);

        switch (choice)
        {
            case MessageBoxResult.Yes:
                Clipboard.SetDataObject(exception.ToString());
                break;
            case MessageBoxResult.No:
                string logs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Client-Server-App",
                    "logs");
                if (Directory.Exists(logs))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{logs}\"")
                    {
                        UseShellExecute = true,
                    });
                }

                break;
        }
    }
}
