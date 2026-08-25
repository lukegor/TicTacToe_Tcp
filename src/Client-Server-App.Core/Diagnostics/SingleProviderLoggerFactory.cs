using Microsoft.Extensions.Logging;

namespace Client_Server_App.Diagnostics;

/// <summary>An <see cref="ILoggerFactory"/> over exactly one provider; adding
/// more is deliberately unsupported.</summary>
internal sealed class SingleProviderLoggerFactory(ILoggerProvider provider) : ILoggerFactory
{
    private bool _disposed;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return provider.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (provider as IDisposable)?.Dispose();
    }
}
