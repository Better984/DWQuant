using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Services;

namespace ServerTest.Modules.TradingExecution.Infrastructure
{
    /// <summary>
    /// 订单请求与事件流水仓储。
    /// </summary>
    public sealed class OrderLifecycleRepository
    {
        private readonly DatabaseService _db;
        private readonly ILogger<OrderLifecycleRepository> _logger;

        public OrderLifecycleRepository(DatabaseService db, ILogger<OrderLifecycleRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string requestSql = @"
CREATE TABLE IF NOT EXISTS trade_order_request (
  request_id BIGINT NOT NULL AUTO_INCREMENT,
  target_id VARCHAR(64) NOT NULL COMMENT '目标ID',
  strategy_uid VARCHAR(64) NOT NULL DEFAULT '' COMMENT '策略唯一标识',
  uid BIGINT NULL COMMENT '用户ID',
  us_id BIGINT NULL COMMENT '策略实例ID',
  strategy_version_id BIGINT NULL COMMENT '策略版本ID',
  strategy_version_no INT NULL COMMENT '策略版本号',
  exchange_api_key_id BIGINT NULL COMMENT '交易所API Key ID',
  exchange VARCHAR(32) NOT NULL DEFAULT '' COMMENT '交易所',
  symbol VARCHAR(64) NOT NULL DEFAULT '' COMMENT '交易对',
  target_type VARCHAR(32) NOT NULL DEFAULT '' COMMENT '目标类型',
  position_side VARCHAR(16) NOT NULL DEFAULT '' COMMENT '仓位方向',
  order_side VARCHAR(16) NOT NULL DEFAULT '' COMMENT '下单方向',
  reduce_only TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否只减仓',
  is_testing TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否testing模式',
  stage VARCHAR(64) NOT NULL DEFAULT '' COMMENT '策略阶段',
  method VARCHAR(64) NOT NULL DEFAULT '' COMMENT '动作方法',
  requested_qty DECIMAL(36,18) NOT NULL DEFAULT 0 COMMENT '策略请求数量',
  normalized_qty DECIMAL(36,18) NULL COMMENT '规则归一化后数量',
  max_position_qty DECIMAL(36,18) NOT NULL DEFAULT 0 COMMENT '最大持仓数量',
  leverage INT NOT NULL DEFAULT 1 COMMENT '杠杆',
  signal_time DATETIME(3) NOT NULL COMMENT '信号时间(UTC)',
  trigger_results_json LONGTEXT NULL COMMENT '触发条件快照JSON',
  risk_checks_json LONGTEXT NULL COMMENT '风控快照JSON',
  latest_status VARCHAR(32) NOT NULL DEFAULT 'pending' COMMENT '当前状态',
  status_message VARCHAR(512) NULL COMMENT '当前状态说明',
  exchange_order_id VARCHAR(128) NULL COMMENT '交易所订单ID',
  average_price DECIMAL(36,18) NULL COMMENT '成交均价',
  recovery_task_id BIGINT NULL COMMENT '关联恢复任务ID',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3) COMMENT '更新时间',
  completed_at DATETIME(3) NULL COMMENT '完成时间',
  PRIMARY KEY (request_id),
  UNIQUE KEY uk_target_id (target_id),
  INDEX idx_uid_created (uid, created_at DESC),
  INDEX idx_usid_created (us_id, created_at DESC),
  INDEX idx_status_created (latest_status, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='交易订单请求主表';";

            const string eventSql = @"
CREATE TABLE IF NOT EXISTS trade_order_event (
  event_id BIGINT NOT NULL AUTO_INCREMENT,
  request_id BIGINT NOT NULL COMMENT '请求ID',
  event_type VARCHAR(32) NOT NULL DEFAULT '' COMMENT '事件类型',
  status VARCHAR(32) NOT NULL DEFAULT '' COMMENT '事件对应状态',
  message VARCHAR(512) NOT NULL DEFAULT '' COMMENT '事件说明',
  detail_json LONGTEXT NULL COMMENT '事件详情JSON',
  occurred_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '事件时间',
  PRIMARY KEY (event_id),
  INDEX idx_request_time (request_id, occurred_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='交易订单事件表';";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using (var cmd = new MySqlCommand(requestSql, conn))
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var cmd = new MySqlCommand(eventSql, conn))
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        public async Task<long> InsertAsync(TradingOrderRequestEntity request, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO trade_order_request
(
  target_id,
  strategy_uid,
  uid,
  us_id,
  strategy_version_id,
  strategy_version_no,
  exchange_api_key_id,
  exchange,
  symbol,
  target_type,
  position_side,
  order_side,
  reduce_only,
  is_testing,
  stage,
  method,
  requested_qty,
  normalized_qty,
  max_position_qty,
  leverage,
  signal_time,
  trigger_results_json,
  risk_checks_json,
  latest_status,
  status_message,
  exchange_order_id,
  average_price,
  recovery_task_id
)
VALUES
(
  @targetId,
  @strategyUid,
  @uid,
  @usId,
  @strategyVersionId,
  @strategyVersionNo,
  @exchangeApiKeyId,
  @exchange,
  @symbol,
  @targetType,
  @positionSide,
  @orderSide,
  @reduceOnly,
  @isTesting,
  @stage,
  @method,
  @requestedQty,
  @normalizedQty,
  @maxPositionQty,
  @leverage,
  @signalTime,
  @triggerResultsJson,
  @riskChecksJson,
  @latestStatus,
  @statusMessage,
  @exchangeOrderId,
  @averagePrice,
  @recoveryTaskId
);
SELECT LAST_INSERT_ID();";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@targetId", request.TargetId);
            cmd.Parameters.AddWithValue("@strategyUid", request.StrategyUid);
            cmd.Parameters.AddWithValue("@uid", (object?)request.Uid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@usId", (object?)request.UsId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@strategyVersionId", (object?)request.StrategyVersionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@strategyVersionNo", (object?)request.StrategyVersionNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exchangeApiKeyId", (object?)request.ExchangeApiKeyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exchange", request.Exchange);
            cmd.Parameters.AddWithValue("@symbol", request.Symbol);
            cmd.Parameters.AddWithValue("@targetType", request.TargetType);
            cmd.Parameters.AddWithValue("@positionSide", request.PositionSide);
            cmd.Parameters.AddWithValue("@orderSide", request.OrderSide);
            cmd.Parameters.AddWithValue("@reduceOnly", request.ReduceOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("@isTesting", request.IsTesting ? 1 : 0);
            cmd.Parameters.AddWithValue("@stage", request.Stage);
            cmd.Parameters.AddWithValue("@method", request.Method);
            cmd.Parameters.AddWithValue("@requestedQty", request.RequestedQty);
            cmd.Parameters.AddWithValue("@normalizedQty", (object?)request.NormalizedQty ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@maxPositionQty", request.MaxPositionQty);
            cmd.Parameters.AddWithValue("@leverage", request.Leverage);
            cmd.Parameters.AddWithValue("@signalTime", request.SignalTimeUtc.ToUniversalTime());
            cmd.Parameters.AddWithValue("@triggerResultsJson", request.TriggerResultsJson ?? "[]");
            cmd.Parameters.AddWithValue("@riskChecksJson", request.RiskChecksJson ?? "[]");
            cmd.Parameters.AddWithValue("@latestStatus", request.LatestStatus);
            cmd.Parameters.AddWithValue("@statusMessage", (object?)request.StatusMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exchangeOrderId", (object?)request.ExchangeOrderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@averagePrice", (object?)request.AveragePrice ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@recoveryTaskId", (object?)request.RecoveryTaskId ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(result);
        }

        public async Task AppendEventAsync(TradingOrderEventEntity orderEvent, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO trade_order_event
(
  request_id,
  event_type,
  status,
  message,
  detail_json,
  occurred_at
)
VALUES
(
  @requestId,
  @eventType,
  @status,
  @message,
  @detailJson,
  @occurredAt
);";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@requestId", orderEvent.RequestId);
            cmd.Parameters.AddWithValue("@eventType", orderEvent.EventType);
            cmd.Parameters.AddWithValue("@status", orderEvent.Status);
            cmd.Parameters.AddWithValue("@message", orderEvent.Message);
            cmd.Parameters.AddWithValue("@detailJson", orderEvent.DetailJson ?? "{}");
            cmd.Parameters.AddWithValue("@occurredAt", orderEvent.OccurredAtUtc.ToUniversalTime());
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async Task<int> UpdateStatusAsync(
            long requestId,
            string status,
            string? message,
            decimal? normalizedQty,
            string? exchangeOrderId,
            decimal? averagePrice,
            long? recoveryTaskId,
            string? riskChecksJson,
            bool markCompleted,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE trade_order_request
SET latest_status = @status,
    status_message = @statusMessage,
    normalized_qty = COALESCE(@normalizedQty, normalized_qty),
    exchange_order_id = COALESCE(@exchangeOrderId, exchange_order_id),
    average_price = COALESCE(@averagePrice, average_price),
    recovery_task_id = COALESCE(@recoveryTaskId, recovery_task_id),
    risk_checks_json = COALESCE(@riskChecksJson, risk_checks_json),
    updated_at = UTC_TIMESTAMP(3),
    completed_at = CASE WHEN @markCompleted = 1 THEN UTC_TIMESTAMP(3) ELSE completed_at END
WHERE request_id = @requestId;";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@statusMessage", (object?)message ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@normalizedQty", (object?)normalizedQty ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exchangeOrderId", (object?)exchangeOrderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@averagePrice", (object?)averagePrice ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@recoveryTaskId", (object?)recoveryTaskId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@riskChecksJson", (object?)riskChecksJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@markCompleted", markCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("@requestId", requestId);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
