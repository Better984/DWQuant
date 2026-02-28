using Microsoft.Extensions.Logging;
using ServerTest.Modules.StrategyRuntime.Domain;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 主服务器向实盘节点分发策略运行指令。
    /// </summary>
    public sealed class LiveTradingWorkerDispatchService
    {
        private static readonly string[] RunnableStates = { "running", "paused_open_position", "testing" };

        private readonly LiveTradingWorkerRegistry _registry;
        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly ServerRoleRuntime _roleRuntime;
        private readonly ILogger<LiveTradingWorkerDispatchService> _logger;

        public LiveTradingWorkerDispatchService(
            LiveTradingWorkerRegistry registry,
            StrategyRuntimeRepository runtimeRepository,
            ServerRoleRuntime roleRuntime,
            ILogger<LiveTradingWorkerDispatchService> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _runtimeRepository = runtimeRepository ?? throw new ArgumentNullException(nameof(runtimeRepository));
            _roleRuntime = roleRuntime ?? throw new ArgumentNullException(nameof(roleRuntime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task DispatchUpsertAsync(long usId, string reason, CancellationToken ct = default)
        {
            return DispatchAsync(
                LiveTradingWorkerCommandActions.Upsert,
                usId,
                reason,
                ct);
        }

        public Task DispatchRemoveAsync(long usId, string reason, CancellationToken ct = default)
        {
            return DispatchAsync(
                LiveTradingWorkerCommandActions.Remove,
                usId,
                reason,
                ct);
        }

        public async Task SyncRunnableStrategiesToWorkerAsync(string workerId, CancellationToken ct = default)
        {
            if (!_roleRuntime.IsCoreLike || string.IsNullOrWhiteSpace(workerId))
            {
                return;
            }

            var rows = await _runtimeRepository.GetRunnableAsync(RunnableStates, ct).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return;
            }

            var sent = 0;
            foreach (var row in rows)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var payload = new LiveTradingWorkerCommand
                {
                    CommandId = Guid.NewGuid().ToString("N"),
                    Action = LiveTradingWorkerCommandActions.Upsert,
                    UsId = row.UsId,
                    Reason = "worker_initial_sync"
                };

                var envelope = ProtocolEnvelopeFactory.Ok<object>(
                    LiveTradingWorkerMessageTypes.Command,
                    null,
                    payload,
                    "live_sync");

                var ok = await _registry.SendAsync(workerId, envelope, ct).ConfigureAwait(false);
                if (!ok)
                {
                    _logger.LogWarning("向实盘节点发送初始同步失败: workerId={WorkerId} usId={UsId}", workerId, row.UsId);
                    break;
                }

                sent++;
            }

            _logger.LogInformation(
                "实盘节点初始同步完成: workerId={WorkerId} strategyCount={Count}",
                workerId,
                sent);
        }

        private async Task DispatchAsync(string action, long usId, string reason, CancellationToken ct)
        {
            if (!_roleRuntime.IsCoreLike || usId <= 0)
            {
                return;
            }

            var sessions = _registry.GetOnlineSessions();
            if (sessions.Count == 0)
            {
                return;
            }

            var payload = new LiveTradingWorkerCommand
            {
                CommandId = Guid.NewGuid().ToString("N"),
                Action = action,
                UsId = usId,
                Reason = reason
            };

            var envelope = ProtocolEnvelopeFactory.Ok<object>(
                LiveTradingWorkerMessageTypes.Command,
                null,
                payload,
                "live_dispatch");

            var successCount = 0;
            foreach (var session in sessions)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var ok = await _registry.SendAsync(session.WorkerId, envelope, ct).ConfigureAwait(false);
                if (ok)
                {
                    successCount++;
                }
            }

            _logger.LogInformation(
                "已分发实盘策略指令: action={Action} usId={UsId} workerSuccess={SuccessCount}/{Total} reason={Reason}",
                action,
                usId,
                successCount,
                sessions.Count,
                reason);
        }
    }
}
