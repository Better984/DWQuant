using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Services;

namespace ServerTest.Modules.TradingExecution.Infrastructure
{
    /// <summary>
    /// 交易恢复任务持久化仓储。
    /// </summary>
    public sealed class TradeRecoveryTaskRepository
    {
        private readonly DatabaseService _db;
        private readonly ILogger<TradeRecoveryTaskRepository> _logger;

        public TradeRecoveryTaskRepository(DatabaseService db, ILogger<TradeRecoveryTaskRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 确保交易恢复任务表结构存在。
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS trade_recovery_task (
  task_id BIGINT NOT NULL AUTO_INCREMENT,
  task_type VARCHAR(32) NOT NULL COMMENT '任务类型：close_write/open_compensation',
  uid BIGINT NULL COMMENT '用户ID',
  us_id BIGINT NULL COMMENT '策略实例ID',
  order_request_id BIGINT NULL COMMENT '关联订单请求ID',
  position_id BIGINT NOT NULL DEFAULT 0 COMMENT '仓位ID',
  exchange_api_key_id BIGINT NULL COMMENT '交易所API Key ID',
  exchange VARCHAR(32) NOT NULL DEFAULT '' COMMENT '交易所',
  symbol VARCHAR(64) NOT NULL DEFAULT '' COMMENT '交易对',
  side VARCHAR(16) NOT NULL DEFAULT '' COMMENT '方向',
  qty DECIMAL(36,18) NOT NULL DEFAULT 0 COMMENT '数量',
  close_price DECIMAL(36,18) NULL COMMENT '平仓价',
  closed_at DATETIME NULL COMMENT '目标平仓时间',
  attempt INT NOT NULL DEFAULT 1 COMMENT '当前尝试次数',
  max_attempts INT NOT NULL DEFAULT 6 COMMENT '最大重试次数',
  status VARCHAR(16) NOT NULL DEFAULT 'pending' COMMENT '任务状态：pending/processing/succeeded/failed',
  next_retry_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '下一次可重试时间',
  processing_token VARCHAR(128) NULL COMMENT '当前处理节点标识',
  processing_at DATETIME NULL COMMENT '开始处理时间',
  last_error TEXT NULL COMMENT '最后错误',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
  completed_at DATETIME NULL COMMENT '完成时间',
  PRIMARY KEY (task_id),
  INDEX idx_status_retry (status, next_retry_at, task_id),
  INDEX idx_uid_status (uid, status),
  INDEX idx_usid_status (us_id, status),
  INDEX idx_processing (status, processing_token, processing_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='交易恢复任务表';";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await EnsureColumnAsync(
                    "order_request_id",
                    "ALTER TABLE trade_recovery_task ADD COLUMN order_request_id BIGINT NULL COMMENT '关联订单请求ID' AFTER us_id;",
                    ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 插入一个待处理恢复任务。
        /// </summary>
        public async Task<long> InsertPendingAsync(TradeRecoveryTaskEntity task, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO trade_recovery_task (
  task_type, uid, us_id, order_request_id, position_id, exchange_api_key_id, exchange, symbol, side, qty,
  close_price, closed_at, attempt, max_attempts, status, next_retry_at, last_error)
VALUES (
  @taskType, @uid, @usId, @orderRequestId, @positionId, @exchangeApiKeyId, @exchange, @symbol, @side, @qty,
  @closePrice, @closedAt, @attempt, @maxAttempts, @status, @nextRetryAt, @lastError);
SELECT LAST_INSERT_ID();";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskType", task.TaskType);
            cmd.Parameters.AddWithValue("@uid", (object?)task.Uid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@usId", (object?)task.UsId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@orderRequestId", (object?)task.OrderRequestId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@positionId", task.PositionId);
            cmd.Parameters.AddWithValue("@exchangeApiKeyId", (object?)task.ExchangeApiKeyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exchange", task.Exchange);
            cmd.Parameters.AddWithValue("@symbol", task.Symbol);
            cmd.Parameters.AddWithValue("@side", task.Side);
            cmd.Parameters.AddWithValue("@qty", task.Qty);
            cmd.Parameters.AddWithValue("@closePrice", (object?)task.ClosePrice ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@closedAt", (object?)task.ClosedAtUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@attempt", Math.Max(1, task.Attempt));
            cmd.Parameters.AddWithValue("@maxAttempts", Math.Max(1, task.MaxAttempts));
            cmd.Parameters.AddWithValue("@status", TradeRecoveryTaskStatuses.Pending);
            cmd.Parameters.AddWithValue("@nextRetryAt", task.NextRetryAtUtc.ToUniversalTime());
            cmd.Parameters.AddWithValue("@lastError", (object?)task.LastError ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(result);
        }

        /// <summary>
        /// 回收长期处于 processing 的任务（例如进程崩溃遗留）。
        /// </summary>
        public async Task<int> RecoverStaleProcessingAsync(int staleSeconds, CancellationToken ct = default)
        {
            var seconds = Math.Max(30, staleSeconds);
            const string sql = @"
UPDATE trade_recovery_task
SET status = @pendingStatus,
    processing_token = NULL,
    processing_at = NULL,
    next_retry_at = UTC_TIMESTAMP(),
    updated_at = UTC_TIMESTAMP()
WHERE status = @processingStatus
  AND processing_at IS NOT NULL
  AND processing_at < DATE_SUB(UTC_TIMESTAMP(), INTERVAL @staleSeconds SECOND)";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@pendingStatus", TradeRecoveryTaskStatuses.Pending);
            cmd.Parameters.AddWithValue("@processingStatus", TradeRecoveryTaskStatuses.Processing);
            cmd.Parameters.AddWithValue("@staleSeconds", seconds);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 领取到期恢复任务并标记 processing。
        /// </summary>
        public async Task<List<TradeRecoveryTaskEntity>> AcquireDueTasksAsync(
            int limit,
            string processingToken,
            CancellationToken ct = default)
        {
            var normalizedLimit = Math.Max(1, Math.Min(limit, 100));
            var result = new List<TradeRecoveryTaskEntity>(normalizedLimit);
            for (var i = 0; i < normalizedLimit; i++)
            {
                ct.ThrowIfCancellationRequested();
                var nextTask = await GetNextDuePendingTaskAsync(ct).ConfigureAwait(false);
                if (nextTask == null)
                {
                    break;
                }

                var claimed = await TryMarkProcessingAsync(nextTask.TaskId, processingToken, ct).ConfigureAwait(false);
                if (!claimed)
                {
                    continue;
                }

                nextTask.Status = TradeRecoveryTaskStatuses.Processing;
                nextTask.ProcessingToken = processingToken;
                nextTask.ProcessingAtUtc = DateTime.UtcNow;
                result.Add(nextTask);
            }

            return result;
        }

        public async Task<int> MarkSucceededAsync(long taskId, string processingToken, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE trade_recovery_task
SET status = @status,
    completed_at = UTC_TIMESTAMP(),
    processing_token = NULL,
    processing_at = NULL,
    updated_at = UTC_TIMESTAMP()
WHERE task_id = @taskId
  AND status = @processingStatus
  AND processing_token = @processingToken";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", TradeRecoveryTaskStatuses.Succeeded);
            cmd.Parameters.AddWithValue("@processingStatus", TradeRecoveryTaskStatuses.Processing);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@processingToken", processingToken);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async Task<int> RequeueAsync(
            long taskId,
            string processingToken,
            int attempt,
            DateTime nextRetryAtUtc,
            string? lastError,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE trade_recovery_task
SET status = @pendingStatus,
    attempt = @attempt,
    next_retry_at = @nextRetryAt,
    last_error = @lastError,
    processing_token = NULL,
    processing_at = NULL,
    updated_at = UTC_TIMESTAMP()
WHERE task_id = @taskId
  AND status = @processingStatus
  AND processing_token = @processingToken";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@pendingStatus", TradeRecoveryTaskStatuses.Pending);
            cmd.Parameters.AddWithValue("@processingStatus", TradeRecoveryTaskStatuses.Processing);
            cmd.Parameters.AddWithValue("@attempt", Math.Max(1, attempt));
            cmd.Parameters.AddWithValue("@nextRetryAt", nextRetryAtUtc.ToUniversalTime());
            cmd.Parameters.AddWithValue("@lastError", (object?)lastError ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@processingToken", processingToken);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async Task<int> MarkFailedAsync(
            long taskId,
            string processingToken,
            int attempt,
            string? lastError,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE trade_recovery_task
SET status = @failedStatus,
    attempt = @attempt,
    last_error = @lastError,
    completed_at = UTC_TIMESTAMP(),
    processing_token = NULL,
    processing_at = NULL,
    updated_at = UTC_TIMESTAMP()
WHERE task_id = @taskId
  AND status = @processingStatus
  AND processing_token = @processingToken";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@failedStatus", TradeRecoveryTaskStatuses.Failed);
            cmd.Parameters.AddWithValue("@processingStatus", TradeRecoveryTaskStatuses.Processing);
            cmd.Parameters.AddWithValue("@attempt", Math.Max(1, attempt));
            cmd.Parameters.AddWithValue("@lastError", (object?)lastError ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@processingToken", processingToken);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private async Task<TradeRecoveryTaskEntity?> GetNextDuePendingTaskAsync(CancellationToken ct)
        {
            const string sql = @"
SELECT
  task_id,
  task_type,
  uid,
  us_id,
  order_request_id,
  position_id,
  exchange_api_key_id,
  exchange,
  symbol,
  side,
  qty,
  close_price,
  closed_at,
  attempt,
  max_attempts,
  status,
  next_retry_at,
  processing_token,
  processing_at,
  last_error,
  created_at,
  updated_at,
  completed_at
FROM trade_recovery_task
WHERE status = @status
  AND next_retry_at <= UTC_TIMESTAMP()
ORDER BY next_retry_at ASC, task_id ASC
LIMIT 1";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", TradeRecoveryTaskStatuses.Pending);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            return ReadEntity(reader);
        }

        private async Task<bool> TryMarkProcessingAsync(long taskId, string processingToken, CancellationToken ct)
        {
            const string sql = @"
UPDATE trade_recovery_task
SET status = @processingStatus,
    processing_token = @processingToken,
    processing_at = UTC_TIMESTAMP(),
    updated_at = UTC_TIMESTAMP()
WHERE task_id = @taskId
  AND status = @pendingStatus";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@processingStatus", TradeRecoveryTaskStatuses.Processing);
            cmd.Parameters.AddWithValue("@pendingStatus", TradeRecoveryTaskStatuses.Pending);
            cmd.Parameters.AddWithValue("@processingToken", processingToken);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return affected > 0;
        }

        private static TradeRecoveryTaskEntity ReadEntity(MySqlDataReader reader)
        {
            return new TradeRecoveryTaskEntity
            {
                TaskId = reader.GetInt64("task_id"),
                TaskType = reader.GetString("task_type"),
                Uid = reader.IsDBNull(reader.GetOrdinal("uid")) ? null : reader.GetInt64("uid"),
                UsId = reader.IsDBNull(reader.GetOrdinal("us_id")) ? null : reader.GetInt64("us_id"),
                OrderRequestId = reader.IsDBNull(reader.GetOrdinal("order_request_id"))
                    ? null
                    : reader.GetInt64("order_request_id"),
                PositionId = reader.GetInt64("position_id"),
                ExchangeApiKeyId = reader.IsDBNull(reader.GetOrdinal("exchange_api_key_id"))
                    ? null
                    : reader.GetInt64("exchange_api_key_id"),
                Exchange = reader.GetString("exchange"),
                Symbol = reader.GetString("symbol"),
                Side = reader.GetString("side"),
                Qty = reader.GetDecimal("qty"),
                ClosePrice = reader.IsDBNull(reader.GetOrdinal("close_price"))
                    ? null
                    : reader.GetDecimal("close_price"),
                ClosedAtUtc = reader.IsDBNull(reader.GetOrdinal("closed_at"))
                    ? null
                    : reader.GetDateTime("closed_at"),
                Attempt = reader.GetInt32("attempt"),
                MaxAttempts = reader.GetInt32("max_attempts"),
                Status = reader.GetString("status"),
                NextRetryAtUtc = reader.GetDateTime("next_retry_at"),
                ProcessingToken = reader.IsDBNull(reader.GetOrdinal("processing_token"))
                    ? null
                    : reader.GetString("processing_token"),
                ProcessingAtUtc = reader.IsDBNull(reader.GetOrdinal("processing_at"))
                    ? null
                    : reader.GetDateTime("processing_at"),
                LastError = reader.IsDBNull(reader.GetOrdinal("last_error"))
                    ? null
                    : reader.GetString("last_error"),
                CreatedAtUtc = reader.GetDateTime("created_at"),
                UpdatedAtUtc = reader.GetDateTime("updated_at"),
                CompletedAtUtc = reader.IsDBNull(reader.GetOrdinal("completed_at"))
                    ? null
                    : reader.GetDateTime("completed_at")
            };
        }

        private async Task EnsureColumnAsync(string columnName, string alterSql, CancellationToken ct)
        {
            const string checkSql = @"
SELECT COUNT(*) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'trade_recovery_task' AND COLUMN_NAME = @columnName;";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var checkCmd = new MySqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@columnName", columnName);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (exists > 0)
            {
                return;
            }

            await using var alterCmd = new MySqlCommand(alterSql, conn);
            await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("trade_recovery_task 已补充字段: {ColumnName}", columnName);
        }
    }
}
