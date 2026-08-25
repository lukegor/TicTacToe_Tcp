using Microsoft.Extensions.Logging;

namespace ClientServer.Core.Diagnostics;

/// <summary>Immutable logging settings resolved once at startup.</summary>
internal sealed record LogConfig(LogLevel MinimumLevel = LogLevel.Information, string? Directory = null);
