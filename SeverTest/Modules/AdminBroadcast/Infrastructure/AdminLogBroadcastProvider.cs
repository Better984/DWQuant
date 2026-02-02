using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ServerTest.Modules.AdminBroadcast.Application;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ServerTest.Modules.AdminBroadcast.Infrastructure
{
    /// <summary>
    /// 日志提供者，用于拦截日志并推送给管理�?
    /// </summary>
    public sealed class AdminLogBroadcastProvider : ILoggerProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, AdminLogBroadcastLogger> _loggers = new();

        public AdminLogBroadcastProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new AdminLogBroadcastLogger(name, _serviceProvider));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }

        private sealed class AdminLogBroadcastLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly IServiceProvider _serviceProvider;

            public AdminLogBroadcastLogger(string categoryName, IServiceProvider serviceProvider)
            {
                _categoryName = categoryName;
                _serviceProvider = serviceProvider;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel)
            {
                // 推送 Information 及以上级别，避免 Debug/Trace 造成噪音
                return logLevel >= LogLevel.Information;
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

                try
                {
                    var message = formatter(state, exception);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        return;
                    }

                    // 转换日志级别
                    var level = logLevel switch
                    {
                        LogLevel.Critical => "CRITICAL",
                        LogLevel.Error => "ERROR",
                        LogLevel.Warning => "WARNING",
                        LogLevel.Information => "INFORMATION",
                        LogLevel.Debug => "DEBUG",
                        LogLevel.Trace => "TRACE",
                        LogLevel.None => "NONE",
                        _ => "INFO"
                    };

                    // 异步推送，不阻塞日志记录，使用延迟获取服务避免循环依赖
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var broadcastService = scope.ServiceProvider.GetService<AdminWebSocketBroadcastService>();
                            if (broadcastService != null)
                            {
                                await broadcastService.BroadcastLogAsync(level, message, _categoryName, CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            // 静默失败，避免影响日志记�?
                        }
                    });
                }
                catch
                {
                    // 静默失败，避免影响日志记�?
                }
            }
        }
    }
}



