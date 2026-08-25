using Microsoft.Extensions.Logging;

namespace Client_Server_App.Tests.TestDoubles;

public sealed class CapturingLogger : ILogger
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public CapturingLogger Inner { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => Inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Inner.Log(logLevel, eventId, state, exception, formatter);
}
