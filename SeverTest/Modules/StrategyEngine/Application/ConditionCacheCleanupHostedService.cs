using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// 条件缓存清理后台服务：定期清理无引用的条件缓存，降低内存增长与 GC 压力。
    /// </summary>
    public sealed class ConditionCacheCleanupHostedService : BackgroundService
    {
        private readonly ConditionUsageTracker _usageTracker;
        private readonly ConditionCacheOptions _options;
        private readonly ILogger<ConditionCacheCleanupHostedService> _logger;

        public ConditionCacheCleanupHostedService(
            ConditionUsageTracker usageTracker,
            IOptions<ConditionCacheOptions> options,
            ILogger<ConditionCacheCleanupHostedService> logger)
        {
            _usageTracker = usageTracker ?? throw new ArgumentNullException(nameof(usageTracker));
            _options = options?.Value ?? new ConditionCacheOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalSeconds = Math.Max(1, _options.CleanupIntervalSeconds);
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            _logger.LogInformation("条件缓存清理服务启动，清理间隔={IntervalSeconds}秒", intervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _usageTracker.PurgeUnused();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "条件缓存清理执行失败");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
