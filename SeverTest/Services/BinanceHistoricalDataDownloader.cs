using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Models;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace ServerTest.Services
{
    public enum DownloadLogLevel
    {
        Info,
        Warning,
        Error
    }

    public sealed class DownloadLogEntry
    {
        public DownloadLogLevel Level { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.Now;
    }

    public sealed class DownloadSummary
    {
        public int DaysProcessed { get; set; }
        public int DaysDownloaded { get; set; }
        public int RowsInserted { get; set; }
        public int TablesCreated { get; set; }
    }

    public sealed class BinanceHistoricalDataDownloader : BaseService
    {
        private sealed class KlineInsertRow
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

        private const int DefaultBatchSize = 2000;
        private static readonly Regex SafeIdentifier = new("^[a-z0-9_]+$", RegexOptions.Compiled);
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private readonly IDbManager _dbManager;

        public BinanceHistoricalDataDownloader(ILogger<BinanceHistoricalDataDownloader> logger, IDbManager dbManager)
            : base(logger)
        {
            _dbManager = dbManager;
        }

        public async Task<DownloadSummary> DownloadAsync(
            MarketDataConfig.SymbolEnum symbolEnum,
            MarketDataConfig.TimeframeEnum timeframeEnum,
            DateTime startDate,
            Action<DownloadLogEntry>? log,
            CancellationToken ct,
            bool forceDailyOnly = false,
            DateTime? endDate = null)
        {
            var summary = new DownloadSummary();
            var exchangeId = MarketDataConfig.ExchangeToString(MarketDataConfig.ExchangeEnum.Binance);
            var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);
            var timeframeStr = MarketDataConfig.TimeframeToString(timeframeEnum);
            var tableName = BuildTableName(exchangeId, symbolStr, timeframeStr);

            await EnsureTableAsync(tableName, ct).ConfigureAwait(false);
            summary.TablesCreated = 1;

            var symbolNoSlash = symbolStr.Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            var start = startDate.Date;
            var end = endDate?.Date ?? DateTime.UtcNow.Date;
            var firstDataFound = false;

            var currentMonthStart = new DateTime(end.Year, end.Month, 1);
            var monthCursor = new DateTime(start.Year, start.Month, 1);

            // 如果强制日线下载，跳过月下载
            if (!forceDailyOnly)
            {
                while (monthCursor < currentMonthStart)
                {
                    ct.ThrowIfCancellationRequested();

                    var monthDays = DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month);
                    var monthStart = monthCursor;
                    var monthEnd = monthCursor.AddDays(monthDays - 1);
                    var rangeStart = monthStart < start ? start : monthStart;
                    var rangeEnd = monthEnd;

                    if (rangeStart <= rangeEnd)
                    {
                        summary.DaysProcessed += (rangeEnd - rangeStart).Days + 1;
                    }

                    var monthText = monthCursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    var monthUrl = $"https://data.binance.vision/data/futures/um/monthly/klines/{symbolNoSlash}/{timeframeStr}/{symbolNoSlash}-{timeframeStr}-{monthText}.zip";

                    var monthResult = await TryDownloadMonthAsync(monthUrl, monthCursor, tableName, log, ct).ConfigureAwait(false);
                    if (monthResult.Downloaded)
                    {
                        firstDataFound = true;
                        if (rangeStart <= rangeEnd)
                        {
                            summary.DaysDownloaded += (rangeEnd - rangeStart).Days + 1;
                        }
                        summary.RowsInserted += monthResult.RowsInserted;
                    }
                    else if (!monthResult.NotFound)
                    {
                        log?.Invoke(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"Month download failed: {symbolNoSlash} {timeframeStr} {monthText}"
                        });
                    }
                    else if (firstDataFound)
                    {
                        log?.Invoke(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"Missing month after start: {symbolNoSlash} {timeframeStr} {monthText}"
                        });
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    monthCursor = monthCursor.AddMonths(1);
                }
            }

            var dailyStart = forceDailyOnly ? start : (start > currentMonthStart ? start : currentMonthStart);
            for (var day = dailyStart; day <= end; day = day.AddDays(1))
            {
                ct.ThrowIfCancellationRequested();
                summary.DaysProcessed++;

                var dateText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var url = $"https://data.binance.vision/data/futures/um/daily/klines/{symbolNoSlash}/{timeframeStr}/{symbolNoSlash}-{timeframeStr}-{dateText}.zip";

                var result = await TryDownloadDayAsync(url, day, tableName, log, ct).ConfigureAwait(false);
                if (result.Downloaded)
                {
                    firstDataFound = true;
                    summary.DaysDownloaded++;
                    summary.RowsInserted += result.RowsInserted;
                }
                else if (!result.NotFound)
                {
                    log?.Invoke(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"Download failed: {symbolNoSlash} {timeframeStr} {dateText}"
                    });
                }
                else if (firstDataFound)
                {
                    log?.Invoke(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"Missing day after start: {symbolNoSlash} {timeframeStr} {dateText}"
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }

            return summary;
        }

        private async Task EnsureTableAsync(string tableName, CancellationToken ct)
        {
            if (!SafeIdentifier.IsMatch(tableName))
            {
                throw new InvalidOperationException($"Invalid table name: {tableName}");
            }

            var sql = $@"CREATE TABLE IF NOT EXISTS `{tableName}` (
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

            await _dbManager.ExecuteAsync(sql, null, null, ct).ConfigureAwait(false);
        }

        private static string BuildTableName(string exchangeId, string symbolStr, string timeframeStr)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbolStr);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframeStr);
            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!SafeIdentifier.IsMatch(exchangeKey) || !SafeIdentifier.IsMatch(symbolPart) || !SafeIdentifier.IsMatch(timeframeKey))
            {
                throw new InvalidOperationException("Invalid market data identifier.");
            }

            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }

        private sealed class DownloadDayResult
        {
            public bool Downloaded { get; set; }
            public bool NotFound { get; set; }
            public int RowsInserted { get; set; }
        }

        private async Task<DownloadDayResult> TryDownloadDayAsync(
            string url,
            DateTime day,
            string tableName,
            Action<DownloadLogEntry>? log,
            CancellationToken ct)
        {
            log?.Invoke(new DownloadLogEntry
            {
                Level = DownloadLogLevel.Info,
                Message = $"Downloading {url}"
            });
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new DownloadDayResult { NotFound = true };
            }

            if (!response.IsSuccessStatusCode)
            {
                log?.Invoke(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = $"HTTP {(int)response.StatusCode} for {url}"
                });
                return new DownloadDayResult();
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "dwquant_klines");
            Directory.CreateDirectory(tempDir);

            var dateText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var zipPath = Path.Combine(tempDir, $"binance_{tableName}_{dateText}.zip");
            var csvPath = string.Empty;

            try
            {
                await using (var fs = File.Create(zipPath))
                {
                    await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries.FirstOrDefault();
                    if (entry == null)
                    {
                        log?.Invoke(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"Zip is empty: {zipPath}"
                        });
                        return new DownloadDayResult();
                    }

                    csvPath = Path.Combine(tempDir, entry.Name);
                    entry.ExtractToFile(csvPath, true);
                }

                var rowsInserted = await LoadCsvAsync(tableName, csvPath, log, ct).ConfigureAwait(false);
                log?.Invoke(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"Imported {rowsInserted} rows for {day:yyyy-MM-dd}"
                });

                return new DownloadDayResult
                {
                    Downloaded = true,
                    RowsInserted = rowsInserted
                };
            }
            finally
            {
                TryDeleteFile(csvPath);
                TryDeleteFile(zipPath);
            }
        }

        private async Task<DownloadDayResult> TryDownloadMonthAsync(
            string url,
            DateTime monthStart,
            string tableName,
            Action<DownloadLogEntry>? log,
            CancellationToken ct)
        {
            log?.Invoke(new DownloadLogEntry
            {
                Level = DownloadLogLevel.Info,
                Message = $"Downloading {url}"
            });
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new DownloadDayResult { NotFound = true };
            }

            if (!response.IsSuccessStatusCode)
            {
                log?.Invoke(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = $"HTTP {(int)response.StatusCode} for {url}"
                });
                return new DownloadDayResult();
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "dwquant_klines");
            Directory.CreateDirectory(tempDir);

            var monthText = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var zipPath = Path.Combine(tempDir, $"binance_{tableName}_{monthText}.zip");
            var csvPath = string.Empty;

            try
            {
                await using (var fs = File.Create(zipPath))
                {
                    await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries.FirstOrDefault();
                    if (entry == null)
                    {
                        log?.Invoke(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"Zip is empty: {zipPath}"
                        });
                        return new DownloadDayResult();
                    }

                    csvPath = Path.Combine(tempDir, entry.Name);
                    entry.ExtractToFile(csvPath, true);
                }

                var rowsInserted = await LoadCsvAsync(tableName, csvPath, log, ct).ConfigureAwait(false);
                log?.Invoke(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"Imported {rowsInserted} rows for {monthText}"
                });

                return new DownloadDayResult
                {
                    Downloaded = true,
                    RowsInserted = rowsInserted
                };
            }
            finally
            {
                TryDeleteFile(csvPath);
                TryDeleteFile(zipPath);
            }
        }

        private async Task<int> LoadCsvAsync(
            string tableName,
            string csvPath,
            Action<DownloadLogEntry>? log,
            CancellationToken ct)
        {
            if (!File.Exists(csvPath))
            {
                return 0;
            }

            var totalInserted = 0;
            var batch = new List<KlineInsertRow>(DefaultBatchSize);

            await using var uow = await _dbManager.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                using var stream = File.OpenRead(csvPath);
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (TryParseLine(line, out var row))
                    {
                        batch.Add(row);
                        if (batch.Count >= DefaultBatchSize)
                        {
                            totalInserted += await InsertBatchAsync(tableName, batch, uow, ct).ConfigureAwait(false);
                            batch.Clear();
                        }
                    }
                    else
                    {
                        log?.Invoke(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"Skip invalid line: {line}"
                        });
                    }
                }

                if (batch.Count > 0)
                {
                    totalInserted += await InsertBatchAsync(tableName, batch, uow, ct).ConfigureAwait(false);
                }

                await uow.CommitAsync(ct).ConfigureAwait(false);
                return totalInserted;
            }
            catch
            {
                await uow.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task<int> InsertBatchAsync(
            string tableName,
            List<KlineInsertRow> rows,
            IUnitOfWork uow,
            CancellationToken ct)
        {
            const string sqlTemplate = @"INSERT IGNORE INTO `{0}`
(`open_time`, `open`, `high`, `low`, `close`, `volume`, `close_time`, `quote_volume`, `count`, `taker_buy_volume`, `taker_buy_quote_volume`, `ignore_col`)
VALUES (@OpenTime, @Open, @High, @Low, @Close, @Volume, @CloseTime, @QuoteVolume, @Count, @TakerBuyVolume, @TakerBuyQuoteVolume, @IgnoreCol);";

            var sql = string.Format(sqlTemplate, tableName);
            return await _dbManager.ExecuteAsync(sql, rows, uow, ct).ConfigureAwait(false);
        }

        private static bool TryParseLine(string line, out KlineInsertRow row)
        {
            row = new KlineInsertRow();
            var parts = line.Split(',', StringSplitOptions.None);
            if (parts.Length < 6)
            {
                return false;
            }

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var openTime))
            {
                return false;
            }

            row.OpenTime = openTime;
            row.Open = TryParseDecimal(parts, 1);
            row.High = TryParseDecimal(parts, 2);
            row.Low = TryParseDecimal(parts, 3);
            row.Close = TryParseDecimal(parts, 4);
            row.Volume = TryParseDecimal(parts, 5);
            row.CloseTime = TryParseLong(parts, 6);
            row.QuoteVolume = TryParseDecimal(parts, 7);
            row.Count = TryParseInt(parts, 8);
            row.TakerBuyVolume = TryParseDecimal(parts, 9);
            row.TakerBuyQuoteVolume = TryParseDecimal(parts, 10);
            row.IgnoreCol = TryParseDecimal(parts, 11);
            return true;
        }

        private static decimal? TryParseDecimal(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return null;
            }

            return decimal.TryParse(parts[index], NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static long? TryParseLong(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return null;
            }

            return long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static int? TryParseInt(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return null;
            }

            return int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
