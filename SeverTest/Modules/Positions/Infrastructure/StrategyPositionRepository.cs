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
    strategy_version_id,
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
    closed_at,
    close_price,
    realized_pnl
)
VALUES
(
    @Uid,
    @UsId,
    @StrategyVersionId,
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
    @ClosedAt,
    @ClosePrice,
    @RealizedPnl
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

        public Task<StrategyPosition?> GetByIdAsync(long positionId, long uid, CancellationToken ct = default)
        {
            if (positionId <= 0 || uid <= 0)
            {
                return Task.FromResult<StrategyPosition?>(null);
            }

            var sql = @"
SELECT *
FROM strategy_position
WHERE position_id = @positionId
  AND uid = @uid
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<StrategyPosition>(sql, new { positionId, uid }, null, ct);
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
            return CloseAsync(positionId, trailingTriggered, closedAt, null, null, ct);
        }

        public Task<int> CloseAsync(long positionId, bool trailingTriggered, DateTime closedAt, string? closeReason, CancellationToken ct = default)
        {
            return CloseAsync(positionId, trailingTriggered, closedAt, closeReason, null, ct);
        }

        public Task<int> CloseAsync(
            long positionId,
            bool trailingTriggered,
            DateTime closedAt,
            string? closeReason,
            decimal? closePrice,
            CancellationToken ct = default)
        {
            var sql = @"
UPDATE strategy_position
SET status = 'Closed',
    closed_at = @closedAt,
    trailing_triggered = @trailingTriggered,
    close_reason = COALESCE(@closeReason, close_reason),
    close_price = COALESCE(@closePrice, close_price),
    realized_pnl = CASE
        WHEN @closePrice IS NULL THEN realized_pnl
        WHEN side = 'Short' THEN (entry_price - @closePrice) * qty
        ELSE (@closePrice - entry_price) * qty
    END,
    trailing_stop_price = trailing_stop_price,
    stop_loss_price = stop_loss_price,
    take_profit_price = take_profit_price
WHERE position_id = @positionId AND status = 'Open';";

            return _db.ExecuteAsync(sql, new { positionId, closedAt, trailingTriggered, closeReason, closePrice }, null, ct);
        }

        public Task<int> CountByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT COUNT(*)
FROM strategy_position
WHERE uid = @uid
  AND (@from IS NULL OR opened_at >= @from)
  AND (@to IS NULL OR opened_at <= @to);";

            return _db.ExecuteScalarAsync<int>(sql, new { uid, from, to }, null, ct);
        }

        public Task<int> CountCurrentOpenByUidAsync(long uid, CancellationToken ct = default)
        {
            const string sql = @"
SELECT COUNT(*)
FROM strategy_position FORCE INDEX (idx_uid_status_opened)
WHERE uid = @uid
  AND status = 'Open';";

            return _db.ExecuteScalarAsync<int>(sql, new { uid }, null, ct);
        }

        public async Task<PositionWindowSummaryRecord> GetWindowSummaryByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  COUNT(*) AS open_count,
  SUM(CASE WHEN status = 'Closed' THEN 1 ELSE 0 END) AS closed_count,
  SUM(
      CASE
          WHEN status = 'Closed'
               AND COALESCE(
                   realized_pnl,
                   CASE
                       WHEN close_price IS NULL THEN NULL
                       WHEN side = 'Short' THEN (entry_price - close_price) * qty
                       ELSE (close_price - entry_price) * qty
                   END,
                   0
               ) > 0
          THEN 1
          ELSE 0
      END
  ) AS win_count,
  COALESCE(
      SUM(
          CASE
              WHEN status = 'Closed'
              THEN COALESCE(
                       realized_pnl,
                       CASE
                           WHEN close_price IS NULL THEN 0
                           WHEN side = 'Short' THEN (entry_price - close_price) * qty
                           ELSE (close_price - entry_price) * qty
                       END,
                       0
                   )
              ELSE 0
          END
      ),
      0
  ) AS realized_pnl
