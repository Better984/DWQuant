using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Modules.TradingExecution.Infrastructure;

namespace ServerTest.Modules.TradingExecution.Application
{
    /// <summary>
    /// 平仓写库恢复任务入队服务，供风控平仓、手动平仓等链路在「交易所成功但本地写库失败」时复用。
    /// </summary>
    public sealed class TradeRecoveryEnqueueService
    {
        private const int RecoveryMaxAttempts = 6;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TradeRecoveryEnqueueService> _logger;

        public TradeRecoveryEnqueueService(
            IServiceScopeFactory scopeFactory,
            ILogger<TradeRecoveryEnqueueService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 入队平仓写库恢复任务。交易所已平仓成功，本地写库失败时调用。
        /// </summary>
        public async Task EnqueueCloseWriteRecoveryAsync(
            long uid,
            long? usId,
            long positionId,
            long? exchangeApiKeyId,
            string exchange,
            string symbol,
            string side,
            decimal qty,
            decimal? closePrice,
            DateTime closedAtUtc,
            string lastError,
            CancellationToken ct = default)
        {
            var entity = new TradeRecoveryTaskEntity
            {
                TaskType = TradeRecoveryTaskTypes.CloseWrite,
                Uid = uid,
                UsId = usId,
                PositionId = positionId,
                ExchangeApiKeyId = exchangeApiKeyId,
                Exchange = exchange ?? string.Empty,
                Symbol = symbol ?? string.Empty,
                Side = side ?? string.Empty,
                Qty = qty,
                ClosePrice = closePrice,
                ClosedAtUtc = closedAtUtc == default ? null : closedAtUtc,
                Attempt = 1,
                MaxAttempts = RecoveryMaxAttempts,
                NextRetryAtUtc = DateTime.UtcNow.AddSeconds(2),
                LastError = lastError ?? string.Empty
            };

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<TradeRecoveryTaskRepository>();
                var taskId = await repository.InsertPendingAsync(entity, ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "已持久化平仓写库恢复任务: taskId={TaskId} positionId={PositionId} uid={Uid} usId={UsId}",
                    taskId, positionId, uid, usId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "平仓写库恢复任务入库失败: positionId={PositionId} uid={Uid} usId={UsId}",
                    positionId, uid, usId);
            }
        }
    }
}
