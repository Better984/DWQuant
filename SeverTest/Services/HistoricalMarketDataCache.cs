using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Db;
using ServerTest.Models;
using ServerTest.Options;

namespace ServerTest.Services
{
    public sealed class HistoricalCacheSnapshot
    {
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Timeframe { get; init; } = string.Empty;
        public DateTime StartTime { get; init; }
        public DateTime EndTime { get; init; }
        public int Count { get; init; }
    }

    public class HistoricalMarketDataCache : BaseService
    {
        private sealed class CachedRange
        {
            public readonly object Lock = new();
            public List<OHLCV> Items = new();
            public long StartMs;
            public long EndMs;
        }

        private sealed class KlineRow
        {
            public long OpenTime { get; set; }
            public decimal? Open { get; set; }
            public decimal? High { get; set; }
            public decimal? Low { get; set; }
            public decimal? Close { get; set; }
            public decimal? Volume { get; set; }
        }

        private static readonly Regex SafeIdentifier = new("^[a-z0-9_]+$", RegexOptions.Compiled);
        private readonly ConcurrentDictionary<string, CachedRange> _cache = new();
        private readonly IDbManager _dbManager;
        private readonly HistoricalMarketDataOptions _options;

        public HistoricalMarketDataCache(
            ILogger<HistoricalMarketDataCache> logger,
            IDbManager dbManager,
            IOptions<HistoricalMarketDataOptions> options) : base(logger)
        {
            _dbManager = dbManager;
            _options = options?.Value ?? new HistoricalMarketDataOptions();
        }

        public void InvalidateCache(
            MarketDataConfig.ExchangeEnum exchange,
            MarketDataConfig.TimeframeEnum timeframe,
            MarketDataConfig.SymbolEnum symbol)
        {
            var exchangeId = MarketDataConfig.ExchangeToString(exchange);
            var symbolStr = MarketDataConfig.SymbolToString(symbol);
            var timeframeStr = MarketDataConfig.TimeframeToString(timeframe);
            var key = BuildCacheKey(exchangeId, symbolStr, timeframeStr);
            _cache.TryRemove(key, out _);
        }

        public IReadOnlyList<HistoricalCacheSnapshot> GetCacheSnapshots()
        {
            var snapshots = new List<HistoricalCacheSnapshot>();

            foreach (var entry in _cache)
            {
                var parts = entry.Key.Split(':');
                if (parts.Length != 3)
                {
                    continue;
                }

                var exchange = parts[0];
                var symbol = parts[1];
                var timeframe = parts[2];
                var cache = entry.Value;

                lock (cache.Lock)
                {
                    if (cache.Items.Count == 0)
                    {
                        continue;
                    }

                    var start = DateTimeOffset.FromUnixTimeMilliseconds(cache.StartMs).LocalDateTime;
                    var end = DateTimeOffset.FromUnixTimeMilliseconds(cache.EndMs).LocalDateTime;

                    snapshots.Add(new HistoricalCacheSnapshot
                    {
                        Exchange = exchange,
                        Symbol = symbol,
                        Timeframe = timeframe,
                        StartTime = start,
                        EndTime = end,
                        Count = cache.Items.Count
                    });
                }
            }

            return snapshots
                .OrderBy(s => s.Exchange)
                .ThenBy(s => s.Symbol)
                .ThenBy(s => s.Timeframe)
                .ToList();
        }

