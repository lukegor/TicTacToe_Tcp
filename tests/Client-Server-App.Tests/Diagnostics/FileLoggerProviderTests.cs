using System.IO;
using ClientServer.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClientServer.Tests.Diagnostics;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "csa-log-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Writes_formatted_lines_and_flushes_on_dispose()
    {
        await using FileLoggerProvider provider = new(new LogConfig(Directory: _dir));
        ILogger logger = provider.CreateLogger("test");

        logger.LogInformation("hello {Who}", "alice");
        await provider.DisposeAsync();

        string text = File.ReadAllText(FindSingleFile());
        Assert.Contains("[INFORMATION]", text);
        Assert.Contains("[test] hello alice", text);
    }

    [Fact]
    public async Task Filters_below_minimum_level()
    {
        await using FileLoggerProvider provider =
            new(new LogConfig(LogLevel.Warning, Directory: _dir));
        ILogger logger = provider.CreateLogger("test");

        logger.LogInformation("ignored");
        await provider.DisposeAsync();

        Assert.DoesNotContain("ignored", File.ReadAllText(FindSingleFile()));
    }

    [Fact]
    public async Task Prunes_to_newest_five_session_files()
    {
        Directory.CreateDirectory(_dir);
        for (int i = 1; i <= 7; i++)
        {
            File.WriteAllText(Path.Combine(_dir, $"2020-01-0{i}_00-00-00.log"), "old");
        }

        await using (FileLoggerProvider _ = new(new LogConfig(Directory: _dir)))
        {
        }

        Assert.Equal(6, Directory.GetFiles(_dir, "*.log").Length); // 5 retained + current session file
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FindSingleFile() =>
        Assert.Single(Directory.GetFiles(_dir, "*.log"));
}
