using ServerTest.Infrastructure.Db;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.StrategyEngine.Infrastructure
{
    /// <summary>
    /// 测试用：策略检查日志仓储
    /// 注意：这是测试功能，用于记录每个策略维度的每一次检查过程，后续会删除
    /// </summary>
    public sealed class TestStrategyCheckLogRepository
    {
        private readonly IDbManager _db;

        public TestStrategyCheckLogRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 插入检查日志
        /// </summary>
        public Task<int> InsertAsync(TestStrategyCheckLog log, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO test_strategy_check_log
(
    uid,
    us_id,
    exchange,
    symbol,
    timeframe,
    candle_timestamp,
    stage,
    group_index,
    condition_key,
    method,
    is_required,
    success,
    message,
    check_process,
    created_at
)
VALUES
(
    @Uid,
    @UsId,
    @Exchange,
    @Symbol,
    @Timeframe,
    @CandleTimestamp,
    @Stage,
    @GroupIndex,
    @ConditionKey,
    @Method,
    @IsRequired,
    @Success,
    @Message,
    @CheckProcess,
    CURRENT_TIMESTAMP
);";

            return _db.ExecuteAsync(sql, log, null, ct);
        }

        /// <summary>
        /// 根据策略实例ID查询检查日志
        /// </summary>
        public async Task<IReadOnlyList<TestStrategyCheckLog>> GetByUsIdAsync(
            long usId,
            int? limit = null,
            CancellationToken ct = default)
        {
            var sql = @"
SELECT *
FROM test_strategy_check_log
WHERE us_id = @usId
ORDER BY created_at DESC";

            if (limit.HasValue && limit.Value > 0)
            {
                sql += $" LIMIT {limit.Value}";
            }

            var result = await _db.QueryAsync<TestStrategyCheckLog>(sql, new { usId }, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        /// <summary>
        /// 根据策略实例ID和时间范围查询检查日志
        /// </summary>
        public async Task<IReadOnlyList<TestStrategyCheckLog>> GetByUsIdAndTimeRangeAsync(
            long usId,
            DateTime? from,
            DateTime? to,
            int? limit = null,
            CancellationToken ct = default)
        {
            var sql = @"
SELECT *
FROM test_strategy_check_log
WHERE us_id = @usId";

            var parameters = new { usId, from, to };

            if (from.HasValue)
            {
                sql += " AND created_at >= @from";
            }

            if (to.HasValue)
            {
                sql += " AND created_at <= @to";
            }

            sql += " ORDER BY created_at DESC";

            if (limit.HasValue && limit.Value > 0)
            {
                sql += $" LIMIT {limit.Value}";
            }

            var result = await _db.QueryAsync<TestStrategyCheckLog>(sql, parameters, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        /// <summary>
        /// 测试用：清空指定策略的所有检查记录（后续会删除）
        /// </summary>
        public Task<int> DeleteByUsIdAsync(long usId, CancellationToken ct = default)
        {
            const string sql = @"
DELETE FROM test_strategy_check_log
WHERE us_id = @usId;";

            return _db.ExecuteAsync(sql, new { usId }, null, ct);
        }
    }
}
