using ServerTest.Infrastructure.Db;

namespace ServerTest.Modules.StrategyEngine.Infrastructure
{
    /// <summary>
    /// 策略系统事件日志仓储。记录系统触发的策略事件（如连续开仓失败暂停）。
    /// </summary>
    public sealed class StrategySystemLogRepository
    {
        private readonly IDbManager _db;

        public StrategySystemLogRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 确保表结构存在。
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS strategy_system_log (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  us_id BIGINT UNSIGNED NOT NULL COMMENT '策略实例ID',
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  event_type VARCHAR(64) NOT NULL COMMENT '事件类型: paused_open_fail 等',
  message VARCHAR(512) NOT NULL COMMENT '事件描述',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
  PRIMARY KEY (id),
  INDEX idx_usid_created (us_id, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='策略系统事件日志（如连续开仓失败暂停）';";

            await _db.ExecuteAsync(sql, null, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 插入一条系统事件日志。
        /// </summary>
        public Task<int> InsertAsync(long usId, long uid, string eventType, string message, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO strategy_system_log (us_id, uid, event_type, message, created_at)
VALUES (@usId, @uid, @eventType, @message, UTC_TIMESTAMP(3));";
            return _db.ExecuteAsync(sql, new { usId, uid, eventType, message }, null, ct);
        }
    }
}