        public async Task WarmUpCacheAsync(DateTime startDate, CancellationToken ct = default)
        {
            // 预热缓存：按交易所/币对/周期读取数据库并写入内存
            var preloadLimit = ResolvePreloadLimit();
            foreach (var exchangeEnum in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                var exchangeId = MarketDataConfig.ExchangeToString(exchangeEnum);
                foreach (var symbolEnum in Enum.GetValues<MarketDataConfig.SymbolEnum>())
                {
                    var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);
                    foreach (var timeframeEnum in Enum.GetValues<MarketDataConfig.TimeframeEnum>())
                    {
                        var timeframeStr = MarketDataConfig.TimeframeToString(timeframeEnum);
                        var tableName = BuildTableName(exchangeId, symbolStr, timeframeStr);
                        var cacheKey = BuildCacheKey(exchangeId, symbolStr, timeframeStr);

                        try
                        {
                            var startMs = new DateTimeOffset(startDate).ToUnixTimeMilliseconds();
                            // 尽可能多地缓存：读取 MaxCacheBars 条
                            Logger.LogInformation(
                                "历史行情预热读取：{Exchange} {Symbol} {Timeframe} 起始={StartDate} 上限={MaxBars}",
                                exchangeId,
                                symbolStr,
                                timeframeStr,
                                startDate.ToString("yyyy-MM-dd"),
                                preloadLimit == int.MaxValue ? "不限" : preloadLimit.ToString());
                            var rows = await QueryRangeAsync(tableName, startMs, null, preloadLimit, ct);
                            UpdateCache(cacheKey, rows);
                            if (rows.Count == 0)
                            {
                                Logger.LogInformation(
                                    "历史行情预热结果：{Exchange} {Symbol} {Timeframe} 无数据",
                                    exchangeId,
                                    symbolStr,
                                    timeframeStr);
                            }
                            else
                            {
                                var startText = DateTimeOffset.FromUnixTimeMilliseconds((long)(rows[0].timestamp ?? 0))
                                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                                var endText = DateTimeOffset.FromUnixTimeMilliseconds((long)(rows[^1].timestamp ?? 0))
                                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                                Logger.LogInformation(
                                    "历史行情预热结果：{Exchange} {Symbol} {Timeframe} 共{Count}条 范围={Start}~{End}",
                                    exchangeId,
                                    symbolStr,
                                    timeframeStr,
                                    rows.Count,
                                    startText,
                                    endText);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "历史行情预热失败: {Table}", tableName);
                        }
                    }
                }
            }
        }

        private int ResolvePreloadLimit()
        {
            if (_options.MaxCacheBars <= 0)
            {
                return int.MaxValue;
            }

            return _options.MaxCacheBars;
        }

