using System.Windows;
using TicTacToe.Core.Diagnostics;
using TicTacToe.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TicTacToe;

public partial class App : Application
{
    private static ILoggerFactory? _loggerFactory;

    /// <summary>Lazily created so windows remain constructible without a running
    /// Application (UI tests); the real app initializes it in OnStartup.</summary>
    internal static ILoggerFactory LoggerFactory =>
        _loggerFactory ??= new SingleProviderLoggerFactory(new FileLoggerProvider(new LogConfig()));

    private CrashHandler? _crash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = LoggerFactory;

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
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }
}
