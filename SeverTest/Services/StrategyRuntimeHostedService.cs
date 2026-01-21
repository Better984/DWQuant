using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;

namespace ServerTest.Services
{
    public sealed class StrategyRuntimeHostedService : BackgroundService
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IndicatorEngine _indicatorEngine;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly ILogger<StrategyRuntimeHostedService> _logger;

        public StrategyRuntimeHostedService(
            MarketDataEngine marketDataEngine,
            IndicatorEngine indicatorEngine,
            RealTimeStrategyEngine strategyEngine,
            StrategyJsonLoader strategyLoader,
            ILogger<StrategyRuntimeHostedService> logger)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _indicatorEngine = indicatorEngine ?? throw new ArgumentNullException(nameof(indicatorEngine));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _strategyLoader = strategyLoader ?? throw new ArgumentNullException(nameof(strategyLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("实时策略运行服务启动，等待行情引擎初始化完成...");
            await _marketDataEngine.WaitForInitializationAsync();
            _logger.LogInformation("行情引擎初始化完成，开始加载策略配置");

            LoadTestStrategy();

            var workerCount = Math.Max(1, Environment.ProcessorCount);
            var indicatorTask = _indicatorEngine.RunAsync(workerCount, stoppingToken);
            var strategyTask = _strategyEngine.RunWorkersAsync(workerCount, stoppingToken);

            await Task.WhenAll(indicatorTask, strategyTask);
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
