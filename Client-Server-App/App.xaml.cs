using System.Windows;
using Client_Server_App.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Client_Server_App;

public partial class App : Application
{
    internal static ILoggerFactory LoggerFactory { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoggerFactory = new SingleProviderLoggerFactory(new FileLoggerProvider(new LogConfig()));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggerFactory.Dispose();
        base.OnExit(e);
    }
}
