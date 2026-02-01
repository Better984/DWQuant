using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.MarketData.Domain;

namespace ServerTest.Modules.MarketData.Infrastructure
{
    /// <summary>
    /// 历史行情维护与校验相关的数据访问封装。
    /// </summary>
    public sealed class MarketDataMaintenanceRepository
    {
        private static readonly Regex TableNamePattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);
        private readonly IDbManager _db;

        public MarketDataMaintenanceRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<bool> TableExistsAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            const string sql = @"SELECT 1 FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = @Table
LIMIT 1;";

            var exists = await _db.QuerySingleOrDefaultAsync<int?>(sql, new { Table = safeTableName }, null, ct)
                .ConfigureAwait(false);
            return exists.HasValue;
        }

        public async Task<int> SortByOpenTimeAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var tempTableName = $"{safeTableName}_temp_{DateTime.UtcNow:yyyyMMddHHmmss}";

            var createTempSql = $@"CREATE TABLE `{tempTableName}` LIKE `{safeTableName}`;";
            await _db.ExecuteAsync(createTempSql, null, null, ct).ConfigureAwait(false);

            var insertSortedSql = $@"INSERT INTO `{tempTableName}` 
SELECT * FROM `{safeTableName}` ORDER BY open_time ASC;";
            var rowsAffected = await _db.ExecuteAsync(insertSortedSql, null, null, ct).ConfigureAwait(false);

            var dropOriginalSql = $@"DROP TABLE `{safeTableName}`;";
            await _db.ExecuteAsync(dropOriginalSql, null, null, ct).ConfigureAwait(false);

            var renameSql = $@"RENAME TABLE `{tempTableName}` TO `{safeTableName}`;";
            await _db.ExecuteAsync(renameSql, null, null, ct).ConfigureAwait(false);

            return rowsAffected;
        }

        public async Task<IReadOnlyList<MarketDataGap>> QueryGapsAsync(
            string tableName,
            long expectedGapMs,
            int limit,
            CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var safeLimit = Math.Clamp(limit, 1, 200);

            var sql = $@"SELECT 
    FROM_UNIXTIME(open_time/1000) as GapStartTime,
    FROM_UNIXTIME(next_time/1000) as GapEndTime,
    (next_time - open_time) / 60000 as GapMinutes
FROM (
    SELECT 
        open_time,
        LEAD(open_time) OVER (ORDER BY open_time) as next_time
    FROM `{safeTableName}`
) t
WHERE next_time IS NOT NULL 
  AND (next_time - open_time) > @ExpectedGap
ORDER BY GapMinutes DESC
LIMIT @Limit;";

            var rows = await _db.QueryAsync<MarketDataGap>(sql, new { ExpectedGap = expectedGapMs, Limit = safeLimit }, null, ct)
                .ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<long> GetDuplicateCountAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var sql = $@"SELECT COUNT(*) - COUNT(DISTINCT open_time) as duplicate_count
FROM `{safeTableName}`;";

            return await _db.QuerySingleOrDefaultAsync<long>(sql, null, null, ct).ConfigureAwait(false);
        }

        public Task<long> GetRowCountAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var sql = $@"SELECT COUNT(*) as count FROM `{safeTableName}`;";
            return _db.QuerySingleOrDefaultAsync<long>(sql, null, null, ct);
        }

        public Task<OpenTimeRange?> GetOpenTimeRangeAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var sql = $@"SELECT MIN(open_time) AS Min, MAX(open_time) AS Max FROM `{safeTableName}`;";
            return _db.QuerySingleOrDefaultAsync<OpenTimeRange>(sql, null, null, ct);
        }

        public Task CreateTableLikeAsync(string targetTableName, string sourceTableName, CancellationToken ct = default)
        {
            var safeTarget = EnsureSafeTableName(targetTableName);
            var safeSource = EnsureSafeTableName(sourceTableName);
            var sql = $@"CREATE TABLE IF NOT EXISTS `{safeTarget}` LIKE `{safeSource}`;";
            return _db.ExecuteAsync(sql, null, null, ct);
        }

        public Task<int> InsertAggregateBatchAsync(
            string sourceTableName,
            string targetTableName,
            long timeframeMs,
            long start,
            long end,
            CancellationToken ct = default)
        {
            var safeSource = EnsureSafeTableName(sourceTableName);
            var safeTarget = EnsureSafeTableName(targetTableName);
            var sql = $@"INSERT IGNORE INTO `{safeTarget}`
(open_time, open, high, low, close, volume, close_time, quote_volume, count, taker_buy_volume, taker_buy_quote_volume, ignore_col)
SELECT
    agg.bucket_time as open_time,
    first_row.open as open,
    agg.max_high as high,
    agg.min_low as low,
    last_row.close as close,
    agg.sum_volume as volume,
    agg.max_close_time as close_time,
    agg.sum_quote_volume as quote_volume,
    agg.sum_count as count,
    agg.sum_taker_buy_volume as taker_buy_volume,
    agg.sum_taker_buy_quote_volume as taker_buy_quote_volume,
    agg.max_ignore_col as ignore_col
FROM (
    SELECT
        FLOOR(open_time / @TfMs) * @TfMs as bucket_time,
        MAX(high) as max_high,
        MIN(low) as min_low,
        SUM(volume) as sum_volume,
        MAX(close_time) as max_close_time,
        SUM(quote_volume) as sum_quote_volume,
        SUM(count) as sum_count,
        SUM(taker_buy_volume) as sum_taker_buy_volume,
        SUM(taker_buy_quote_volume) as sum_taker_buy_quote_volume,
        MAX(ignore_col) as max_ignore_col,
        MIN(open_time) as min_open_time,
        MAX(open_time) as max_open_time
    FROM `{safeSource}`
    WHERE open_time >= @Start AND open_time < @End
    GROUP BY bucket_time
) agg
INNER JOIN `{safeSource}` first_row ON first_row.open_time = agg.min_open_time AND FLOOR(first_row.open_time / @TfMs) * @TfMs = agg.bucket_time
INNER JOIN `{safeSource}` last_row ON last_row.open_time = agg.max_open_time AND FLOOR(last_row.open_time / @TfMs) * @TfMs = agg.bucket_time
;";

            return _db.ExecuteAsync(sql, new { TfMs = timeframeMs, Start = start, End = end }, null, ct);
        }

        public Task<long> CountRowsInRangeAsync(string tableName, long startMs, long endMs, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var sql = $@"SELECT COUNT(*) as count
FROM `{safeTableName}`
WHERE open_time >= @GapStartMs AND open_time < @GapEndMs;";

            return _db.QuerySingleOrDefaultAsync<long>(sql, new { GapStartMs = startMs, GapEndMs = endMs }, null, ct);
        }

        private static string EnsureSafeTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName) || !TableNamePattern.IsMatch(tableName))
            {
                throw new ArgumentException("表名不合法。", nameof(tableName));
            }

            return tableName;
        }
    }
}
