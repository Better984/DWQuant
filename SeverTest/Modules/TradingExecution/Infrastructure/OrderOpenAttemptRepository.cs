using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.TradingExecution.Domain;

namespace ServerTest.Modules.TradingExecution.Infrastructure
{
    /// <summary>
    /// 开仓尝试记录仓储。记录每次开仓尝试（成功/失败/被上限阻断），支持快速查询连续失败次数。
    /// </summary>
    public sealed class OrderOpenAttemptRepository
    {
        private readonly IDbManager _db;
        private readonly ILogger<OrderOpenAttemptRepository> _logger;

        public OrderOpenAttemptRepository(IDbManager db, ILogger<OrderOpenAttemptRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 确保表结构存在，并对历史版本做幂等补列。
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string createSql = @"
CREATE TABLE IF NOT EXISTS order_open_attempt (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  us_id BIGINT UNSIGNED NOT NULL COMMENT '策略实例ID',
  exchange VARCHAR(32) NOT NULL COMMENT '交易所',
  symbol VARCHAR(32) NOT NULL COMMENT '交易对',
  side VARCHAR(16) NOT NULL COMMENT '方向: Long/Short',
  success TINYINT(1) NOT NULL COMMENT '1=成功 0=失败',
  error_message VARCHAR(512) NULL COMMENT '失败时错误信息',
  attempt_type VARCHAR(32) NOT NULL DEFAULT 'order_result' COMMENT '事件类型: order_result/blocked_max_position',
  signal_time DATETIME(3) NULL COMMENT '信号命中时间（UTC）',
  signal_price DECIMAL(20,8) NULL COMMENT '信号命中时参考价',
  max_position_qty DECIMAL(20,8) NULL COMMENT '策略配置最大持仓',
  current_open_qty DECIMAL(20,8) NULL COMMENT '阻断时当前同向持仓',
  request_order_qty DECIMAL(20,8) NULL COMMENT '本次计划开仓数量',
  created_at DATETIME(3) NOT NULL COMMENT '尝试时间',
  PRIMARY KEY (id),
  INDEX idx_usid_created (us_id, created_at DESC),
  INDEX idx_usid_type_created (us_id, attempt_type, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='开仓尝试记录（成功/失败/上限阻断）';";

            await _db.ExecuteAsync(createSql, null, null, ct).ConfigureAwait(false);

            await EnsureColumnAsync(
                    "attempt_type",
                    "ALTER TABLE order_open_attempt ADD COLUMN attempt_type VARCHAR(32) NOT NULL DEFAULT 'order_result' COMMENT '事件类型: order_result/blocked_max_position' AFTER error_message;",
                    ct)
                .ConfigureAwait(false);
            await EnsureColumnAsync(
                    "signal_time",
                    "ALTER TABLE order_open_attempt ADD COLUMN signal_time DATETIME(3) NULL COMMENT '信号命中时间（UTC）' AFTER attempt_type;",
                    ct)
                .ConfigureAwait(false);
            await EnsureColumnAsync(
                    "signal_price",
                    "ALTER TABLE order_open_attempt ADD COLUMN signal_price DECIMAL(20,8) NULL COMMENT '信号命中时参考价' AFTER signal_time;",
                    ct)
                .ConfigureAwait(false);
            await EnsureColumnAsync(
                    "max_position_qty",
                    "ALTER TABLE order_open_attempt ADD COLUMN max_position_qty DECIMAL(20,8) NULL COMMENT '策略配置最大持仓' AFTER signal_price;",
                    ct)
                .ConfigureAwait(false);
            await EnsureColumnAsync(
                    "current_open_qty",
                    "ALTER TABLE order_open_attempt ADD COLUMN current_open_qty DECIMAL(20,8) NULL COMMENT '阻断时当前同向持仓' AFTER max_position_qty;",
                    ct)
                .ConfigureAwait(false);
            await EnsureColumnAsync(
                    "request_order_qty",
                    "ALTER TABLE order_open_attempt ADD COLUMN request_order_qty DECIMAL(20,8) NULL COMMENT '本次计划开仓数量' AFTER current_open_qty;",
                    ct)
                .ConfigureAwait(false);
            await EnsureIndexAsync(
                    "idx_usid_type_created",
                    "ALTER TABLE order_open_attempt ADD INDEX idx_usid_type_created (us_id, attempt_type, created_at DESC);",
                    ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 插入一次开仓尝试记录（默认事件类型：开仓执行结果）。
        /// </summary>
        public Task InsertAsync(
            long uid,
            long usId,
            string exchange,
            string symbol,
            string side,
            bool success,
            string? errorMessage,
            CancellationToken ct = default)
        {
            return InsertAsync(
                uid,
                usId,
                exchange,
                symbol,
                side,
                success,
                errorMessage,
                OrderOpenAttemptTypes.OrderResult,
                null,
                null,
                null,
                null,
                null,
                ct);
        }

        /// <summary>
        /// 插入一次开仓尝试记录（可附带信号与持仓上下文）。
        /// </summary>
        public async Task InsertAsync(
            long uid,
            long usId,
            string exchange,
            string symbol,
            string side,
            bool success,
            string? errorMessage,
            string attemptType,
            DateTime? signalTimeUtc,
            decimal? signalPrice,
            decimal? maxPositionQty,
            decimal? currentOpenQty,
            decimal? requestOrderQty,
            CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO order_open_attempt
(
  uid,
  us_id,
  exchange,
  symbol,
  side,
  success,
  error_message,
  attempt_type,
  signal_time,
  signal_price,
  max_position_qty,
  current_open_qty,
  request_order_qty,
  created_at
)
VALUES
(
  @uid,
  @usId,
  @exchange,
  @symbol,
  @side,
  @success,
  @errorMessage,
  @attemptType,
  @signalTimeUtc,
  @signalPrice,
  @maxPositionQty,
  @currentOpenQty,
  @requestOrderQty,
  UTC_TIMESTAMP(3)
);";

            await _db.ExecuteAsync(sql, new
            {
                uid,
                usId,
                exchange = exchange ?? string.Empty,
                symbol = symbol ?? string.Empty,
                side = side ?? string.Empty,
                success = success ? 1 : 0,
                errorMessage,
                attemptType = string.IsNullOrWhiteSpace(attemptType) ? OrderOpenAttemptTypes.OrderResult : attemptType.Trim(),
                signalTimeUtc,
                signalPrice,
                maxPositionQty,
                currentOpenQty,
                requestOrderQty
            }, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 获取某策略最近连续失败次数。从最近一次真实下单尝试往前数，连续失败则累加，遇到成功则停止。
        /// 被持仓上限阻断的记录不会计入连续失败暂停。
        /// </summary>
        public async Task<int> GetConsecutiveFailuresAsync(long usId, int limit = 50, CancellationToken ct = default)
        {
            var safeLimit = Math.Min(Math.Max(1, limit), 200);
            var sql = $@"
SELECT success FROM order_open_attempt
WHERE us_id = @usId
  AND attempt_type = @attemptType
ORDER BY created_at DESC
LIMIT {safeLimit};";

            var rows = await _db.QueryAsync<OrderOpenAttemptRow>(
                    sql,
                    new { usId, attemptType = OrderOpenAttemptTypes.OrderResult },
                    null,
                    ct)
                .ConfigureAwait(false);
            var count = 0;
            foreach (var row in rows)
            {
                if (row.Success != 0)
                {
                    break;
                }
                count++;
            }
            return count;
        }

        private async Task EnsureColumnAsync(string columnName, string alterSql, CancellationToken ct)
        {
            const string checkSql = @"
SELECT COUNT(*) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'order_open_attempt' AND COLUMN_NAME = @columnName;";

            var exists = await _db.ExecuteScalarAsync<int>(checkSql, new { columnName }, null, ct).ConfigureAwait(false);
            if (exists > 0)
            {
                return;
            }

            await _db.ExecuteAsync(alterSql, null, null, ct).ConfigureAwait(false);
            _logger.LogInformation("order_open_attempt 已补充字段: {ColumnName}", columnName);
        }

        private async Task EnsureIndexAsync(string indexName, string alterSql, CancellationToken ct)
        {
            const string checkSql = @"
SELECT COUNT(*) FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'order_open_attempt' AND INDEX_NAME = @indexName;";

            var exists = await _db.ExecuteScalarAsync<int>(checkSql, new { indexName }, null, ct).ConfigureAwait(false);
            if (exists > 0)
            {
                return;
            }

            await _db.ExecuteAsync(alterSql, null, null, ct).ConfigureAwait(false);
            _logger.LogInformation("order_open_attempt 已补充索引: {IndexName}", indexName);
        }

        private sealed record OrderOpenAttemptRow(int Success);
    }
}
