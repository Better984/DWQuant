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
                _logger.LogInformation("历史数据同步已禁用: HistoricalData:SyncEnabled=false");
            }

            try
            {
                if (_options.SyncEnabled)
                {
                    _logger.LogInformation("历史数据同步开始: 增量回填");
                    await _syncService.SyncIfNeededAsync(stoppingToken);
                    _logger.LogInformation("历史数据同步完成: 增量回填完成");
                }

                if (_options.PreloadEnabled)
                {
                    var preloadStart = ResolvePreloadStartDate();
                    _logger.LogInformation(
                        "历史数据预加载开始: 开始日期={StartDate}, 最大缓存K线数={MaxCacheBars}",
                        preloadStart.ToString("yyyy-MM-dd"),
                        _options.MaxCacheBars);
                    await _syncService.PreloadCacheAsync(preloadStart, stoppingToken);
                    _logger.LogInformation("历史数据预加载完成");
                }

                if (_options.SyncEnabled)
                {
                    await RunPeriodicSyncAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "启动时历史数据同步/预加载失败");
            }
        }

        private async Task RunPeriodicSyncAsync(CancellationToken stoppingToken)
        {
            var interval = ResolveSyncInterval();
            _logger.LogInformation("历史数据定期同步已启用: 间隔={Minutes} 分钟", interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _logger.LogInformation("历史数据定期同步开始");
                    await _syncService.SyncIfNeededAsync(stoppingToken);
                    _logger.LogInformation("历史数据定期同步完成");
                }
                catch (OperationCanceledException)
                {
                    // Shutdown.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "历史数据定期同步失败");
                }
            }
        }

        private TimeSpan ResolveSyncInterval()
        {
            if (_options.SyncIntervalMinutes <= 0)
            {
                return TimeSpan.FromMinutes(60);
            }

            return TimeSpan.FromMinutes(_options.SyncIntervalMinutes);
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
