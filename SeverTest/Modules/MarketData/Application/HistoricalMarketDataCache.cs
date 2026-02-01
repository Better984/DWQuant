using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Models;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ServerTest.Services;

namespace ServerTest.Modules.MarketData.Application
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

        private static readonly Regex SafeIdentifier = new("^[a-z0-9_]+$", RegexOptions.Compiled);
        private readonly ConcurrentDictionary<string, CachedRange> _cache = new();
        private readonly HistoricalMarketDataRepository _repository;
        private readonly HistoricalMarketDataOptions _options;

        public HistoricalMarketDataCache(
            ILogger<HistoricalMarketDataCache> logger,
            HistoricalMarketDataRepository repository,
            IOptions<HistoricalMarketDataOptions> options) : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
            // 只预热币安
            List<MarketDataConfig.ExchangeEnum> exchangesToPreload = new()
            {
                MarketDataConfig.ExchangeEnum.Binance
            };

            var preloadLimit = ResolvePreloadLimit();
            var startMs = new DateTimeOffset(startDate).ToUnixTimeMilliseconds();

            // 汇总统计
            int combosTotal = 0, combosOk = 0, combosNoData = 0, combosFail = 0;
            long totalRows = 0;

            // 可选：记录全局最早/最晚时间范围（跨所有组合）
            long? globalMinTs = null;
            long? globalMaxTs = null;

            // 可选：记录失败明细（避免太长，限制条数）
            const int maxFailDetails = 10;
            var failDetails = new List<string>(maxFailDetails);

            foreach (var exchangeEnum in exchangesToPreload)
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

                        combosTotal++;

                        try
                        {
                            var rows = await QueryRangeAsync(tableName, startMs, null, preloadLimit, ct);
                            UpdateCache(cacheKey, rows);

                            if (rows.Count == 0)
                            {
                                combosNoData++;
                                continue;
                            }

                            combosOk++;
                            totalRows += rows.Count;

                            var firstTs = (long)(rows[0].timestamp ?? 0);
                            var lastTs = (long)(rows[^1].timestamp ?? 0);

                            globalMinTs = globalMinTs is null ? firstTs : Math.Min(globalMinTs.Value, firstTs);
                            globalMaxTs = globalMaxTs is null ? lastTs : Math.Max(globalMaxTs.Value, lastTs);
                        }
                        catch (Exception ex)
                        {
                            combosFail++;
                            if (failDetails.Count < maxFailDetails)
                                failDetails.Add($"{exchangeId}/{symbolStr}/{timeframeStr}({tableName}): {ex.GetType().Name} {ex.Message}");
                            // 不在循环内打 log
                        }
                    }
                }
            }

            string rangeText;
            if (globalMinTs is null || globalMaxTs is null)
            {
                rangeText = "无";
            }
            else
            {
                var startText = DateTimeOffset.FromUnixTimeMilliseconds(globalMinTs.Value)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                var endText = DateTimeOffset.FromUnixTimeMilliseconds(globalMaxTs.Value)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                rangeText = $"{startText}~{endText}";
            }

            var limitText = preloadLimit == int.MaxValue ? "不限" : preloadLimit.ToString();

            // 只输出这一条
            Logger.LogInformation(
                "历史行情预热完成：起始={StartDate} 上限={MaxBars} 组合={Total}(成功={Ok},无数据={NoData},失败={Fail}) 总条数={Rows} 全局范围={Range} 失败示例={FailExamples}",
                startDate.ToString("yyyy-MM-dd"),
                limitText,
                combosTotal,
                combosOk,
                combosNoData,
                combosFail,
                totalRows,
                rangeText,
                failDetails.Count == 0 ? "无" : string.Join(" | ", failDetails));
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
            var rows = await _repository.QueryRangeAsync(tableName, startMs, endMs, count, ct).ConfigureAwait(false);
            var list = rows.Select(ToOhlcv).ToList();

            // 反向查询时需要恢复成时间正序
            if (!startMs.HasValue)
            {
// 反向查询时需要恢复成时间正序
                list.Reverse();
            }

            return list;
        }

        private static OHLCV ToOhlcv(HistoricalMarketDataKlineRow row)
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
