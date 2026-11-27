// Fakes for the evaluation tests of the maintenance helpers (plan 0004, layer 7, packet P7a).
namespace Graphiti.Port.Evaluation;

using Microsoft.Extensions.Logging;

/// <summary>A logger factory that keeps each log line of each category, with its Python level name, in order.</summary>
internal sealed class MaintenanceLogCapture : ILoggerFactory, ILoggerProvider
{
    internal MaintenanceLogCapture(List<(string Level, string Message)>? lines = null)
    {
        Lines = lines ?? [];
    }

    internal List<(string Level, string Message)> Lines { get; }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Logger(Lines);

    public void Dispose()
    {
    }

    private sealed class Logger(List<(string Level, string Message)> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Add((logLevel switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARNING",
                LogLevel.Error => "ERROR",
                _ => logLevel.ToString().ToUpperInvariant(),
            }, formatter(state, exception)));
    }
}

/// <summary>A clock that always gives one time, as the recording replaces utc_now with a fixed time.</summary>
internal sealed class MaintenanceFixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