        public async Task<IReadOnlyList<OHLCV>> GetHistoryAsync(
            MarketDataConfig.ExchangeEnum exchange,
            MarketDataConfig.TimeframeEnum timeframe,
            MarketDataConfig.SymbolEnum symbol,
            DateTime? startTime,
            DateTime? endTime,
            int? count,
            CancellationToken ct = default)
        {
            // 统一参数，生成缓存键与表名
            var exchangeId = MarketDataConfig.ExchangeToString(exchange);
            var symbolStr = MarketDataConfig.SymbolToString(symbol);
            var timeframeStr = MarketDataConfig.TimeframeToString(timeframe);

            var startMs = startTime.HasValue ? new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds() : (long?)null;
            var endMs = endTime.HasValue ? new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds() : (long?)null;

            var safeCount = NormalizeCount(count);
            var cacheKey = BuildCacheKey(exchangeId, symbolStr, timeframeStr);
            var tableName = BuildTableName(exchangeId, symbolStr, timeframeStr);

            // 时间范围非法直接返回空
            if (startMs.HasValue && endMs.HasValue && startMs > endMs)
            {
                return Array.Empty<OHLCV>();
            }

            // 命中缓存直接返回
            if (TryGetFromCache(cacheKey, startMs, endMs, safeCount, out var cached))
            {
                return cached;
            }

            try
            {
                // 未命中缓存，从数据库读取并回填缓存
                var rows = await QueryRangeAsync(tableName, startMs, endMs, safeCount, ct);
                UpdateCache(cacheKey, rows);
                Logger.LogInformation(
                    "历史行情读取完成：{Exchange} {Symbol} {Timeframe} 返回{Count}条 开始={Start} 结束={End}",
                    exchangeId,
                    symbolStr,
                    timeframeStr,
                    rows.Count,
                    startTime?.ToString("yyyy-MM-dd HH:mm") ?? "NULL",
                    endTime?.ToString("yyyy-MM-dd HH:mm") ?? "NULL"
                );
                return rows;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "历史行情查询失败: {Table}", tableName);
                return Array.Empty<OHLCV>();
            }
        }

        private int NormalizeCount(int? count)
        {
            var max = _options.MaxQueryBars <= 0 ? 2000 : _options.MaxQueryBars;
            if (!count.HasValue)
            {
                return Math.Min(100, max);
            }

            if (count.Value <= 0)
            {
                return 1;
            }

            return Math.Min(count.Value, max);
        }

        private bool TryGetFromCache(
            string cacheKey,
            long? startMs,
            long? endMs,
            int count,
            out List<OHLCV> result)
        {
            result = new List<OHLCV>();
            if (!_cache.TryGetValue(cacheKey, out var cache))
            {
                return false;
            }

            lock (cache.Lock)
            {
                // 缓存为空或范围不覆盖则需要回表
                if (cache.Items.Count == 0)
                {
                    return false;
                }

                var rangeStart = startMs ?? cache.StartMs;
                var rangeEnd = endMs ?? cache.EndMs;

                if (cache.StartMs > rangeStart || cache.EndMs < rangeEnd)
                {
                    return false;
                }

                // 二分定位范围内起止索引
                var startIndex = FindFirstIndex(cache.Items, rangeStart);
                var endIndex = FindLastIndex(cache.Items, rangeEnd);
                if (startIndex < 0 || endIndex < startIndex)
                {
                    return false;
                }

                var slice = cache.Items.GetRange(startIndex, endIndex - startIndex + 1);
                if (slice.Count > count)
                {
                    slice = slice.Skip(slice.Count - count).ToList();
                }

                result = slice;
                return true;
            }
        }

        private void UpdateCache(string cacheKey, List<OHLCV> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            if (!_cache.TryGetValue(cacheKey, out var cache))
            {
                cache = new CachedRange();
                _cache[cacheKey] = cache;
            }

            lock (cache.Lock)
            {
                // 控制缓存最大长度，保留最新部分
                var trimmed = TrimToCacheLimit(items);
                cache.Items = trimmed;
                cache.StartMs = (long)(cache.Items[0].timestamp ?? 0);
                cache.EndMs = (long)(cache.Items[^1].timestamp ?? 0);
            }
        }

        private List<OHLCV> TrimToCacheLimit(List<OHLCV> items)
        {
            var limit = _options.MaxCacheBars;
            if (limit <= 0 || items.Count <= limit)
            {
                return items;
            }

            return items.Skip(items.Count - limit).ToList();
        }

        private async Task<List<OHLCV>> QueryRangeAsync(
            string tableName,
            long? startMs,
            long? endMs,
            int count,
            CancellationToken ct)
        {
            string sql;
            object param;

            // 根据参数组合 SQL，尽量使用范围查询
            if (startMs.HasValue && endMs.HasValue)
            {
                sql = $@"SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
FROM `{tableName}`
WHERE open_time BETWEEN @Start AND @End
ORDER BY open_time ASC
LIMIT @Count;";
                param = new { Start = startMs.Value, End = endMs.Value, Count = count };
            }
            else if (startMs.HasValue)
            {
                sql = $@"SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
FROM `{tableName}`
WHERE open_time >= @Start
ORDER BY open_time ASC
LIMIT @Count;";
                param = new { Start = startMs.Value, Count = count };
            }
            else
            {
                sql = $@"SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
FROM `{tableName}`
{(endMs.HasValue ? "WHERE open_time <= @End" : string.Empty)}
ORDER BY open_time DESC
LIMIT @Count;";
                param = endMs.HasValue ? new { End = endMs.Value, Count = count } : new { Count = count };
            }

            // DB 读取后统一转换为 OHLCV
            var rows = await _dbManager.QueryAsync<KlineRow>(sql, param, null, ct);
            var list = rows.Select(ToOhlcv).ToList();

            // 反向查询时需要恢复成时间正序
            if (!startMs.HasValue)
            {
                list.Reverse();
            }

            return list;
        }

        private static OHLCV ToOhlcv(KlineRow row)
        {
            return new OHLCV
            {
                timestamp = row.OpenTime,
                open = row.Open.HasValue ? (double)row.Open.Value : null,
                high = row.High.HasValue ? (double)row.High.Value : null,
                low = row.Low.HasValue ? (double)row.Low.Value : null,
                close = row.Close.HasValue ? (double)row.Close.Value : null,
                volume = row.Volume.HasValue ? (double)row.Volume.Value : null
            };
        }

        private static int FindFirstIndex(List<OHLCV> items, long target)
        {
            var left = 0;
            var right = items.Count - 1;
            var result = -1;

            while (left <= right)
            {
                var mid = left + (right - left) / 2;
                var ts = (long)(items[mid].timestamp ?? 0);
                if (ts >= target)
                {
                    result = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }

            return result;
        }

        private static int FindLastIndex(List<OHLCV> items, long target)
        {
            var left = 0;
            var right = items.Count - 1;
            var result = -1;

            while (left <= right)
            {
                var mid = left + (right - left) / 2;
                var ts = (long)(items[mid].timestamp ?? 0);
                if (ts <= target)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return result;
        }

        private static string BuildCacheKey(string exchangeId, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            return $"{exchangeKey}:{symbolKey}:{timeframeKey}";
        }

        private static string BuildTableName(string exchangeId, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);

            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!SafeIdentifier.IsMatch(exchangeKey) || !SafeIdentifier.IsMatch(symbolPart) || !SafeIdentifier.IsMatch(timeframeKey))
            {
                throw new InvalidOperationException("Invalid market data identifier.");
            }

            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }
    }
}
