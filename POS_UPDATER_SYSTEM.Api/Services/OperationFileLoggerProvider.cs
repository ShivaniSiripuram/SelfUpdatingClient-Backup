using System.Collections.Concurrent;
using System.Text;

namespace POS_UPDATER_SYSTEM.Api.Services;

public sealed class OperationFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
    private IExternalScopeProvider _scopeProvider = NullExternalScopeProvider.Instance;

    public ILogger CreateLogger(string categoryName)
    {
        return new OperationFileLogger(categoryName, () => _scopeProvider, _fileLocks);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        foreach (var fileLock in _fileLocks.Values)
        {
            fileLock.Dispose();
        }
    }

    private sealed class OperationFileLogger : ILogger
    {
        private readonly Func<IExternalScopeProvider> _scopeProviderAccessor;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks;

        public OperationFileLogger(
            string categoryName,
            Func<IExternalScopeProvider> scopeProviderAccessor,
            ConcurrentDictionary<string, SemaphoreSlim> fileLocks)
        {
            _scopeProviderAccessor = scopeProviderAccessor;
            _fileLocks = fileLocks;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _scopeProviderAccessor().Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

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

            var logFile = ResolveLogFile();
            if (string.IsNullOrWhiteSpace(logFile))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            var line = FormatLine(logLevel, message, exception);
            var fileLock = _fileLocks.GetOrAdd(logFile, _ => new SemaphoreSlim(1, 1));

            fileLock.Wait();
            try
            {
                File.AppendAllText(logFile, line, Encoding.UTF8);
            }
            finally
            {
                fileLock.Release();
            }
        }

        private string? ResolveLogFile()
        {
            string? logFile = null;
            _scopeProviderAccessor().ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> values)
                {
                    foreach (var value in values)
                    {
                        if (string.Equals(value.Key, "OperationLogFile", StringComparison.OrdinalIgnoreCase))
                        {
                            logFile = Convert.ToString(value.Value);
                        }
                    }
                }
            }, (object?)null);

            return logFile;
        }

        private string FormatLine(LogLevel logLevel, string message, Exception? exception)
        {
            var builder = new StringBuilder()
                .Append('[')
                .Append(ToLabel(logLevel))
                .Append("] ")
                .Append(message);

            if (exception is not null)
            {
                builder.AppendLine()
                    .Append("[ERROR] [SYSTEM] ")
                    .Append(exception.GetType().Name)
                    .Append(": ")
                    .Append(exception.Message);
            }

            builder.AppendLine();
            return builder.ToString();
        }

        private static string ToLabel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "ERROR",
                _ => logLevel.ToString().ToUpperInvariant()
            };
        }
    }

    private sealed class NullExternalScopeProvider : IExternalScopeProvider
    {
        public static readonly NullExternalScopeProvider Instance = new();

        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        {
        }

        public IDisposable Push(object? state)
        {
            return NullScope.Instance;
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
