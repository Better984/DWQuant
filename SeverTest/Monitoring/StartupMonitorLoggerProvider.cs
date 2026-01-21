using System;
using Microsoft.Extensions.Logging;

namespace ServerTest.Monitoring
{
    public sealed class StartupMonitorLoggerProvider : ILoggerProvider
    {
        private readonly StartupMonitorHost _host;

        public StartupMonitorLoggerProvider(StartupMonitorHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new StartupMonitorLogger(_host, categoryName);
        }

        public void Dispose()
        {
        }

        private sealed class StartupMonitorLogger : ILogger
        {
            private readonly StartupMonitorHost _host;
            private readonly string _categoryName;

            public StartupMonitorLogger(StartupMonitorHost host, string categoryName)
            {
                _host = host;
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
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

                if (formatter == null)
                {
                    return;
                }

                var message = formatter(state, exception);
                if (string.IsNullOrWhiteSpace(message) && exception == null)
                {
                    return;
                }

                if (exception != null)
                {
                    message = string.IsNullOrWhiteSpace(message)
                        ? exception.ToString()
                        : $"{message} | {exception}";
                }

                _host.AppendLog(new StartupMonitorLogEntry(DateTime.Now, logLevel, _categoryName, message));
            }
        }
    }
}
