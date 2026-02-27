using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyRuntime.Domain;
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
        // 多节点下优先安全：租约协调首轮异常即触发安全降级，缩短重复执行窗口。
        private const int LeaseFailureSafeModeThreshold = 1;
        // 续租并行度上限，避免大量策略时续租串行超时。
        private const int RenewParallelismMax = 64;
        private const int RenewParallelismMin = 8;

        private readonly StrategyOwnershipService _ownership;
        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly StrategyRuntimeLoader _loader;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StrategyOwnershipOptions _options;
        private readonly ILogger<StrategyRuntimeLeaseHostedService> _logger;
        private readonly ConcurrentDictionary<long, DateTime> _lastUpdated = new();
        private DateTime _lastSafeModeSyncSkipLogUtc = DateTime.MinValue;
        private int _consecutiveLeaseFailures;
        private volatile bool _safeModeActive;

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
            var renewLoop = RunRenewLoopAsync(renewInterval, stoppingToken);
            var syncLoop = RunSyncLoopAsync(syncInterval, stoppingToken);
            await Task.WhenAll(renewLoop, syncLoop).ConfigureAwait(false);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _ownership.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RenewOwnedAsync(CancellationToken ct)
        {
            var ownedIds = _ownership.GetOwnedIdsSnapshot();
            if (ownedIds.Count == 0)
            {
                return;
            }

            var lostLeaseQueue = new ConcurrentQueue<(long UsId, string? FailureReason)>();
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = ResolveRenewParallelism()
            };

            await Parallel.ForEachAsync(ownedIds, parallelOptions, async (usId, token) =>
            {
                var (success, failureReason) = await _ownership.TryRenewWithReasonAsync(usId, token).ConfigureAwait(false);
                if (!success)
                {
                    lostLeaseQueue.Enqueue((usId, failureReason));
                }
            }).ConfigureAwait(false);

            while (lostLeaseQueue.TryDequeue(out var lost))
            {
                var reason = string.IsNullOrEmpty(lost.FailureReason)
                    ? "租约已丢失"
                    : $"租约已丢失: {lost.FailureReason}";
                await ReleaseAndRemoveAsync(lost.UsId, reason, ct).ConfigureAwait(false);
            }
        }

        private async Task SyncStrategiesAsync(CancellationToken ct)
        {
            var ownedIds = _ownership.GetOwnedIdsSnapshot();
            var ownedSet = ownedIds.ToHashSet();

            if (ownedSet.Count > 0)
            {
                var ownedRows = await _runtimeRepository.GetByIdsHeadersAsync(ownedSet, ct).ConfigureAwait(false);
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

            var runnableRows = await _runtimeRepository.GetRunnableHeadersAsync(RunnableStates, ct).ConfigureAwait(false);
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

                var fullRow = await _runtimeRepository.GetByIdAsync(row.UsId, ct).ConfigureAwait(false);
                if (fullRow == null)
                {
                    await _ownership.ReleaseAsync(row.UsId, ct).ConfigureAwait(false);
                    continue;
                }

                if (!IsRunnableState(fullRow.State))
                {
                    await _ownership.ReleaseAsync(fullRow.UsId, ct).ConfigureAwait(false);
                    continue;
                }

                var runtimeStrategy = await _loader.TryLoadAsync(fullRow, ct).ConfigureAwait(false);
                if (runtimeStrategy == null)
                {
                    await _ownership.ReleaseAsync(fullRow.UsId, ct).ConfigureAwait(false);
                    continue;
                }

                _strategyEngine.UpsertStrategy(runtimeStrategy);
                TrackUpdateTime(fullRow);
                _logger.LogInformation("已接管策略 {UsId}", fullRow.UsId);
            }
        }

        private async Task UpsertIfChangedAsync(StrategyRuntimeRow row, CancellationToken ct)
        {
            var updatedAt = row.UpdatedAt?.ToUniversalTime() ?? DateTime.MinValue;
            var uidCode = row.UsId.ToString();

            if (!_lastUpdated.TryGetValue(row.UsId, out var last))
            {
                // 启动首轮同步先建立水位，避免引导完成后再次全量重载。
                _lastUpdated[row.UsId] = updatedAt;
                if (_strategyEngine.HasStrategy(uidCode))
                {
                    return;
                }
            }
            else if (last >= updatedAt && _strategyEngine.HasStrategy(uidCode))
            {
                return;
            }

            var fullRow = await _runtimeRepository.GetByIdAsync(row.UsId, ct).ConfigureAwait(false);
            if (fullRow == null)
            {
                _logger.LogWarning("策略 {UsId} 刷新失败：策略不存在，保持当前租约", row.UsId);
                return;
            }

            if (!IsRunnableState(fullRow.State))
            {
                await ReleaseAndRemoveAsync(fullRow.UsId, $"策略状态不可运行: {fullRow.State}", ct).ConfigureAwait(false);
                return;
            }

            var runtimeStrategy = await _loader.TryLoadAsync(fullRow, ct).ConfigureAwait(false);
            if (runtimeStrategy == null)
            {
                _logger.LogWarning("策略 {UsId} 刷新失败，保持当前租约", row.UsId);
                return;
            }

            _strategyEngine.UpsertStrategy(runtimeStrategy);
            TrackUpdateTime(fullRow);
            _logger.LogInformation("策略 {UsId} 已刷新", row.UsId);
        }

        private async Task RunRenewLoopAsync(TimeSpan renewInterval, CancellationToken ct)
        {
            await RunRenewCycleAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(renewInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RunRenewCycleAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task RunRenewCycleAsync(CancellationToken ct)
        {
            try
            {
                await RenewOwnedAsync(ct).ConfigureAwait(false);

                if (_consecutiveLeaseFailures > 0)
                {
                    _logger.LogInformation("策略租约协调恢复: 连续失败次数已清零，之前失败次数={FailureCount}", _consecutiveLeaseFailures);
                    _consecutiveLeaseFailures = 0;
                }

                if (_safeModeActive)
                {
                    _safeModeActive = false;
                    _logger.LogInformation("策略租约安全降级已解除");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _consecutiveLeaseFailures++;
                _logger.LogError(ex, "策略租约续租周期执行失败: 连续失败次数={FailureCount}", _consecutiveLeaseFailures);

                if (_consecutiveLeaseFailures >= LeaseFailureSafeModeThreshold)
                {
                    await EnterSafeModeAsync(ct).ConfigureAwait(false);
                }
            }
        }

        private async Task RunSyncLoopAsync(TimeSpan syncInterval, CancellationToken ct)
        {
            await RunSyncCycleAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(syncInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RunSyncCycleAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task RunSyncCycleAsync(CancellationToken ct)
        {
            if (_safeModeActive)
            {
                var now = DateTime.UtcNow;
                if (now - _lastSafeModeSyncSkipLogUtc >= TimeSpan.FromSeconds(30))
                {
                    _lastSafeModeSyncSkipLogUtc = now;
                    _logger.LogWarning("策略租约处于安全降级状态，暂不执行接管同步");
                }
                return;
            }

            try
            {
                await SyncStrategiesAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "策略租约同步周期执行失败");
            }
        }

        private async Task ReleaseAndRemoveAsync(long usId, string reason, CancellationToken ct)
        {
            _strategyEngine.RemoveStrategy(usId.ToString());
            _ownership.Untrack(usId);
            _lastUpdated.TryRemove(usId, out _);

            await _ownership.ReleaseAsync(usId, ct).ConfigureAwait(false);
            _logger.LogWarning("策略 {UsId} 已释放: {Reason}", usId, reason);
        }

        /// <summary>
        /// 当租约协调连续失败时进入安全降级：停止本节点托管策略，降低重复执行风险。
        /// </summary>
        private async Task EnterSafeModeAsync(CancellationToken ct)
        {
            if (!_safeModeActive)
            {
                _safeModeActive = true;
                _logger.LogError("策略租约连续失败达到阈值({Threshold})，进入安全降级，停止本节点已托管策略",
                    LeaseFailureSafeModeThreshold);
            }

            var ownedIds = _ownership.GetOwnedIdsSnapshot();
            foreach (var usId in ownedIds)
            {
                try
                {
                    _strategyEngine.RemoveStrategy(usId.ToString());
                    _ownership.Untrack(usId);
                    _lastUpdated.TryRemove(usId, out _);
                    await _ownership.ReleaseAsync(usId, ct).ConfigureAwait(false);
                    _logger.LogWarning("安全降级已下线策略 {UsId}", usId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "安全降级下线策略失败: {UsId}", usId);
                }
            }
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

        private void TrackUpdateTime(StrategyRuntimeRow row)
        {
            var updatedAt = row.UpdatedAt?.ToUniversalTime() ?? DateTime.MinValue;
            _lastUpdated[row.UsId] = updatedAt;
        }

        private static int ResolveRenewParallelism()
        {
            var degree = Environment.ProcessorCount * 2;
            return Math.Clamp(degree, RenewParallelismMin, RenewParallelismMax);
        }
    }
}
