using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProductCatalog.IntegrationTests.Infrastructure;

/// <summary>Keeps every log message in memory, so tests can check what an operator would see.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, string Message)> _entries = new();

    public IReadOnlyList<string> Messages(string category)
        => _entries.Where(e => e.Category == category).Select(e => e.Message).ToList();

    /// <summary>The request log is written as the response completes, which can be just after the client has it.</summary>
    public async Task<IReadOnlyList<string>> WaitForAsync(string category, string text)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var matches = Messages(category).Where(m => m.Contains(text, StringComparison.Ordinal)).ToList();
            if (matches.Count > 0)
            {
                return matches;
            }

            await Task.Delay(20);
        }

        return [];
    }

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(string Category, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue((category, formatter(state, exception)));
    }
}
