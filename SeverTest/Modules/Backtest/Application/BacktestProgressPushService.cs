using System;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.Backtest.Infrastructure;
using ServerTest.Protocol;
using ServerTest.WebSockets;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测实时进度推送服务：
    /// - 仅在用户存在 WebSocket 连接时推送
    /// - 无连接时静默跳过，不影响回测主流程
    /// </summary>
    public sealed class BacktestProgressPushService
    {
        private const string ProgressType = "backtest.progress";

        private readonly IConnectionManager _connectionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BacktestProgressPushService> _logger;

        public BacktestProgressPushService(
            IConnectionManager connectionManager,
            IServiceProvider serviceProvider,
            ILogger<BacktestProgressPushService> logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task PublishStageAsync(
            BacktestProgressContext? context,
            string stage,
            string stageName,
            string message,
            int? processedBars = null,
            int? totalBars = null,
            long? elapsedMs = null,
            bool? completed = null,
            CancellationToken ct = default)
        {
            var payload = new BacktestProgressMessage
            {
                EventKind = "stage",
                Stage = stage ?? string.Empty,
                StageName = stageName ?? string.Empty,
                Message = message,
                ProcessedBars = processedBars,
                TotalBars = totalBars,
                Progress = BuildProgress(processedBars, totalBars),
                ElapsedMs = elapsedMs,
                Completed = completed
            };

            return PublishAsync(context, payload, ct);
        }

        /// <summary>
        /// 直接按自定义内容推送回测进度消息。
        /// </summary>
        public Task PublishMessageAsync(
            BacktestProgressContext? context,
            BacktestProgressMessage payload,
            CancellationToken ct = default)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            payload.Stage ??= string.Empty;
            payload.StageName ??= string.Empty;
            return PublishAsync(context, payload, ct);
        }

        public Task PublishPositionsAsync(
            BacktestProgressContext? context,
            string stage,
            string stageName,
            int foundPositions,
            int totalPositions,
            string? symbol,
            IReadOnlyList<BacktestTrade>? positions,
            bool completed,
            long? elapsedMs = null,
            CancellationToken ct = default)
        {
            var payload = new BacktestProgressMessage
            {
                EventKind = "positions",
                Stage = stage ?? string.Empty,
                StageName = stageName ?? string.Empty,
                Message = completed ? "仓位汇总完成" : "正在汇总仓位",
                FoundPositions = foundPositions,
                TotalPositions = totalPositions,
                ChunkCount = positions?.Count ?? 0,
                Symbol = symbol,
                Positions = positions == null ? null : new List<BacktestTrade>(positions),
                Progress = totalPositions > 0 ? foundPositions / (decimal)totalPositions : 0m,
                ElapsedMs = elapsedMs,
                Completed = completed
            };

            return PublishAsync(context, payload, ct);
        }

        private async Task PublishAsync(BacktestProgressContext? context, BacktestProgressMessage payload, CancellationToken ct)
        {
            await PersistTaskProgressAsync(context, payload, ct).ConfigureAwait(false);

            if (context?.UserId == null || context.UserId.Value <= 0)
            {
                return;
            }

            var userId = context.UserId.Value.ToString();
            var connections = _connectionManager.GetConnections(userId);
            if (connections.Count == 0)
            {
                return;
            }

            var envelope = ProtocolEnvelopeFactory.Ok<object>(ProgressType, context.ReqId, payload);
            var json = ProtocolJson.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var connection in connections)
            {
                if (connection.Socket.State != WebSocketState.Open)
                {
                    continue;
                }

                try
                {
                    await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "回测进度推送失败: uid={Uid} connectionId={ConnectionId} stage={Stage} kind={Kind}",
                        context.UserId.Value,
                        connection.ConnectionId,
                        payload.Stage,
                        payload.EventKind);
                }
            }
        }

        private async Task PersistTaskProgressAsync(BacktestProgressContext? context, BacktestProgressMessage payload, CancellationToken ct)
        {
            if (context?.TaskId == null)
            {
                return;
            }

            var taskId = context.TaskId.Value;
            if (taskId <= 0)
            {
                return;
            }

            var progress = ResolveProgress(payload);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();
                await repository.UpdateProgressAsync(
                    taskId,
                    progress,
                    payload.Stage,
                    payload.StageName,
                    payload.Message,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回测进度落库失败: taskId={TaskId} stage={Stage}", taskId, payload.Stage);
            }
        }

        private static decimal ResolveProgress(BacktestProgressMessage payload)
        {
            if (payload.Completed == true && payload.Stage.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                return 1m;
            }

            var progress = payload.Progress ?? 0m;
            if (progress < 0m)
            {
                return 0m;
            }

            if (progress > 1m)
            {
                return 1m;
            }

            return progress;
        }

        private static decimal? BuildProgress(int? processed, int? total)
        {
            if (!processed.HasValue || !total.HasValue || total.Value <= 0)
            {
                return null;
            }

            var safeProcessed = Math.Min(Math.Max(processed.Value, 0), total.Value);
            return safeProcessed / (decimal)total.Value;
        }
    }
}
