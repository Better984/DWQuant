using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Modules.StrategyEngine.Application;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    public sealed class StrategyRuntimeBootstrapHostedService : IHostedService
    {
        private static readonly string[] RunnableStates = { "running", "paused_open_position", "testing" };

        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly StrategyRuntimeLoader _runtimeLoader;
        private readonly StrategyOwnershipService _ownership;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly ILogger<StrategyRuntimeBootstrapHostedService> _logger;

        public StrategyRuntimeBootstrapHostedService(
            StrategyRuntimeRepository runtimeRepository,
            StrategyRuntimeLoader runtimeLoader,
            StrategyOwnershipService ownership,
            RealTimeStrategyEngine strategyEngine,
            ILogger<StrategyRuntimeBootstrapHostedService> logger)
        {
            _runtimeRepository = runtimeRepository ?? throw new ArgumentNullException(nameof(runtimeRepository));
            _runtimeLoader = runtimeLoader ?? throw new ArgumentNullException(nameof(runtimeLoader));
            _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("从 user_strategy 引导运行时策略...");

            try
            {
                var rows = await _runtimeRepository.GetRunnableAsync(RunnableStates, cancellationToken).ConfigureAwait(false);

                var loaded = 0;
                var skipped = 0;
                foreach (var row in rows)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!await _ownership.TryAcquireAsync(row.UsId, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.LogInformation("策略 {UsId} 已被其他实例托管，跳过加载", row.UsId);
                        skipped++;
                        continue;
                    }

                    var runtimeStrategy = await _runtimeLoader.TryLoadAsync(row, cancellationToken).ConfigureAwait(false);
                    if (runtimeStrategy == null)
                    {
                        _logger.LogWarning("策略 {UsId} 运行时加载失败，释放租约", row.UsId);
                        await _ownership.ReleaseAsync(row.UsId, cancellationToken).ConfigureAwait(false);
                        skipped++;
                        continue;
                    }

                    _strategyEngine.UpsertStrategy(runtimeStrategy);
                    _ownership.TrackOwned(row.UsId);
                    loaded++;
                }

                _logger.LogInformation("运行时策略引导完成：已加载={Loaded}, 已跳过={Skipped}", loaded, skipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "运行时策略引导失败");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
