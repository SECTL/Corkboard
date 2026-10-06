using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Services.Logging;

/// <summary>写进 <see cref="FileLoggerProvider" /> 的单个 category 日志器。</summary>
public class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
            return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {categoryName}: {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        provider.WriteLog(line);
    }
}
