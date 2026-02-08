using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ccxt;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.MarketData.Infrastructure;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测 K 线数据加载器（从 BacktestRunner 拆分）：
    /// 三级优先级：HistoricalMarketDataCache → IMarketDataProvider → 历史行情表
    /// </summary>
    internal sealed class BacktestDataLoader
    {
        private static readonly Regex SafeIdentifier = new("^[a-z0-9_]+$", RegexOptions.Compiled);

        private readonly HistoricalMarketDataRepository _repository;
        private readonly HistoricalMarketDataCache _historicalCache;
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly int _maxQueryBars;
        private readonly ILogger _logger;

        public BacktestDataLoader(
            HistoricalMarketDataRepository repository,
            HistoricalMarketDataCache historicalCache,
            IMarketDataProvider marketDataProvider,
            int maxQueryBars,
            ILogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _historicalCache = historicalCache ?? throw new ArgumentNullException(nameof(historicalCache));
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _maxQueryBars = maxQueryBars;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private void LogInfo(string message, params object[] args)
        {
            _logger.LogInformation($"回测系统Log：{message}", args);
        }

        private void LogWarning(string message, params object[] args)
        {
            _logger.LogWarning($"回测系统Log：{message}", args);
        }

        /// <summary>
        /// 加载主周期 K 线数据（按时间范围或按数量）
        /// </summary>
        public async Task<List<OHLCV>> LoadPrimaryBarsAsync(
            string exchange,
            string symbol,
            string timeframe,
            DateTimeOffset? startTime,
            DateTimeOffset? endTime,
            int barCount,
            int warmupBars,
            CancellationToken ct)
        {
            if (startTime.HasValue && endTime.HasValue)
            {
                var startMs = startTime.Value.ToUnixTimeMilliseconds();
                var endMs = endTime.Value.ToUnixTimeMilliseconds();
                var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                var warmupStart = Math.Max(0, startMs - warmupBars * tfMs);
                return await LoadBarsByRangeAsync(exchange, symbol, timeframe, warmupStart, endMs, ct);
            }

            var count = Math.Max(1, barCount + Math.Max(0, warmupBars));
            return await LoadBarsByCountAsync(exchange, symbol, timeframe, null, count, ct);
        }

        /// <summary>
        /// 加载补充周期 K 线数据（非主周期的指标所需数据）
        /// </summary>
        public async Task LoadSupplementaryTimeframesAsync(
            string exchange,
            IReadOnlyCollection<string> timeframes,
            IReadOnlyList<string> symbols,
            long drivingStart,
            long drivingEnd,
            IReadOnlyDictionary<string, int> warmupByTimeframe,
            Dictionary<string, List<OHLCV>> series,
            CancellationToken ct)
        {
            foreach (var timeframe in timeframes)
            {
                foreach (var symbol in symbols)
                {
                    var key = BacktestMarketDataProvider.BuildKey(exchange, symbol, timeframe);
                    if (series.ContainsKey(key))
                        continue;

                    var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                    var warmupBars = warmupByTimeframe.TryGetValue(timeframe, out var warmup) ? warmup : 0;
                    var startMs = Math.Max(0, drivingStart - warmupBars * tfMs);

                    var bars = await LoadBarsByRangeAsync(exchange, symbol, timeframe, startMs, drivingEnd, ct);
                    series[key] = bars;
                }
            }
        }

        /// <summary>
        /// 按时间范围加载 K 线（优先缓存 → 读表）
        /// </summary>
        public async Task<List<OHLCV>> LoadBarsByRangeAsync(
            string exchange, string symbol, string timeframe,
            long startMs, long endMs, CancellationToken ct)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);

            if (_historicalCache.TryGetHistoryFromCache(
                    exchangeKey, timeframeKey, symbolKey,
                    startMs, endMs, null,
                    out var cachedBars, out var cacheMissReason))
            {
                LogInfo("历史行情缓存命中（区间）：交易所={Exchange} 标的={Symbol} 周期={Timeframe} K线数量={Bars}",
                    exchangeKey, symbolKey, timeframeKey, cachedBars.Count);
                return cachedBars;
            }

            LogInfo("历史行情缓存未命中（区间），回退历史行情表：交易所={Exchange} 标的={Symbol} 周期={Timeframe} 原因={Reason}",
                exchangeKey, symbolKey, timeframeKey, cacheMissReason);

            var tableName = BuildTableName(exchangeKey, symbolKey, timeframeKey);
            var pageSize = ResolvePageSize();
            var result = new List<OHLCV>(pageSize);
            var cursor = startMs;
            var pageCount = 0;
            var rowCount = 0;
            var loadSw = System.Diagnostics.Stopwatch.StartNew();

            while (cursor <= endMs)
            {
                var rows = await _repository.QueryRangeAsync(tableName, cursor, endMs, pageSize, ct)
                    .ConfigureAwait(false);
                if (rows.Count == 0) break;

                var expectedCount = result.Count + rows.Count;
                if (result.Capacity < expectedCount)
                {
                    result.Capacity = expectedCount;
                }

                foreach (var row in rows)
                {
                    result.Add(ToOhlcv(row));
                }
                pageCount++;
                rowCount += rows.Count;

                var lastTs = rows[^1].OpenTime;
                if (lastTs >= endMs || rows.Count < pageSize) break;
                cursor = lastTs + 1;
            }

            loadSw.Stop();
            LogInfo("区间查询完成：数据表={Table} 行数={Rows} 分页次数={Pages} 耗时={Elapsed}ms",
                tableName, rowCount, pageCount, loadSw.ElapsedMilliseconds);
            return result;
        }

        /// <summary>
        /// 按数量加载 K 线（优先缓存 → 历史行情系统 → 读表）
        /// </summary>
        public async Task<List<OHLCV>> LoadBarsByCountAsync(
            string exchange, string symbol, string timeframe,
            long? endMs, int count, CancellationToken ct)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var safeCount = Math.Max(1, count);
            var timeframeMs = MarketDataConfig.TimeframeToMs(timeframeKey);

            // 1. 优先从历史行情缓存截取
            if (_historicalCache.TryGetHistoryFromCache(
                    exchangeKey, timeframeKey, symbolKey,
                    null, endMs, safeCount,
                    out var cachedBars, out var cacheMissReason))
            {
                if (cachedBars.Count >= safeCount)
                {
                    LogInfo("历史行情缓存命中（数量）：交易所={Exchange} 标的={Symbol} 周期={Timeframe} 请求数量={Requested} 返回数量={Actual}",
                        exchangeKey, symbolKey, timeframeKey, safeCount, cachedBars.Count);
                    return cachedBars;
                }

                LogInfo("历史行情缓存数量不足，继续回退：交易所={Exchange} 标的={Symbol} 周期={Timeframe} 请求数量={Requested} 返回数量={Actual}",
                    exchangeKey, symbolKey, timeframeKey, safeCount, cachedBars.Count);
            }
            else
            {
                LogInfo("历史行情缓存未命中（数量）：交易所={Exchange} 标的={Symbol} 周期={Timeframe} 原因={Reason}",
                    exchangeKey, symbolKey, timeframeKey, cacheMissReason);
            }

            // 2. 尝试 IMarketDataProvider（MarketDataEngine 缓存）
            List<OHLCV>? providerBars = null;
            try
            {
                providerBars = _marketDataProvider.GetHistoryKlines(
                    exchangeKey, timeframeKey, symbolKey, endMs, safeCount);
            }
            catch (Exception ex)
            {
                LogWarning("历史行情系统读取异常，回退历史行情表：交易所={Exchange} 标的={Symbol} 错误={Error}",
                    exchangeKey, symbolKey, ex.Message);
            }

            if (providerBars != null && providerBars.Count >= safeCount)
            {
                LogInfo("历史行情系统命中：交易所={Exchange} 标的={Symbol} 周期={Timeframe} 返回数量={Bars}",
                    exchangeKey, symbolKey, timeframeKey, providerBars.Count);
                return providerBars;
            }

            if (providerBars != null && providerBars.Count > 0 && providerBars.Count < safeCount)
            {
                LogInfo("历史行情系统数量不足，回退历史行情表：交易所={Exchange} 标的={Symbol} 请求数量={Requested} 返回数量={Actual}",
                    exchangeKey, symbolKey, safeCount, providerBars.Count);
            }

            // 3. 回退到历史行情表
            var tableName = BuildTableName(exchangeKey, symbolKey, timeframeKey);
            var loadSw = System.Diagnostics.Stopwatch.StartNew();
            var rows = await _repository.QueryRangeAsync(tableName, null, endMs, safeCount, ct)
                .ConfigureAwait(false);
            var list = new List<OHLCV>(rows.Count);
            foreach (var row in rows)
            {
                list.Add(ToOhlcv(row));
            }

            list.Reverse();
            loadSw.Stop();

            LogInfo("数量查询完成：数据表={Table} 行数={Rows} 耗时={Elapsed}ms",
                tableName, rows.Count, loadSw.ElapsedMilliseconds);

            if ((providerBars == null || providerBars.Count == 0) && list.Count == 0)
            {
                throw new InvalidOperationException(
                    $"历史行情不存在: exchange={exchangeKey} symbol={symbolKey} timeframe={timeframeKey}");
            }

            return list;
        }

        /// <summary>
        /// 筛选驱动 K 线（按范围或按数量截取）
        /// </summary>
        public static List<OHLCV> SelectDrivingBars(
            List<OHLCV> bars,
            DateTimeOffset? startTime, DateTimeOffset? endTime,
            int barCount)
        {
            if (bars.Count == 0)
                return new List<OHLCV>();

            if (startTime.HasValue && endTime.HasValue)
            {
                var startMs = startTime.Value.ToUnixTimeMilliseconds();
                var endMs = endTime.Value.ToUnixTimeMilliseconds();
                var startIndex = FindFirstIndexAtOrAfterTimestamp(bars, startMs);
                if (startIndex < 0)
                {
                    return new List<OHLCV>();
                }

                var endIndex = FindLastIndexAtOrBeforeTimestamp(bars, endMs);
                if (endIndex < startIndex)
                {
                    return new List<OHLCV>();
                }

                return bars.GetRange(startIndex, endIndex - startIndex + 1);
            }

            if (barCount <= 0 || bars.Count <= barCount)
                return new List<OHLCV>(bars);

            return bars.GetRange(bars.Count - barCount, barCount);
        }

        /// <summary>
        /// 构建多标的交集时间轴
        /// </summary>
        public static List<long> BuildIntersection(IEnumerable<List<long>> sources, BacktestObjectPoolManager? objectPoolManager = null)
        {
            HashSet<long>? intersection = null;
            HashSet<long>? temp = null;

            try
            {
                intersection = objectPoolManager?.RentTimestampSet() ?? new HashSet<long>();
                temp = objectPoolManager?.RentTimestampSet() ?? new HashSet<long>();
                using var enumerator = sources.GetEnumerator();
                if (!enumerator.MoveNext())
                {
                    return new List<long>();
                }

                PopulatePositiveTimestamps(intersection, enumerator.Current);
                if (intersection.Count == 0)
                {
                    return new List<long>();
                }

                while (enumerator.MoveNext())
                {
                    var source = enumerator.Current;
                    if (source == null || source.Count == 0)
                    {
                        intersection.Clear();
                        break;
                    }

                    temp.Clear();
                    PopulatePositiveTimestamps(temp, source);
                    if (temp.Count == 0)
                    {
                        intersection.Clear();
                        break;
                    }

                    intersection.IntersectWith(temp);
                    if (intersection.Count == 0)
                    {
                        break;
                    }
                }

                if (intersection.Count == 0)
                {
                    return new List<long>();
                }

                var list = new List<long>(intersection.Count);
                foreach (var timestamp in intersection)
                {
                    list.Add(timestamp);
                }

                list.Sort();
                return list;
            }
            finally
            {
                if (temp != null && objectPoolManager != null)
                {
                    objectPoolManager.ReturnTimestampSet(temp);
                }

                if (intersection != null && objectPoolManager != null)
                {
                    objectPoolManager.ReturnTimestampSet(intersection);
                }
            }
        }

        private static int FindFirstIndexAtOrAfterTimestamp(IReadOnlyList<OHLCV> bars, long targetTimestamp)
        {
            var left = 0;
            var right = bars.Count - 1;
            var result = -1;
            while (left <= right)
            {
                var mid = left + ((right - left) >> 1);
                var timestamp = (long)(bars[mid].timestamp ?? 0);
                if (timestamp >= targetTimestamp)
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

        private static int FindLastIndexAtOrBeforeTimestamp(IReadOnlyList<OHLCV> bars, long targetTimestamp)
        {
            var left = 0;
            var right = bars.Count - 1;
            var result = -1;
            while (left <= right)
            {
                var mid = left + ((right - left) >> 1);
                var timestamp = (long)(bars[mid].timestamp ?? 0);
                if (timestamp <= targetTimestamp)
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

        private static void PopulatePositiveTimestamps(HashSet<long> target, IEnumerable<long>? source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var timestamp in source)
            {
                if (timestamp > 0)
                {
                    target.Add(timestamp);
                }
            }
        }

        private int ResolvePageSize()
        {
            var max = _maxQueryBars > 0 ? _maxQueryBars : 2000;
            return Math.Max(500, max);
        }

        internal static OHLCV ToOhlcv(HistoricalMarketDataKlineRow row)
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

        internal static string BuildTableName(string exchangeId, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);

            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!SafeIdentifier.IsMatch(exchangeKey) || !SafeIdentifier.IsMatch(symbolPart) || !SafeIdentifier.IsMatch(timeframeKey))
                throw new InvalidOperationException("历史行情表名不合法");

            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }
    }
}
