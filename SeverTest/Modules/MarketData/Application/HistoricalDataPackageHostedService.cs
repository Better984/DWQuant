using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.MarketData.Application
{
    /// <summary>
    /// 历史K线离线包定时任务。
    /// </summary>
    public sealed class HistoricalDataPackageHostedService : BackgroundService
    {
        private readonly HistoricalDataPackageService _packageService;
        private readonly HistoricalDataPackageOptions _options;
        private readonly ILogger<HistoricalDataPackageHostedService> _logger;

        public HistoricalDataPackageHostedService(
            HistoricalDataPackageService packageService,
            IOptions<HistoricalDataPackageOptions> options,
            ILogger<HistoricalDataPackageHostedService> logger)
        {
            _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
            _options = options?.Value ?? new HistoricalDataPackageOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("历史K线离线包任务已禁用: HistoricalDataPackage:Enabled=false");
                return;
            }

            var interval = ResolveInterval();
            _logger.LogInformation("历史K线离线包任务启动: interval={Minutes}分钟", interval.TotalMinutes);

            await TryRunOnceAsync("启动首次", stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await TryRunOnceAsync("定时", stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 服务关闭中，忽略取消异常。
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "历史K线离线包定时任务异常");
                }
            }
        }

        private async Task TryRunOnceAsync(string stage, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("历史K线离线包任务开始: stage={Stage}", stage);
                var success = await _packageService.GenerateAndUploadAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("历史K线离线包任务完成: stage={Stage}, success={Success}", stage, success);
            }
            catch (OperationCanceledException)
            {
                // 服务关闭中，忽略取消异常。
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史K线离线包任务失败: stage={Stage}", stage);
            }
        }

        private TimeSpan ResolveInterval()
        {
            if (_options.UpdateIntervalMinutes <= 0)
            {
                return TimeSpan.FromMinutes(1440);
            }

            return TimeSpan.FromMinutes(_options.UpdateIntervalMinutes);
        }
    }
}
