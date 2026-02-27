using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Shared.Infrastructure.Diagnostics;

namespace ServerTest.Modules.Shared.Application.Diagnostics
{
    /// <summary>
    /// 策略链路追踪日志异步写入器（批量刷盘）。
    /// </summary>
    public sealed class StrategyTaskTraceLogWriter : BackgroundService
    {
        private const int BatchSize = 200;

        private readonly StrategyTaskTraceLogQueue _queue;
        private readonly StrategyTaskTraceLogRepository _repository;
        private readonly ILogger<StrategyTaskTraceLogWriter> _logger;

        public StrategyTaskTraceLogWriter(
            StrategyTaskTraceLogQueue queue,
            StrategyTaskTraceLogRepository repository,
            ILogger<StrategyTaskTraceLogWriter> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _repository.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);

            var buffer = new List<StrategyTaskTraceLog>(BatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var item = await _queue.ReadAsync(stoppingToken).ConfigureAwait(false);
                    buffer.Add(item);

                    while (buffer.Count < BatchSize && _queue.TryDequeue(out var next))
                    {
                        buffer.Add(next);
                    }

                    await FlushAsync(buffer, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "策略链路追踪日志写入失败");
                    try
                    {
                        await Task.Delay(100, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            // 停机前尽力冲刷一次剩余日志。
            if (buffer.Count > 0)
            {
                try
                {
                    await FlushAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "策略链路追踪日志停机冲刷失败");
                }
            }
        }

        private async Task FlushAsync(List<StrategyTaskTraceLog> buffer, CancellationToken ct)
        {
            if (buffer.Count == 0)
            {
                return;
            }

            try
            {
                await _repository.InsertBatchAsync(buffer, ct).ConfigureAwait(false);
            }
            finally
            {
                buffer.Clear();
            }
        }
    }
}
