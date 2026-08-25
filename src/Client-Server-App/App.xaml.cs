using System.Windows;
using Client_Server_App.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Client_Server_App;

public partial class App : Application
{
    internal static ILoggerFactory LoggerFactory { get; private set; } = null!;

    private CrashHandler? _crash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoggerFactory = new SingleProviderLoggerFactory(new FileLoggerProvider(new LogConfig()));

        _crash = new CrashHandler(LoggerFactory.CreateLogger<CrashHandler>(), new MessageBoxCrashReporter());
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (_crash.Handle("UI dispatcher", args.Exception, fatal: true))
            {
                Current.Shutdown();
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _crash.Handle("AppDomain", (Exception)args.ExceptionObject, fatal: true);
            if (args.IsTerminating)
            {
                Environment.Exit(1);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _crash.Handle("UnobservedTask", args.Exception, fatal: false);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggerFactory.Dispose();
        base.OnExit(e);
    }
}
