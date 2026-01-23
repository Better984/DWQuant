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
                _logger.LogInformation("Historical sync disabled: HistoricalData:SyncEnabled=false");
            }

            try
            {
                if (_options.SyncEnabled)
                {
                    _logger.LogInformation("Historical sync start: incremental backfill");
                    await _syncService.SyncIfNeededAsync(stoppingToken);
                    _logger.LogInformation("Historical sync done: incremental backfill complete");
                }

                if (_options.PreloadEnabled)
                {
                    var preloadStart = ResolvePreloadStartDate();
                    _logger.LogInformation(
                        "Historical preload start: start={StartDate}, maxCacheBars={MaxCacheBars}",
                        preloadStart.ToString("yyyy-MM-dd"),
                        _options.MaxCacheBars);
                    await _syncService.PreloadCacheAsync(preloadStart, stoppingToken);
                    _logger.LogInformation("Historical preload done");
                }

                if (_options.SyncEnabled)
                {
                    await RunPeriodicSyncAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Historical sync/preload failed during startup");
            }
        }

        private async Task RunPeriodicSyncAsync(CancellationToken stoppingToken)
        {
            var interval = ResolveSyncInterval();
            _logger.LogInformation("Historical periodic sync enabled: interval={Minutes} minutes", interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _logger.LogInformation("Historical periodic sync start");
                    await _syncService.SyncIfNeededAsync(stoppingToken);
                    _logger.LogInformation("Historical periodic sync done");
                }
                catch (OperationCanceledException)
                {
                    // Shutdown.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Historical periodic sync failed");
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
