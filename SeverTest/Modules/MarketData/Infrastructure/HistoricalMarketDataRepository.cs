using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ServerTest.Infrastructure.Db;

namespace ServerTest.Modules.MarketData.Infrastructure
{
    public sealed class HistoricalMarketDataInsertRow
    {
        public long OpenTime { get; set; }
        public decimal? Open { get; set; }
        public decimal? High { get; set; }
        public decimal? Low { get; set; }
        public decimal? Close { get; set; }
        public decimal? Volume { get; set; }
        public long CloseTime { get; set; }
    }

    public sealed class HistoricalMarketDataFullInsertRow
    {
        public long OpenTime { get; set; }
        public decimal? Open { get; set; }
        public decimal? High { get; set; }
        public decimal? Low { get; set; }
        public decimal? Close { get; set; }
        public decimal? Volume { get; set; }
        public long? CloseTime { get; set; }
        public decimal? QuoteVolume { get; set; }
        public int? Count { get; set; }
        public decimal? TakerBuyVolume { get; set; }
        public decimal? TakerBuyQuoteVolume { get; set; }
        public decimal? IgnoreCol { get; set; }
    }

    public sealed class HistoricalMarketDataKlineRow
    {
        public long OpenTime { get; set; }
        public decimal? Open { get; set; }
        public decimal? High { get; set; }
        public decimal? Low { get; set; }
        public decimal? Close { get; set; }
        public decimal? Volume { get; set; }
    }

    public sealed class HistoricalMarketDataRepository
    {
// 只允许字母、数字和下划线，避免表名注入风险
        private static readonly Regex TableNamePattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);
        private readonly IDbManager _db;

        public HistoricalMarketDataRepository(IDbManager db)
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

        public Task<long?> GetLastOpenTimeAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var sql = $"SELECT MAX(open_time) FROM `{safeTableName}`;";
            return _db.ExecuteScalarAsync<long?>(sql, null, null, ct);
        }

        public Task InsertRowsAsync(
            string tableName,
            IReadOnlyList<HistoricalMarketDataInsertRow> rows,
            CancellationToken ct = default)
        {
            if (rows == null || rows.Count == 0)
            {
                return Task.CompletedTask;
            }

            var safeTableName = EnsureSafeTableName(tableName);
            const string sqlTemplate = @"INSERT IGNORE INTO `{0}` 
(`open_time`, `open`, `high`, `low`, `close`, `volume`, `close_time`)
VALUES (@OpenTime, @Open, @High, @Low, @Close, @Volume, @CloseTime);";

            var sql = string.Format(sqlTemplate, safeTableName);
            return _db.ExecuteAsync(sql, rows, null, ct);
        }

        public Task EnsureKlineTableAsync(string tableName, CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var sql = $@"CREATE TABLE IF NOT EXISTS `{safeTableName}` (
  `open_time` bigint NOT NULL,
  `open` decimal(20, 8) NULL DEFAULT NULL,
  `high` decimal(20, 8) NULL DEFAULT NULL,
  `low` decimal(20, 8) NULL DEFAULT NULL,
  `close` decimal(20, 8) NULL DEFAULT NULL,
  `volume` decimal(20, 8) NULL DEFAULT NULL,
  `close_time` bigint NULL DEFAULT NULL,
  `quote_volume` decimal(20, 8) NULL DEFAULT NULL,
  `count` int NULL DEFAULT NULL,
  `taker_buy_volume` decimal(20, 8) NULL DEFAULT NULL,
  `taker_buy_quote_volume` decimal(20, 8) NULL DEFAULT NULL,
  `ignore_col` decimal(20, 8) NULL DEFAULT NULL,
  PRIMARY KEY (`open_time`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci ROW_FORMAT = DYNAMIC;";

            return _db.ExecuteAsync(sql, null, null, ct);
        }

        public Task<int> InsertFullRowsAsync(
            string tableName,
            IReadOnlyList<HistoricalMarketDataFullInsertRow> rows,
            IUnitOfWork uow,
            CancellationToken ct = default)
        {
            if (rows == null || rows.Count == 0)
            {
                return Task.FromResult(0);
            }

            var safeTableName = EnsureSafeTableName(tableName);
            const string sqlTemplate = @"INSERT IGNORE INTO `{0}`
(`open_time`, `open`, `high`, `low`, `close`, `volume`, `close_time`, `quote_volume`, `count`, `taker_buy_volume`, `taker_buy_quote_volume`, `ignore_col`)
VALUES (@OpenTime, @Open, @High, @Low, @Close, @Volume, @CloseTime, @QuoteVolume, @Count, @TakerBuyVolume, @TakerBuyQuoteVolume, @IgnoreCol);";

            var sql = string.Format(sqlTemplate, safeTableName);
            return _db.ExecuteAsync(sql, rows, uow, ct);
        }

        public async Task<IReadOnlyList<HistoricalMarketDataKlineRow>> QueryRangeAsync(
            string tableName,
            long? startMs,
            long? endMs,
            int count,
            CancellationToken ct = default)
        {
            var safeTableName = EnsureSafeTableName(tableName);
            var safeCount = Math.Max(1, count);

            string sql;
            object param;

            if (startMs.HasValue && endMs.HasValue)
            {
                sql = $@"SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
FROM `{safeTableName}`
WHERE open_time BETWEEN @Start AND @End
ORDER BY open_time ASC
LIMIT @Count;";
                param = new { Start = startMs.Value, End = endMs.Value, Count = safeCount };
            }
            else if (startMs.HasValue)
            {
                sql = $@"SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
FROM `{safeTableName}`
WHERE open_time >= @Start
ORDER BY open_time ASC
LIMIT @Count;";
                param = new { Start = startMs.Value, Count = safeCount };
            }
            else
            {
                sql = $@"SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
FROM `{safeTableName}`
{(endMs.HasValue ? "WHERE open_time <= @End" : string.Empty)}
ORDER BY open_time DESC
LIMIT @Count;";
                param = endMs.HasValue ? new { End = endMs.Value, Count = safeCount } : new { Count = safeCount };
            }

            var rows = await _db.QueryAsync<HistoricalMarketDataKlineRow>(sql, param, null, ct).ConfigureAwait(false);
            return rows.ToList();
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
