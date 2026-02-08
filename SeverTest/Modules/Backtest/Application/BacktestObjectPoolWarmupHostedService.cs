using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Config;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测对象池预热服务：
    /// 在服务器启动时初始化回测热点对象，降低运行期首轮分配抖动。
    /// </summary>
    internal sealed class BacktestObjectPoolWarmupHostedService : IHostedService
    {
        private const int DefaultBarDictionaryPrewarm = 256;
        private const int DefaultConditionResultListPrewarm = 1024;
        private const int DefaultStrategyMethodListPrewarm = 2048;
        private const int DefaultTimestampSetPrewarm = 128;
        private const int DefaultIndicatorTaskPrewarm = 1024;
        private const int DefaultStrategyContextPrewarm = 1024;

        private readonly BacktestObjectPoolManager _objectPoolManager;
        private readonly ServerConfigStore _configStore;
        private readonly ILogger<BacktestObjectPoolWarmupHostedService> _logger;

        public BacktestObjectPoolWarmupHostedService(
            BacktestObjectPoolManager objectPoolManager,
            ServerConfigStore configStore,
            ILogger<BacktestObjectPoolWarmupHostedService> logger)
        {
            _objectPoolManager = objectPoolManager ?? throw new ArgumentNullException(nameof(objectPoolManager));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var barDictionaryPrewarm = ResolvePrewarmCount(
                "Backtest:ObjectPool:BarDictionaryPrewarm",
                DefaultBarDictionaryPrewarm);
            var conditionResultListPrewarm = ResolvePrewarmCount(
                "Backtest:ObjectPool:ConditionResultListPrewarm",
                DefaultConditionResultListPrewarm);
            var strategyMethodListPrewarm = ResolvePrewarmCount(
                "Backtest:ObjectPool:StrategyMethodListPrewarm",
                DefaultStrategyMethodListPrewarm);
            var timestampSetPrewarm = ResolvePrewarmCount(
                "Backtest:ObjectPool:TimestampSetPrewarm",
                DefaultTimestampSetPrewarm);
            var indicatorTaskPrewarm = ResolvePrewarmCount(
                "Backtest:ObjectPool:IndicatorTaskPrewarm",
                DefaultIndicatorTaskPrewarm);
            var strategyContextPrewarm = ResolvePrewarmCount(
                "Backtest:ObjectPool:StrategyContextPrewarm",
                DefaultStrategyContextPrewarm);

            _objectPoolManager.Warmup(
                barDictionaryPrewarm,
                conditionResultListPrewarm,
                strategyMethodListPrewarm,
                timestampSetPrewarm,
                indicatorTaskPrewarm,
                strategyContextPrewarm);

            _logger.LogInformation(
                "回测对象池初始化完成：字典={BarDict} 条件结果List={ConditionList} 条件方法List={MethodList} 时间戳HashSet={TimestampSet} 指标任务={IndicatorTask} 策略上下文={StrategyContext}",
                barDictionaryPrewarm,
                conditionResultListPrewarm,
                strategyMethodListPrewarm,
                timestampSetPrewarm,
                indicatorTaskPrewarm,
                strategyContextPrewarm);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private int ResolvePrewarmCount(string key, int fallback)
        {
            var value = _configStore.GetInt(key, fallback);
            if (value <= 0)
            {
                return fallback;
            }

            return value;
        }
    }
}
