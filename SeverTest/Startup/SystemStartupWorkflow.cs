using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Monitoring.Application;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Services;
using StackExchange.Redis;

namespace ServerTest.Startup
{
    /// <summary>
    /// 系统启动流程编排，集中处理启动检查与就绪标记
    /// </summary>
    public sealed class SystemStartupWorkflow
    {
        private readonly SystemStartupManager _startupManager;
        private readonly StartupMonitorHost _startupMonitorHost;
        private readonly IConnectionMultiplexer _redis;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IndicatorEngine _indicatorEngine;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StartupOptions _options;
        private readonly ILogger<SystemStartupWorkflow> _logger;

        public SystemStartupWorkflow(
            SystemStartupManager startupManager,
            StartupMonitorHost startupMonitorHost,
            IConnectionMultiplexer redis,
            MarketDataEngine marketDataEngine,
            IndicatorEngine indicatorEngine,
            RealTimeStrategyEngine strategyEngine,
            IOptions<StartupOptions> options,
            ILogger<SystemStartupWorkflow> logger)
        {
            _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
            _startupMonitorHost = startupMonitorHost ?? throw new ArgumentNullException(nameof(startupMonitorHost));
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _indicatorEngine = indicatorEngine ?? throw new ArgumentNullException(nameof(indicatorEngine));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _options = options?.Value ?? new StartupOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RunAsync(
            WebApplication app,
            ServerTest.Options.WebSocketOptions wsConfig,
            Action<WebApplication, ServerTest.Options.WebSocketOptions> configurePipeline)
        {
            _startupMonitorHost.Start(_startupManager);
            PrintBanner("DWQuant 量化交易系统启动流程");

            try
            {
                await StartInfrastructureAsync();
                await StartMarketDataEngineAsync();
                StartIndicatorEngine();
                StartStrategyEngine();
                await StartTradingSystemAsync();
                StartNetwork(app, wsConfig, configurePipeline);

                _startupManager.PrintStatusSummary();
                PrintBanner("系统启动完成，开始监听请求");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "系统启动失败，应用将退出");
                _startupManager.PrintStatusSummary();
                throw;
            }
        }

        private async Task StartInfrastructureAsync()
        {
            _startupManager.MarkStarting(SystemModule.Infrastructure, "Redis、数据库等基础设施");

            var db = _redis.GetDatabase();
            try
            {
                await db.StringSetAsync("__startup_test__", "ok", TimeSpan.FromSeconds(1));
                var testValue = await db.StringGetAsync("__startup_test__");
                if (testValue == "ok")
                {
                    _startupManager.MarkReady(SystemModule.Infrastructure, "Redis 连接正常");
                    return;
                }

                throw new Exception("Redis 测试失败");
            }
            catch (Exception ex)
            {
                _startupManager.MarkFailed(SystemModule.Infrastructure, $"Redis 连接失败: {ex.Message}");
                throw;
            }
        }

        private async Task StartMarketDataEngineAsync()
        {
            _startupManager.MarkStarting(SystemModule.MarketDataEngine, "行情数据引擎（WebSocket 订阅）");

            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.MarketDataInitTimeoutSeconds));
            _logger.LogInformation("等待行情引擎初始化（超时 {Timeout} 秒）...", timeout.TotalSeconds);

            try
            {
                await _marketDataEngine.WaitForInitializationAsync().WaitAsync(timeout);
                _startupManager.MarkReady(SystemModule.MarketDataEngine, "行情数据引擎已就绪");
            }
            catch (TimeoutException)
            {
                var message = $"行情引擎初始化超时（>{timeout.TotalSeconds} 秒）";
                _startupManager.MarkFailed(SystemModule.MarketDataEngine, message);
                _logger.LogError(message);
                throw;
            }
            catch (Exception ex)
            {
                _startupManager.MarkFailed(SystemModule.MarketDataEngine, $"行情引擎初始化失败: {ex.Message}");
                _logger.LogError(ex, "行情引擎初始化失败");
                throw;
            }
        }

        private void StartIndicatorEngine()
        {
            _startupManager.MarkStarting(SystemModule.IndicatorEngine, "指标计算引擎");
            _ = _indicatorEngine;
            _startupManager.MarkReady(SystemModule.IndicatorEngine, "指标引擎已注册");
        }

        private void StartStrategyEngine()
        {
            _startupManager.MarkStarting(SystemModule.StrategyEngine, "实时策略执行引擎");
            _ = _strategyEngine;
            _startupManager.MarkReady(SystemModule.StrategyEngine, "策略引擎已注册");
        }

        private async Task StartTradingSystemAsync()
        {
            _startupManager.MarkStarting(SystemModule.TradingSystem, "实盘交易系统（行情/指标/策略）");

            var warmupSeconds = Math.Max(0, _options.StrategyRuntimeWarmupSeconds);
            if (warmupSeconds > 0)
            {
                _logger.LogInformation("等待策略运行时服务启动...");
                await Task.Delay(TimeSpan.FromSeconds(warmupSeconds));
            }

            _startupManager.MarkReady(SystemModule.TradingSystem, "实盘交易系统已就绪");
        }

        private void StartNetwork(
            WebApplication app,
            ServerTest.Options.WebSocketOptions wsConfig,
            Action<WebApplication, ServerTest.Options.WebSocketOptions> configurePipeline)
        {
            _startupManager.MarkStarting(SystemModule.Network, "网络层（HTTP API + WebSocket）");
            configurePipeline(app, wsConfig);
            _startupManager.MarkReady(SystemModule.Network, "网络层已就绪");
        }

        private void PrintBanner(string title)
        {
            _logger.LogInformation("");
            _logger.LogInformation("===============================================================");
            _logger.LogInformation("  {Title}", title);
            _logger.LogInformation("===============================================================");
            _logger.LogInformation("");
        }
    }
}
