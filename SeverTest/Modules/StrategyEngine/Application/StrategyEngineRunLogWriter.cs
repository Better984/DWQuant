using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.StrategyEngine.Infrastructure;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class StrategyEngineRunLogWriter : BackgroundService
    {
        private readonly StrategyEngineRunLogQueue _queue;
        private readonly StrategyEngineRunLogRepository _repository;
        private readonly ILogger<StrategyEngineRunLogWriter> _logger;

        public StrategyEngineRunLogWriter(
            StrategyEngineRunLogQueue queue,
            StrategyEngineRunLogRepository repository,
            ILogger<StrategyEngineRunLogWriter> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var log in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _repository.InsertAsync(log, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "策略运行日志插入失败");
                }
            }
        }
    }
}
