using ccxt;
using ServerTest.Models;
using System;
using System.Collections.Generic;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测行情提供者（内存K线列表）
    /// </summary>
    internal sealed class BacktestMarketDataProvider : IMarketDataProvider
    {
        private sealed class BacktestSeries
        {
            public BacktestSeries(List<OHLCV> bars)
            {
                Bars = bars;
                TimestampIndex = new Dictionary<long, int>(bars.Count);
                for (var i = 0; i < bars.Count; i++)
                {
                    var ts = (long)(bars[i].timestamp ?? 0);
                    if (ts > 0 && !TimestampIndex.ContainsKey(ts))
                    {
                        TimestampIndex[ts] = i;
                    }
                }
            }

            public List<OHLCV> Bars { get; }
            public Dictionary<long, int> TimestampIndex { get; }
        }

        private readonly Dictionary<string, BacktestSeries> _series;

        public BacktestMarketDataProvider(Dictionary<string, List<OHLCV>> series)
        {
            _series = new Dictionary<string, BacktestSeries>(series.Count);
            foreach (var item in series)
            {
                _series[item.Key] = new BacktestSeries(item.Value);
            }
        }

        public List<OHLCV> GetHistoryKlines(
            string exchangeId,
            string timeframe,
            string symbol,
            long? endTimestamp,
            int count)
        {
            if (count <= 0)
            {
                return new List<OHLCV>();
            }

            if (!_series.TryGetValue(BuildKey(exchangeId, symbol, timeframe), out var series))
            {
                return new List<OHLCV>();
            }

            var bars = series.Bars;
            if (bars.Count == 0)
            {
                return new List<OHLCV>();
            }

            var endIndex = bars.Count - 1;
            if (endTimestamp.HasValue)
            {
                endIndex = FindLastIndex(bars, endTimestamp.Value);
                if (endIndex < 0)
                {
                    return new List<OHLCV>();
                }
            }

            var startIndex = Math.Max(0, endIndex - count + 1);
            return bars.GetRange(startIndex, endIndex - startIndex + 1);
        }

        public bool TryGetBar(string exchangeId, string timeframe, string symbol, long timestamp, out OHLCV bar)
        {
            bar = default;
            if (!_series.TryGetValue(BuildKey(exchangeId, symbol, timeframe), out var series))
            {
                return false;
            }

            if (!series.TimestampIndex.TryGetValue(timestamp, out var index))
            {
                return false;
            }

            if (index < 0 || index >= series.Bars.Count)
            {
                return false;
            }

            bar = series.Bars[index];
            return true;
        }

        public IReadOnlyList<long> GetTimestamps(string exchangeId, string timeframe, string symbol)
        {
            if (!_series.TryGetValue(BuildKey(exchangeId, symbol, timeframe), out var series))
            {
                return Array.Empty<long>();
            }

            var result = new long[series.Bars.Count];
            for (var i = 0; i < series.Bars.Count; i++)
            {
                result[i] = (long)(series.Bars[i].timestamp ?? 0);
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

        public static string BuildKey(string exchangeId, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            return $"{exchangeKey}|{symbolKey}|{timeframeKey}";
        }
    }
}