FROM strategy_position FORCE INDEX (idx_uid_time)
WHERE uid = @uid
  AND (@from IS NULL OR opened_at >= @from)
  AND (@to IS NULL OR opened_at <= @to);";

            var row = await _db.QuerySingleOrDefaultAsync<PositionWindowSummaryRecord>(sql, new { uid, from, to }, null, ct)
                .ConfigureAwait(false);
            return row ?? new PositionWindowSummaryRecord();
        }

        public async Task<IReadOnlyList<PositionOpenLiteRecord>> GetCurrentOpenLiteByUidAsync(
            long uid,
            CancellationToken ct = default)
        {
            if (uid <= 0)
            {
                return Array.Empty<PositionOpenLiteRecord>();
            }

            const string sql = @"
SELECT
  position_id,
  exchange,
  symbol,
  side,
  entry_price,
  qty,
  opened_at
FROM strategy_position FORCE INDEX (idx_uid_status_opened)
WHERE uid = @uid
  AND status = 'Open'
ORDER BY opened_at DESC;";

            var rows = await _db.QueryAsync<PositionOpenLiteRecord>(sql, new { uid }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<PositionDetailRecord>> GetDetailsByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            int limit,
            int offset,
            CancellationToken ct = default)
        {
            if (uid <= 0 || limit <= 0 || offset < 0)
            {
                return Array.Empty<PositionDetailRecord>();
            }

            var sql = BuildDetailSql(onlyOpen: false, includeRange: true, includePaging: true);
            var rows = await _db.QueryAsync<PositionDetailRecord>(sql, new { uid, from, to, limit, offset }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<PositionDetailRecord>> GetCurrentOpenDetailsByUidAsync(
            long uid,
            int limit,
            CancellationToken ct = default)
        {
            if (uid <= 0 || limit <= 0)
            {
                return Array.Empty<PositionDetailRecord>();
            }

            var sql = BuildDetailSql(onlyOpen: true, includeRange: false, includePaging: true);
            var rows = await _db.QueryAsync<PositionDetailRecord>(sql, new { uid, limit, offset = 0 }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<PositionRecentEventRecord>> GetRecentEventsByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            int limit,
            CancellationToken ct = default)
        {
            if (uid <= 0 || limit <= 0)
            {
                return Array.Empty<PositionRecentEventRecord>();
            }

            var take = Math.Clamp(limit, 1, 200);
            const string sql = @"
SELECT
  e.event_type,
  e.event_at,
  e.position_id,
  e.exchange,
  e.symbol,
  e.side,
  e.qty,
  e.event_price,
  e.realized_pnl,
  e.close_reason
FROM
(
  SELECT
    'Open' AS event_type,
    o.event_at,
    o.position_id,
    o.exchange,
    o.symbol,
    o.side,
    o.qty,
    o.event_price,
    o.realized_pnl,
    o.close_reason
  FROM
  (
    SELECT
      p.opened_at AS event_at,
      p.position_id,
      p.exchange,
      p.symbol,
      p.side,
      p.qty,
      p.entry_price AS event_price,
      NULL AS realized_pnl,
      NULL AS close_reason
    FROM strategy_position p FORCE INDEX (idx_uid_time)
    WHERE p.uid = @uid
      AND (@from IS NULL OR p.opened_at >= @from)
      AND (@to IS NULL OR p.opened_at <= @to)
    ORDER BY p.opened_at DESC
    LIMIT @take
  ) o

  UNION ALL

  SELECT
    'Close' AS event_type,
    c.event_at,
    c.position_id,
    c.exchange,
    c.symbol,
    c.side,
    c.qty,
    c.event_price,
    c.realized_pnl,
    c.close_reason
  FROM
  (
    SELECT
      p.closed_at AS event_at,
      p.position_id,
      p.exchange,
      p.symbol,
      p.side,
      p.qty,
      COALESCE(p.close_price, p.entry_price) AS event_price,
      p.realized_pnl,
      p.close_reason
    FROM strategy_position p
    WHERE p.uid = @uid
      AND p.status = 'Closed'
      AND p.closed_at IS NOT NULL
      AND (@from IS NULL OR p.closed_at >= @from)
      AND (@to IS NULL OR p.closed_at <= @to)
    ORDER BY p.closed_at DESC
    LIMIT @take
  ) c
) e
ORDER BY e.event_at DESC
LIMIT @take;";

            var rows = await _db.QueryAsync<PositionRecentEventRecord>(sql, new { uid, from, to, take }, null, ct)
                .ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<PositionStrategyOpenStatRecord>> GetOpenStatsByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            if (uid <= 0)
            {
                return Array.Empty<PositionStrategyOpenStatRecord>();
            }

            var sql = BuildStrategyOpenStatsSql();
            var rows = await _db.QueryAsync<PositionStrategyOpenStatRecord>(sql, new { uid, from, to }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<PositionVersionParticipationRecord>> GetVersionParticipationByUidAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            if (uid <= 0)
            {
                return Array.Empty<PositionVersionParticipationRecord>();
            }

            var sql = BuildVersionParticipationSql();
            var rows = await _db.QueryAsync<PositionVersionParticipationRecord>(sql, new { uid, from, to }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private static string BuildDetailSql(bool onlyOpen, bool includeRange, bool includePaging)
        {
            var builder = new StringBuilder();
            builder.AppendLine(BuildPositionDecoratedCteSql(onlyOpen, includeRange));
            builder.AppendLine("SELECT");
            builder.AppendLine("  d.position_id,");
            builder.AppendLine("  d.uid,");
            builder.AppendLine("  d.us_id,");
            builder.AppendLine("  d.strategy_version_id,");
            builder.AppendLine("  d.effective_version_id,");
            builder.AppendLine("  d.version_source,");
            builder.AppendLine("  sv.version_no AS effective_version_no,");
            builder.AppendLine("  sv.created_at AS effective_version_created_at,");
            builder.AppendLine("  sv.changelog AS effective_version_changelog,");
            builder.AppendLine("  d.exchange_api_key_id,");
            builder.AppendLine("  d.exchange,");
            builder.AppendLine("  d.symbol,");
            builder.AppendLine("  d.side,");
            builder.AppendLine("  d.entry_price,");
            builder.AppendLine("  d.qty,");
            builder.AppendLine("  d.status,");
            builder.AppendLine("  d.stop_loss_price,");
            builder.AppendLine("  d.take_profit_price,");
            builder.AppendLine("  d.trailing_enabled,");
            builder.AppendLine("  d.trailing_triggered,");
            builder.AppendLine("  d.trailing_stop_price,");
            builder.AppendLine("  d.close_reason,");
            builder.AppendLine("  d.close_price,");
            builder.AppendLine("  d.realized_pnl,");
            builder.AppendLine("  d.opened_at,");
            builder.AppendLine("  d.closed_at,");
            builder.AppendLine("  d.alias_name,");
            builder.AppendLine("  d.strategy_name,");
            builder.AppendLine("  d.strategy_state,");
            builder.AppendLine("  d.def_id,");
            builder.AppendLine("  d.def_type");
            builder.AppendLine("FROM decorated d");
            builder.AppendLine("LEFT JOIN strategy_version sv ON sv.version_id = d.effective_version_id");
            builder.AppendLine("ORDER BY d.opened_at DESC");
            if (includePaging)
            {
                builder.AppendLine("LIMIT @limit OFFSET @offset");
            }

            builder.AppendLine(";");
            return builder.ToString();
        }

        private static string BuildStrategyOpenStatsSql()
        {
            var builder = new StringBuilder();
            builder.AppendLine(BuildPositionDecoratedCteSql(onlyOpen: false, includeRange: true));
            builder.AppendLine("SELECT");
            builder.AppendLine("  d.us_id,");
            builder.AppendLine("  d.alias_name,");
            builder.AppendLine("  d.def_id,");
            builder.AppendLine("  d.strategy_name,");
            builder.AppendLine("  COUNT(*) AS open_success_count,");
            builder.AppendLine("  SUM(CASE WHEN d.status = 'Open' THEN 1 ELSE 0 END) AS current_open_count,");
            builder.AppendLine("  SUM(CASE WHEN d.status = 'Closed' THEN 1 ELSE 0 END) AS closed_count,");
            builder.AppendLine("  MIN(d.opened_at) AS first_opened_at,");
            builder.AppendLine("  MAX(d.opened_at) AS last_opened_at");
            builder.AppendLine("FROM decorated d");
            builder.AppendLine("GROUP BY d.us_id, d.alias_name, d.def_id, d.strategy_name");
            builder.AppendLine("ORDER BY open_success_count DESC, last_opened_at DESC;");
            return builder.ToString();
        }

        private static string BuildVersionParticipationSql()
        {
            var builder = new StringBuilder();
            builder.AppendLine(BuildPositionDecoratedCteSql(onlyOpen: false, includeRange: true));
            builder.AppendLine("SELECT");
            builder.AppendLine("  d.effective_version_id,");
            builder.AppendLine("  sv.version_no AS effective_version_no,");
            builder.AppendLine("  sv.created_at AS effective_version_created_at,");
            builder.AppendLine("  sv.changelog AS effective_version_changelog,");
            builder.AppendLine("  COUNT(*) AS open_success_count,");
            builder.AppendLine("  COUNT(DISTINCT d.us_id) AS strategy_count,");
            builder.AppendLine("  GROUP_CONCAT(DISTINCT CAST(d.us_id AS CHAR) ORDER BY d.us_id SEPARATOR ',') AS strategy_us_ids_csv,");
            builder.AppendLine("  GROUP_CONCAT(DISTINCT d.alias_name ORDER BY d.alias_name SEPARATOR ' / ') AS strategy_alias_names,");
            builder.AppendLine("  SUM(CASE WHEN d.version_source = 'snapshot' THEN 1 ELSE 0 END) AS snapshot_count,");
            builder.AppendLine("  SUM(CASE WHEN d.version_source = 'inferred' THEN 1 ELSE 0 END) AS inferred_count,");
            builder.AppendLine("  SUM(CASE WHEN d.version_source = 'pinned' THEN 1 ELSE 0 END) AS pinned_count");
            builder.AppendLine("FROM decorated d");
            builder.AppendLine("LEFT JOIN strategy_version sv ON sv.version_id = d.effective_version_id");
            builder.AppendLine("GROUP BY d.effective_version_id, sv.version_no, sv.created_at, sv.changelog");
            builder.AppendLine("ORDER BY open_success_count DESC, d.effective_version_id DESC;");
            return builder.ToString();
        }

        private static string BuildPositionDecoratedCteSql(bool onlyOpen, bool includeRange)
        {
            var builder = new StringBuilder();
            builder.AppendLine("WITH base AS (");
            builder.AppendLine("SELECT");
            builder.AppendLine("  p.position_id,");
            builder.AppendLine("  p.uid,");
            builder.AppendLine("  p.us_id,");
            builder.AppendLine("  p.strategy_version_id,");
            builder.AppendLine("  p.exchange_api_key_id,");
            builder.AppendLine("  p.exchange,");
            builder.AppendLine("  p.symbol,");
            builder.AppendLine("  p.side,");
            builder.AppendLine("  p.entry_price,");
            builder.AppendLine("  p.qty,");
            builder.AppendLine("  p.status,");
            builder.AppendLine("  p.stop_loss_price,");
            builder.AppendLine("  p.take_profit_price,");
            builder.AppendLine("  p.trailing_enabled,");
            builder.AppendLine("  p.trailing_triggered,");
            builder.AppendLine("  p.trailing_stop_price,");
            builder.AppendLine("  p.close_reason,");
            builder.AppendLine("  p.close_price,");
            builder.AppendLine("  p.realized_pnl,");
            builder.AppendLine("  p.opened_at,");
            builder.AppendLine("  p.closed_at,");
            builder.AppendLine("  us.alias_name,");
            builder.AppendLine("  us.state AS strategy_state,");
            builder.AppendLine("  us.def_id,");
            builder.AppendLine("  us.pinned_version_id,");
            builder.AppendLine("  sd.name AS strategy_name,");
            builder.AppendLine("  sd.def_type,");
            builder.AppendLine("  (");
            builder.AppendLine("    SELECT sv2.version_id");
            builder.AppendLine("    FROM strategy_version sv2");
            builder.AppendLine("    WHERE sv2.def_id = us.def_id");
            builder.AppendLine("      AND sv2.created_at <= p.opened_at");
            builder.AppendLine("    ORDER BY sv2.created_at DESC");
            builder.AppendLine("    LIMIT 1");
            builder.AppendLine("  ) AS inferred_version_id");
            if (onlyOpen)
            {
                builder.AppendLine("FROM strategy_position p FORCE INDEX (idx_uid_status_opened)");
            }
            else
            {
                builder.AppendLine("FROM strategy_position p");
            }
            builder.AppendLine("JOIN user_strategy us ON us.us_id = p.us_id AND us.uid = p.uid");
            builder.AppendLine("JOIN strategy_def sd ON sd.def_id = us.def_id");
            builder.AppendLine("WHERE p.uid = @uid");
            if (onlyOpen)
            {
                builder.AppendLine("  AND p.status = 'Open'");
            }

            if (includeRange)
            {
                builder.AppendLine("  AND (@from IS NULL OR p.opened_at >= @from)");
                builder.AppendLine("  AND (@to IS NULL OR p.opened_at <= @to)");
            }

            builder.AppendLine("),");
            builder.AppendLine("decorated AS (");
            builder.AppendLine("SELECT");
            builder.AppendLine("  base.*,");
            builder.AppendLine("  COALESCE(base.strategy_version_id, base.inferred_version_id, base.pinned_version_id) AS effective_version_id,");
            builder.AppendLine("  CASE");
            builder.AppendLine("    WHEN base.strategy_version_id IS NOT NULL THEN 'snapshot'");
            builder.AppendLine("    WHEN base.inferred_version_id IS NOT NULL THEN 'inferred'");
            builder.AppendLine("    ELSE 'pinned'");
            builder.AppendLine("  END AS version_source");
            builder.AppendLine("FROM base");
            builder.AppendLine(")");
            return builder.ToString();
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

    public sealed class PositionDetailRecord
    {
        public long PositionId { get; set; }
        public long Uid { get; set; }
        public long UsId { get; set; }
        public long? StrategyVersionId { get; set; }
        public long EffectiveVersionId { get; set; }
        public string VersionSource { get; set; } = "pinned";
        public int? EffectiveVersionNo { get; set; }
        public DateTime? EffectiveVersionCreatedAt { get; set; }
        public string? EffectiveVersionChangelog { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public decimal Qty { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal? StopLossPrice { get; set; }
        public decimal? TakeProfitPrice { get; set; }
        public bool TrailingEnabled { get; set; }
        public bool TrailingTriggered { get; set; }
        public decimal? TrailingStopPrice { get; set; }
        public string? CloseReason { get; set; }
        public decimal? ClosePrice { get; set; }
        public decimal? RealizedPnl { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public string StrategyState { get; set; } = string.Empty;
        public long DefId { get; set; }
        public string DefType { get; set; } = string.Empty;
    }

    public sealed class PositionStrategyOpenStatRecord
    {
        public long UsId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public long DefId { get; set; }
        public string StrategyName { get; set; } = string.Empty;
        public int OpenSuccessCount { get; set; }
        public int CurrentOpenCount { get; set; }
        public int ClosedCount { get; set; }
        public DateTime FirstOpenedAt { get; set; }
        public DateTime LastOpenedAt { get; set; }
    }

    public sealed class PositionVersionParticipationRecord
    {
        public long EffectiveVersionId { get; set; }
        public int? EffectiveVersionNo { get; set; }
        public DateTime? EffectiveVersionCreatedAt { get; set; }
        public string? EffectiveVersionChangelog { get; set; }
        public int OpenSuccessCount { get; set; }
        public int StrategyCount { get; set; }
        public string? StrategyUsIdsCsv { get; set; }
        public string? StrategyAliasNames { get; set; }
        public int SnapshotCount { get; set; }
        public int InferredCount { get; set; }
        public int PinnedCount { get; set; }
    }

    public sealed class PositionWindowSummaryRecord
    {
        public int OpenCount { get; set; }
        public int ClosedCount { get; set; }
        public int WinCount { get; set; }
        public decimal RealizedPnl { get; set; }
    }

    public sealed class PositionOpenLiteRecord
    {
        public long PositionId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public decimal Qty { get; set; }
        public DateTime OpenedAt { get; set; }
    }

    public sealed class PositionRecentEventRecord
    {
        public string EventType { get; set; } = string.Empty;
        public DateTime EventAt { get; set; }
        public long PositionId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal Qty { get; set; }
        public decimal? EventPrice { get; set; }
        public decimal? RealizedPnl { get; set; }
        public string? CloseReason { get; set; }
    }
}
