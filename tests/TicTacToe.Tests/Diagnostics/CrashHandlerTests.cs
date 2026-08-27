using TicTacToe.Core.Diagnostics;
using TicTacToe.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TicTacToe.Tests.Diagnostics;

public sealed class CrashHandlerTests
{
    private sealed class RecordingReporter(Action? onReport = null) : ICrashReporter
    {
        public int Calls { get; private set; }

        public void Report(Exception exception)
        {
            Calls++;
            onReport?.Invoke();
        }
    }

    [Fact]
    public void Fatal_logs_critical_reports_and_returns_true()
    {
        CapturingLogger<CrashHandler> logger = new();
        RecordingReporter reporter = new();
        CrashHandler handler = new(logger, reporter);

        bool handled = handler.Handle("test", new InvalidOperationException("boom"), fatal: true);

        Assert.True(handled);
        Assert.Equal(1, reporter.Calls);
        Assert.Contains(logger.Inner.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public void Non_fatal_logs_error_without_report()
    {
        CapturingLogger<CrashHandler> logger = new();
        RecordingReporter reporter = new();
        CrashHandler handler = new(logger, reporter);

        bool handled = handler.Handle("task", new InvalidOperationException("slip"), fatal: false);

        Assert.False(handled);
        Assert.Equal(0, reporter.Calls);
        Assert.Contains(logger.Inner.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Second_fatal_during_report_is_suppressed()
    {
        CapturingLogger<CrashHandler> logger = new();
        CrashHandler handler = null!;
        RecordingReporter reporter = new(onReport: () =>
            Assert.False(handler.Handle("nested", new Exception(), fatal: true)));
        handler = new CrashHandler(logger, reporter);

        bool handled = handler.Handle("outer", new InvalidOperationException(), fatal: true);

        Assert.True(handled);
        Assert.Equal(1, reporter.Calls);
    }

    [Fact]
    public void Non_fatal_does_not_latch_handler()
    {
        CapturingLogger<CrashHandler> logger = new();
        RecordingReporter reporter = new();
        CrashHandler handler = new(logger, reporter);

        handler.Handle("a", new Exception(), fatal: false);
        handler.Handle("b", new Exception(), fatal: false);

        Assert.Equal(2, logger.Inner.Entries.Count);
    }
}
