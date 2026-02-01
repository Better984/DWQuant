using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Infrastructure.Db;
using System.Text;

namespace ServerTest.Modules.Positions.Infrastructure
{
    public sealed class StrategyPositionRepository
    {
        private readonly IDbManager _db;
        private readonly ILogger<StrategyPositionRepository> _logger;

        public StrategyPositionRepository(IDbManager db, ILogger<StrategyPositionRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<long> InsertAsync(StrategyPosition entity, CancellationToken ct = default)
        {
            var sql = @"
INSERT INTO strategy_position
(
    uid,
    us_id,
    exchange_api_key_id,
    exchange,
    symbol,
    side,
    entry_price,
    qty,
    status,
    stop_loss_price,
    take_profit_price,
    trailing_enabled,
    trailing_stop_price,
    trailing_triggered,
    opened_at,
    closed_at
)
VALUES
(
    @Uid,
    @UsId,
    @ExchangeApiKeyId,
    @Exchange,
    @Symbol,
    @Side,
    @EntryPrice,
    @Qty,
    @Status,
    @StopLossPrice,
    @TakeProfitPrice,
    @TrailingEnabled,
    @TrailingStopPrice,
    @TrailingTriggered,
    @OpenedAt,
    @ClosedAt
);
SELECT LAST_INSERT_ID();";

            return _db.ExecuteScalarAsync<long>(sql, entity, null, ct);
        }

        public Task<StrategyPosition?> FindOpenAsync(
            long uid,
            long usId,
            string exchange,
            string symbol,
            string side,
            CancellationToken ct = default)
        {
            var sql = @"
SELECT *
FROM strategy_position
WHERE uid = @uid
  AND us_id = @usId
  AND exchange = @exchange
  AND symbol = @symbol
  AND side = @side
  AND status = 'Open'
ORDER BY opened_at DESC
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<StrategyPosition>(sql, new { uid, usId, exchange, symbol, side }, null, ct);
        }

        public async Task<IReadOnlyList<StrategyPosition>> GetByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            string? status,
            CancellationToken ct = default)
        {
            if (uid <= 0)
            {
                return Array.Empty<StrategyPosition>();
            }

            var (sql, param) = BuildQuerySql("uid", uid, from, to, status);
            var result = await _db.QueryAsync<StrategyPosition>(sql, param, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        public async Task<IReadOnlyList<StrategyPosition>> GetByUsIdAsync(
            long uid,
            long usId,
            DateTime? from,
            DateTime? to,
            string? status,
            CancellationToken ct = default)
        {
            if (uid <= 0 || usId <= 0)
            {
                return Array.Empty<StrategyPosition>();
            }

            var (sql, param) = BuildQuerySql("us_id", usId, from, to, status, uid);
            var result = await _db.QueryAsync<StrategyPosition>(sql, param, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        public async Task<IReadOnlyList<StrategyPosition>> ListOpenAsync(CancellationToken ct = default)
        {
            var sql = @"
SELECT *
FROM strategy_position
WHERE status = 'Open'
ORDER BY opened_at ASC;";

            var result = await _db.QueryAsync<StrategyPosition>(sql, null, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        public Task<int> UpdateTrailingAsync(long positionId, decimal trailingStopPrice, CancellationToken ct = default)
        {
            var sql = @"
UPDATE strategy_position
SET trailing_stop_price = @trailingStopPrice
WHERE position_id = @positionId AND status = 'Open';";

            return _db.ExecuteAsync(sql, new { positionId, trailingStopPrice }, null, ct);
        }

        public Task<int> CloseAsync(long positionId, bool trailingTriggered, DateTime closedAt, CancellationToken ct = default)
        {
            return CloseAsync(positionId, trailingTriggered, closedAt, null, ct);
        }

        public Task<int> CloseAsync(long positionId, bool trailingTriggered, DateTime closedAt, string? closeReason, CancellationToken ct = default)
        {
            var sql = @"
UPDATE strategy_position
SET status = 'Closed',
    closed_at = @closedAt,
    trailing_triggered = @trailingTriggered,
    close_reason = COALESCE(@closeReason, close_reason),
    trailing_stop_price = trailing_stop_price,
    stop_loss_price = stop_loss_price,
    take_profit_price = take_profit_price
WHERE position_id = @positionId AND status = 'Open';";

            return _db.ExecuteAsync(sql, new { positionId, closedAt, trailingTriggered, closeReason }, null, ct);
        }

        private static (string Sql, object Param) BuildQuerySql(
            string keyColumn,
            long keyValue,
            DateTime? from,
            DateTime? to,
            string? status,
            long? uid = null)
        {
            var builder = new StringBuilder();
            builder.AppendLine("SELECT *");
            builder.AppendLine("FROM strategy_position");
            builder.AppendLine($"WHERE {keyColumn} = @keyValue");

            if (uid.HasValue)
            {
                builder.AppendLine("  AND uid = @uid");
            }

            if (from.HasValue)
            {
                builder.AppendLine("  AND opened_at >= @from");
            }

            if (to.HasValue)
            {
                builder.AppendLine("  AND opened_at <= @to");
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("  AND status = @status");
            }

            builder.AppendLine("ORDER BY opened_at DESC;");

            return (builder.ToString(), new { keyValue, from, to, status, uid });
        }
    }
}
