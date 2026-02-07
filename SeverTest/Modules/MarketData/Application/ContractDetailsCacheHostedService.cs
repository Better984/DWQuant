using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServerTest.Modules.MarketData.Application
{
    /// <summary>
    /// 合约详情缓存后台服务：定期刷新合约详情缓存
    /// 
    /// 功能说明：
    /// - 启动时初始化合约详情缓存（先从数据库读取，再验证是否和交易所一致）
    /// - 每天凌晨 2:00 重新获取并更新所有交易所的合约详情
    /// - 确保缓存数据保持最新
    /// </summary>
    public sealed class ContractDetailsCacheHostedService : BackgroundService
    {
        private readonly ContractDetailsCacheService _cacheService;
        private readonly ILogger<ContractDetailsCacheHostedService> _logger;

        public ContractDetailsCacheHostedService(
            ContractDetailsCacheService cacheService,
            ILogger<ContractDetailsCacheHostedService> logger)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 启动时初始化缓存（先从数据库读取，再验证是否和交易所一致）
                _logger.LogInformation("合约详情缓存后台服务启动，开始初始化缓存...");
                await _cacheService.InitializeAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("合约详情缓存初始化完成");

                // 每天凌晨 2:00 刷新所有交易所的合约详情
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var nextRefreshTime = GetNextRefreshTime();
                        var delay = nextRefreshTime - DateTime.UtcNow;

                        _logger.LogInformation(
                            "下次合约详情刷新时间：{NextRefreshTime} UTC（{DelayHours:F2} 小时后）",
                            nextRefreshTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            delay.TotalHours);

                        // 等待到下次刷新时间
                        if (delay.TotalMilliseconds > 0)
                        {
                            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                        }

                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // 执行每日刷新
                        _logger.LogInformation("开始每日刷新合约详情缓存（从交易所获取并更新数据库）...");
                        await _cacheService.RefreshAllAsync(stoppingToken).ConfigureAwait(false);
                        _logger.LogInformation("每日合约详情缓存刷新完成");
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常关闭
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "每日刷新合约详情缓存失败，将在下次刷新时间重试");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "合约详情缓存后台服务启动失败");
            }
        }

        /// <summary>
        /// 获取下次刷新时间（每天凌晨 2:00 UTC）
        /// </summary>
        private static DateTime GetNextRefreshTime()
        {
            var now = DateTime.UtcNow;
            var today2Am = new DateTime(now.Year, now.Month, now.Day, 2, 0, 0, DateTimeKind.Utc);

            // 如果今天凌晨 2:00 已过，则返回明天凌晨 2:00
            if (now >= today2Am)
            {
                return today2Am.AddDays(1);
            }

            return today2Am;
        }
    }
}
