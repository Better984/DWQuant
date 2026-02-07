using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Backtest.Domain;
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
        private readonly ILogger<BacktestProgressPushService> _logger;

        public BacktestProgressPushService(
            IConnectionManager connectionManager,
            ILogger<BacktestProgressPushService> logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
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
