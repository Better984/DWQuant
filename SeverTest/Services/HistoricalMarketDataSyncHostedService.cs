using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Services
{
    public class HistoricalMarketDataSyncHostedService : BackgroundService
    {
        private readonly HistoricalMarketDataSyncService _syncService;
        private readonly HistoricalMarketDataOptions _options;
        private readonly ILogger<HistoricalMarketDataSyncHostedService> _logger;

        public HistoricalMarketDataSyncHostedService(
            HistoricalMarketDataSyncService syncService,
            IOptions<HistoricalMarketDataOptions> options,
            ILogger<HistoricalMarketDataSyncHostedService> logger)
        {
            _syncService = syncService;
            _options = options?.Value ?? new HistoricalMarketDataOptions();
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.SyncEnabled)
            {
                _logger.LogInformation("历史行情同步已禁用：配置项 HistoricalData:SyncEnabled=false");
            }

            try
            {
                if (_options.SyncEnabled)
                {
                    _logger.LogInformation("历史行情同步开始：检查表结构并执行增量补齐");
                    await _syncService.SyncIfNeededAsync(stoppingToken);
                    _logger.LogInformation("历史行情同步完成：表结构检查与增量补齐结束");
                }

                if (_options.PreloadEnabled)
                {
                    var preloadStart = ResolvePreloadStartDate();
                    _logger.LogInformation(
                        "历史行情预热开始：起始时间={StartDate}，最大缓存条数={MaxCacheBars}",
                        preloadStart.ToString("yyyy-MM-dd"),
                        _options.MaxCacheBars);
                    await _syncService.PreloadCacheAsync(preloadStart, stoppingToken);
                    _logger.LogInformation("历史行情预热完成：缓存已就绪");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史行情启动任务失败（同步或预热执行异常）。");
            }
        }

        private DateTime ResolvePreloadStartDate()
        {
            if (DateTime.TryParse(_options.PreloadStartDate, out var parsed))
            {
                return parsed;
            }

            return new DateTime(2025, 1, 1);
        }
    }
}
