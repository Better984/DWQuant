using System;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using TALib;

namespace ServerTest.Services
{
    internal sealed class IndicatorHandle
    {
        private readonly object _sync = new();
        private readonly IndicatorSeries _series;
        private readonly double[] _parameters;
        private Abstract.IndicatorFunction? _function;
        private int _lookback;
        private int _maxOffset;
        private long _lastComputedTimestamp;

        public IndicatorHandle(IndicatorKey key, double[] parameters, int maxOffset)
        {
            Key = key;
            _parameters = parameters ?? Array.Empty<double>();
            _maxOffset = Math.Max(0, maxOffset);
            _series = new IndicatorSeries(Math.Max(5, _maxOffset + 2));
        }

        public IndicatorKey Key { get; }

        public int MaxOffset
        {
            get
            {
                lock (_sync)
                {
                    return _maxOffset;
                }
            }
        }

        public void UpdateMaxOffset(int maxOffset)
        {
            var next = Math.Max(0, maxOffset);
            lock (_sync)
            {
                if (next <= _maxOffset)
                {
                    return;
                }

                _maxOffset = next;
            }
        }

        public bool TryGetValue(int offset, out double value)
        {
            return _series.TryGetValue(offset, out value);
        }

        public bool Update(
            MarketDataTask task,
            MarketDataEngine marketDataEngine,
            TalibIndicatorCalculator calculator,
            ILogger logger)
        {
            if (!IsMatchingTask(task))
            {
                return false;
            }

            if (string.Equals(Key.CalcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase) && !task.IsBarClose)
            {
                return false;
            }

            lock (_sync)
            {
                var targetTimestamp = task.CandleTimestamp;
                var skipWhenSame = string.Equals(Key.CalcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase);
                if (skipWhenSame && targetTimestamp > 0 && targetTimestamp <= _lastComputedTimestamp)
                {
                    return true;
                }

                _function ??= calculator.ResolveFunction(Key.Indicator);
                if (_function == null)
                {
                    logger.LogWarning("Indicator not found: {Indicator}", Key.Indicator);
                    return false;
                }

                if (_lookback <= 0)
                {
                    _lookback = calculator.GetLookback(_function, _parameters);
                }

                var requiredBars = Math.Max(_lookback + _maxOffset + 2, 5);
                _series.EnsureCapacity(requiredBars);

                var endTimestamp = calculator.ResolveEndTimestamp(Key.CalcMode, task.CandleTimestamp);
                var candles = marketDataEngine.GetHistoryKlines(
                    Key.Exchange,
                    Key.Timeframe,
                    Key.Symbol,
                    endTimestamp,
                    requiredBars);

                // 输出周期 和最新的5根K线
                logger.LogInformation($"指标计算 ：[{Key.Exchange}] {Key.Symbol} {Key.Timeframe} 最新的5根K线: \n" +
                    $"{string.Join("\n", candles.TakeLast(5).Select(c => $"time={MarketDataEngine.FormatTimestamp(c.timestamp ?? 0)}, close={c.close}"))}\n");
                if (candles.Count == 0)
                {
                    return false;
                }

                if (!calculator.TryCompute(Key, _function, _parameters, candles, _series.Capacity, out var points))
                {
                    logger.LogDebug("Indicator compute skipped: {Key}", Key.ToString());
                    return false;
                }

                _series.AddPoints(points);
                if (points.Count > 0)
                {
                    _lastComputedTimestamp = points[^1].Timestamp;
                }

                return points.Count > 0;
            }
        }

        private bool IsMatchingTask(MarketDataTask task)
        {
            return Key.Exchange == task.Exchange &&
                   Key.Symbol == task.Symbol &&
                   Key.Timeframe == task.Timeframe;
        }
    }
}
