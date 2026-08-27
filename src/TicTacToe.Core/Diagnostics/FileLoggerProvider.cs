using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace TicTacToe.Core.Diagnostics;

/// <summary>Writes formatted log lines to one file per process session through a
/// single background writer; prunes old session files at startup.</summary>
internal sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private const int RetainedFiles = 5;
    private readonly LogConfig _config;
    private readonly Channel<string> _queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private readonly string _filePath;

    public FileLoggerProvider(LogConfig config)
    {
        _config = config;
        string directory = config.Directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TicTacToe",
                "logs");
        Directory.CreateDirectory(directory);
        PruneOldSessions(directory);
        _filePath = Path.Combine(directory, $"{DateTimeOffset.Now:yyyy-MM-dd_HH-mm-ss}.log");
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task WriteLoopAsync()
    {
        using StreamWriter writer = new(_filePath, append: true) { AutoFlush = false };
        await foreach (string line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static void PruneOldSessions(string directory)
    {
        // Timestamped names make lexical order chronological.
        List<FileInfo> files = [.. new DirectoryInfo(directory)
            .EnumerateFiles("*.log")
            .OrderByDescending(static f => f.Name)];
        foreach (FileInfo stale in files.Skip(RetainedFiles))
        {
            try
            {
                stale.Delete();
            }
            catch (IOException)
            {
                // A concurrent process may hold the file; retention is best-effort.
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel is not LogLevel.None && logLevel >= owner._config.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line =
                $"{DateTimeOffset.Now:HH:mm:ss.fff} [{logLevel.ToString().ToUpperInvariant(),-7}] [{category}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            owner.Enqueue(line);
        }
    }
}
