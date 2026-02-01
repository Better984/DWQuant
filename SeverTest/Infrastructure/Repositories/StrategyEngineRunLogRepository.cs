using ServerTest.Infrastructure.Db;
using ServerTest.Models.Strategy;

namespace ServerTest.Infrastructure.Repositories
{
    public sealed class StrategyEngineRunLogRepository
    {
        private readonly IDbManager _db;

        public StrategyEngineRunLogRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<int> InsertAsync(StrategyEngineRunLog log, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO strategy_engine_run_log
(
    run_at,
    exchange,
    symbol,
    timeframe,
    candle_timestamp,
    is_bar_close,
    duration_ms,
    matched_count,
    executed_count,
    skipped_count,
    condition_eval_count,
    action_exec_count,
    open_task_count,
    executed_strategy_ids,
    open_task_strategy_ids,
    extra_json,
    engine_instance
)
VALUES
(
    @RunAt,
    @Exchange,
    @Symbol,
    @Timeframe,
    @CandleTimestamp,
    @IsBarClose,
    @DurationMs,
    @MatchedCount,
    @ExecutedCount,
    @SkippedCount,
    @ConditionEvalCount,
    @ActionExecCount,
    @OpenTaskCount,
    @ExecutedStrategyIds,
    @OpenTaskStrategyIds,
    @ExtraJson,
    @EngineInstance
);";

            return _db.ExecuteAsync(sql, log, null, ct);
        }
    }
}
