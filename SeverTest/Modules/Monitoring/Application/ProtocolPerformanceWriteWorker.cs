using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Monitoring.Infrastructure;

namespace ServerTest.Modules.Monitoring.Application
{
    public sealed class ProtocolPerformanceWriteWorker : BackgroundService
    {
        private readonly ProtocolPerformanceStorageFeature _storageFeature;
        private readonly ProtocolPerformanceWriteQueue _queue;
        private readonly ProtocolPerformanceRepository _repository;
        private readonly ILogger<ProtocolPerformanceWriteWorker> _logger;

        public ProtocolPerformanceWriteWorker(
            ProtocolPerformanceStorageFeature storageFeature,
            ProtocolPerformanceWriteQueue queue,
            ProtocolPerformanceRepository repository,
            ILogger<ProtocolPerformanceWriteWorker> logger)
        {
            _storageFeature = storageFeature ?? throw new ArgumentNullException(nameof(storageFeature));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_storageFeature.IsEnabled)
            {
                _logger.LogInformation("协议性能监控 MySQL 入库已关闭，后台写入任务不启动");
                return;
            }

            await _repository.EnsureTableAsync(stoppingToken).ConfigureAwait(false);

            await foreach (var item in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    if (item.ServerMetric != null)
                    {
                        await _repository.UpsertServerMetricAsync(item.ServerMetric, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    if (item.ClientMetrics != null && item.ClientMetrics.Count > 0)
                    {
                        await _repository.UpsertClientMetricsAsync(item.ClientMetrics, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "协议性能监控写入失败");
                }
            }
        }
    }
}
