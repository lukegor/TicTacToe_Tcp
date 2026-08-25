using Microsoft.Extensions.Logging;

namespace ClientServer.Core.Diagnostics;

/// <summary>Performs crash-dialog follow-ups (copy details, open folder).</summary>
internal interface ICrashReporter
{
    void Report(Exception exception);
}

/// <summary>Global crash net. Fatal escapes (UI dispatcher, terminating AppDomain)
/// produce a critical entry and a user-facing report before the caller shuts the
/// app down. Unobserved task exceptions are logged as errors without taking the
/// process down (they are not fatal since .NET 4.5). A fatal hit latches the
/// handler so cascading failures during teardown cannot re-open dialogs.</summary>
internal sealed class CrashHandler(ILogger<CrashHandler> logger, ICrashReporter reporter)
{
    private bool _handling;

    /// <summary>Handles one escape; returns true when a fatal report was performed
    /// (the caller is then expected to shut the application down).</summary>
    public bool Handle(string origin, Exception exception, bool fatal)
    {
        if (_handling)
        {
            return false;
        }

        try
        {
            if (fatal)
            {
                _handling = true;
                logger.LogCritical(exception, "Unhandled exception at {Origin}; shutting down.", origin);
                reporter.Report(exception);
                return true;
            }

            logger.LogError(exception, "Unobserved task exception at {Origin}.", origin);
            return false;
        }
        finally
        {
            if (!fatal)
            {
                _handling = false;
            }
        }
    }
}
