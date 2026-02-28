using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Options;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    public sealed class StrategyRuntimeHostedService : BackgroundService
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IndicatorEngine _indicatorEngine;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly ILogger<StrategyRuntimeHostedService> _logger;
        private readonly LiveTradingOptions _liveTradingOptions;

        public StrategyRuntimeHostedService(
            MarketDataEngine marketDataEngine,
            IndicatorEngine indicatorEngine,
            RealTimeStrategyEngine strategyEngine,
            StrategyJsonLoader strategyLoader,
            ILogger<StrategyRuntimeHostedService> logger,
            IOptions<LiveTradingOptions>? liveTradingOptions = null)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _indicatorEngine = indicatorEngine ?? throw new ArgumentNullException(nameof(indicatorEngine));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _strategyLoader = strategyLoader ?? throw new ArgumentNullException(nameof(strategyLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _liveTradingOptions = liveTradingOptions?.Value ?? new LiveTradingOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("实时策略运行服务启动，等待行情引擎初始化完成...");
            await _marketDataEngine.WaitForInitializationAsync();
            _logger.LogInformation("行情引擎初始化完成，开始加载策略配置");

            var indicatorWorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
            var indicatorTask = _indicatorEngine.RunAsync(indicatorWorkerCount, stoppingToken);
            var indicatorAutoTask = _indicatorEngine.SubscribeAndAutoUpdateAsync(
                _marketDataEngine, stoppingToken);

            Task strategyTask;
            var shardCount = _liveTradingOptions.ShardCount;
            if (shardCount > 1)
            {
                var consumer = new KeyShardedStrategyConsumer(
                    _strategyEngine, shardCount, _logger);
                strategyTask = consumer.RunAsync(stoppingToken);
            }
            else
            {
                strategyTask = _strategyEngine.RunWorkersAsync(1, stoppingToken);
            }

            _logger.LogInformation(
                "策略消费模式: {Mode}, shardCount={ShardCount}",
                shardCount > 1 ? "键分片并行" : "单 worker",
                shardCount);

            await Task.WhenAll(indicatorTask, indicatorAutoTask, strategyTask);
        }

        private void LoadTestStrategy()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "teststrategy.json");
            var strategy = _strategyLoader.LoadFromFile(path);
            if (strategy == null)
            {
                _logger.LogWarning("策略配置加载失败: {Path}", path);
                return;
            }

            if (strategy.State != StrategyState.Running && strategy.State != StrategyState.Testing)
            {
                _logger.LogWarning(
                    "策略 {Uid} 状态 {State} 不可运行，运行时强制改为 Running",
                    strategy.UidCode,
                    strategy.State);
                strategy.State = StrategyState.Running;
            }

            _strategyEngine.UpsertStrategy(strategy);
            _logger.LogInformation("策略已载入实时引擎: {Uid}", strategy.UidCode);
        }
    }
}
