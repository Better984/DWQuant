using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Linq;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 策略租约守护：续租、接管与状态同步。
    /// </summary>
    public sealed class StrategyRuntimeLeaseHostedService : BackgroundService
    {
        private static readonly string[] RunnableStates = { "running", "paused_open_position", "testing" };

        private readonly StrategyOwnershipService _ownership;
        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly StrategyRuntimeLoader _loader;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StrategyOwnershipOptions _options;
        private readonly ILogger<StrategyRuntimeLeaseHostedService> _logger;
        private readonly ConcurrentDictionary<long, DateTime> _lastUpdated = new();
        private DateTime _lastSyncUtc = DateTime.MinValue;

        public StrategyRuntimeLeaseHostedService(
            StrategyOwnershipService ownership,
            StrategyRuntimeRepository runtimeRepository,
            StrategyRuntimeLoader loader,
            RealTimeStrategyEngine strategyEngine,
            IOptions<StrategyOwnershipOptions> options,
            ILogger<StrategyRuntimeLeaseHostedService> logger)
        {
            _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            _runtimeRepository = runtimeRepository ?? throw new ArgumentNullException(nameof(runtimeRepository));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _options = options?.Value ?? new StrategyOwnershipOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_ownership.IsEnabled)
            {
                _logger.LogInformation("策略租约未启用，跳过多实例协调");
                return;
            }

            _logger.LogInformation("策略租约服务启动: InstanceId={InstanceId}", _ownership.InstanceId);

            var renewInterval = TimeSpan.FromSeconds(Math.Max(1, _options.RenewIntervalSeconds));
            var syncInterval = TimeSpan.FromSeconds(Math.Max(1, _options.SyncIntervalSeconds));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RenewOwnedAsync(stoppingToken).ConfigureAwait(false);

                    if (DateTime.UtcNow - _lastSyncUtc >= syncInterval)
                    {
                        await SyncStrategiesAsync(stoppingToken).ConfigureAwait(false);
                        _lastSyncUtc = DateTime.UtcNow;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "策略租约周期执行失败");
                }

                try
                {
                    await Task.Delay(renewInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _ownership.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RenewOwnedAsync(CancellationToken ct)
        {
            var ownedIds = _ownership.GetOwnedIdsSnapshot();
            foreach (var usId in ownedIds)
            {
                if (!await _ownership.TryRenewAsync(usId, ct).ConfigureAwait(false))
                {
                    await ReleaseAndRemoveAsync(usId, "租约已丢失", ct).ConfigureAwait(false);
                }
            }
        }

        private async Task SyncStrategiesAsync(CancellationToken ct)
        {
            var ownedIds = _ownership.GetOwnedIdsSnapshot();
            var ownedSet = ownedIds.ToHashSet();

            if (ownedSet.Count > 0)
            {
                var ownedRows = await _runtimeRepository.GetByIdsAsync(ownedSet, ct).ConfigureAwait(false);
                var ownedMap = ownedRows.ToDictionary(row => row.UsId);

                foreach (var usId in ownedSet)
                {
                    if (!ownedMap.TryGetValue(usId, out var row))
                    {
                        await ReleaseAndRemoveAsync(usId, "策略不存在", ct).ConfigureAwait(false);
                        continue;
                    }

                    if (!IsRunnableState(row.State))
                    {
                        await ReleaseAndRemoveAsync(usId, $"策略状态不可运行: {row.State}", ct).ConfigureAwait(false);
                        continue;
                    }

                    await UpsertIfChangedAsync(row, ct).ConfigureAwait(false);
                }
            }

            var runnableRows = await _runtimeRepository.GetRunnableAsync(RunnableStates, ct).ConfigureAwait(false);
            foreach (var row in runnableRows)
            {
                if (ownedSet.Contains(row.UsId))
                {
                    continue;
                }

                if (!await _ownership.TryAcquireAsync(row.UsId, ct).ConfigureAwait(false))
                {
                    continue;
                }

                ownedSet.Add(row.UsId);

                var runtimeStrategy = await _loader.TryLoadAsync(row, ct).ConfigureAwait(false);
                if (runtimeStrategy == null)
                {
                    await _ownership.ReleaseAsync(row.UsId, ct).ConfigureAwait(false);
                    continue;
                }

                _strategyEngine.UpsertStrategy(runtimeStrategy);
                TrackUpdateTime(row);
                _logger.LogInformation("已接管策略 {UsId}", row.UsId);
            }
        }

        private async Task UpsertIfChangedAsync(Modules.StrategyRuntime.Domain.StrategyRuntimeRow row, CancellationToken ct)
        {
            var updatedAt = row.UpdatedAt?.ToUniversalTime() ?? DateTime.MinValue;
            if (_lastUpdated.TryGetValue(row.UsId, out var last) && last >= updatedAt)
            {
                return;
            }

            var runtimeStrategy = await _loader.TryLoadAsync(row, ct).ConfigureAwait(false);
            if (runtimeStrategy == null)
            {
                _logger.LogWarning("策略 {UsId} 刷新失败，保持当前租约", row.UsId);
                return;
            }

            _strategyEngine.UpsertStrategy(runtimeStrategy);
            TrackUpdateTime(row);
            _logger.LogInformation("策略 {UsId} 已刷新", row.UsId);
        }

        private async Task ReleaseAndRemoveAsync(long usId, string reason, CancellationToken ct)
        {
            _strategyEngine.RemoveStrategy(usId.ToString());
            _ownership.Untrack(usId);
            _lastUpdated.TryRemove(usId, out _);

            await _ownership.ReleaseAsync(usId, ct).ConfigureAwait(false);
            _logger.LogWarning("策略 {UsId} 已释放: {Reason}", usId, reason);
        }

        private static bool IsRunnableState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return false;
            }

            var normalized = state.Trim().ToLowerInvariant();
            return normalized == "running"
                || normalized == "paused_open_position"
                || normalized == "testing";
        }

        private void TrackUpdateTime(Modules.StrategyRuntime.Domain.StrategyRuntimeRow row)
        {
            var updatedAt = row.UpdatedAt?.ToUniversalTime() ?? DateTime.MinValue;
            _lastUpdated[row.UsId] = updatedAt;
        }
    }
}
