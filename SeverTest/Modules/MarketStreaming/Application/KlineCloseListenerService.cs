using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServerTest.Modules.MarketStreaming.Application
{
    /// <summary>
    /// K线收线监听服务：监听K线收线事件，立即触发行情推送（不受间隔限制）
    /// </summary>
    public sealed class KlineCloseListenerService : BackgroundService
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly MarketTickerBroadcastService _broadcastService;
        private readonly ILogger<KlineCloseListenerService> _logger;

        public KlineCloseListenerService(
            MarketDataEngine marketDataEngine,
            MarketTickerBroadcastService broadcastService,
            ILogger<KlineCloseListenerService> logger)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _broadcastService = broadcastService ?? throw new ArgumentNullException(nameof(broadcastService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 等待MarketDataEngine初始化完成
            await _marketDataEngine.WaitForInitializationAsync();

            _logger.LogInformation("KlineCloseListenerService 开始监听K线收线事件...");

            // 持续监听MarketDataTask通道，当IsBarClose=true时立即触发推送
            await foreach (var task in _marketDataEngine.ReadAllMarketTasksAsync(stoppingToken))
            {
                try
                {
                    // 只处理收线任务
                    if (!task.IsBarClose)
                    {
                        continue;
                    }

                    // 立即触发行情推送，不受间隔限制
                    await _broadcastService.BroadcastImmediatelyAsync(stoppingToken).ConfigureAwait(false);

                    // _logger.LogDebug(
                    //    "K线收线触发立即推送: {Exchange} {Symbol} {Timeframe} time={Timestamp}",
                    //    task.Exchange, task.Symbol, task.Timeframe, task.CandleTimestamp);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown.
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "K线收线监听处理失败: {Exchange} {Symbol} {Timeframe}", 
                        task.Exchange, task.Symbol, task.Timeframe);
                }
            }
        }
    }
}
