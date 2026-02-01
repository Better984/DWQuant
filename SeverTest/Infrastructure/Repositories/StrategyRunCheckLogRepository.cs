using ServerTest.Infrastructure.Db;
using ServerTest.Models.Strategy;

namespace ServerTest.Infrastructure.Repositories
{
    public sealed class StrategyRunCheckLogRepository
    {
        private readonly IDbManager _db;

        public StrategyRunCheckLogRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<int> InsertAsync(StrategyRunCheckLog log, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO strategy_run_check_log
(
    uid,
    us_id,
    state,
    exchange,
    symbol,
    success,
    reason,
    detail_json,
    created_at
)
VALUES
(
    @Uid,
    @UsId,
    @State,
    @Exchange,
    @Symbol,
    @Success,
    @Reason,
    @DetailJson,
    CURRENT_TIMESTAMP
);";

            return _db.ExecuteAsync(sql, log, null, ct);
        }
    }
}
