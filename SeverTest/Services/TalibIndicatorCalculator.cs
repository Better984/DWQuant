using System;
using System.Collections.Generic;
using System.Linq;
using ccxt;
using ServerTest.Models.Indicator;
using TALib;

namespace ServerTest.Services
{
    internal sealed class TalibIndicatorCalculator
    {
        private readonly TalibIndicatorCatalog? _catalog;

        public TalibIndicatorCalculator(TalibIndicatorCatalog? catalog)
        {
            _catalog = catalog;
        }

        public Abstract.IndicatorFunction? ResolveFunction(string indicator)
        {
            if (string.IsNullOrWhiteSpace(indicator))
            {
                return null;
            }

            return Abstract.Function(indicator);
        }

        public int GetLookback(Abstract.IndicatorFunction func, double[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return func.Lookback();
            }

            var intOptions = new int[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                intOptions[i] = (int)Math.Round(parameters[i], MidpointRounding.AwayFromZero);
            }

            return func.Lookback(intOptions);
        }

        public long? ResolveEndTimestamp(string calcMode, long candleTimestamp)
        {
            return string.Equals(calcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase)
                ? candleTimestamp
                : null;
        }

        public bool TryCompute(
            IndicatorKey key,
            Abstract.IndicatorFunction func,
            double[] parameters,
            List<OHLCV> candles,
            int maxPoints,
            out List<IndicatorPoint> points)
        {
            points = new List<IndicatorPoint>();
            if (candles == null || candles.Count == 0)
            {
                return false;
            }

            if (!TryBuildInputs(func, key.Input, candles, out var inputs))
            {
                return false;
            }

            var outputs = new double[func.Outputs.Length][];
            for (var i = 0; i < outputs.Length; i++)
            {
                outputs[i] = new double[candles.Count];
            }

            var retCode = func.Run<double>(
                inputs,
                parameters,
                outputs,
                new Range(0, candles.Count - 1),
                out var outRange);

            if (retCode != Core.RetCode.Success)
            {
                Console.WriteLine("更新指标出错："+retCode);
                return false;
            }

            var outputIndex = ResolveOutputIndex(func, key);
            if (outputIndex < 0 || outputIndex >= outputs.Length)
            {
                outputIndex = 0;
            }

            var outStart = outRange.Start.Value;
            var outEnd = outRange.End.Value;
            if (outEnd <= outStart)
            {
                return false;
            }

            var available = outEnd - outStart;
            var needed = Math.Min(available, Math.Max(1, maxPoints));
            var startIndex = outEnd - needed;

            for (var i = startIndex; i < outEnd; i++)
            {
                // TA-Lib writes outputs from index 0; outRange maps them back to input indices.
                var valueIndex = i - outStart;
                var value = outputs[outputIndex][valueIndex];
                if (double.IsNaN(value))
                {
                    continue;
                }

                var timestamp = candles[i].timestamp ?? 0;
                if (timestamp <= 0)
                {
                    continue;
                }

                points.Add(new IndicatorPoint(timestamp, value));
            }

            return points.Count > 0;
        }

        private bool TryBuildInputs(
            Abstract.IndicatorFunction func,
            string input,
            List<OHLCV> candles,
            out double[][] inputs)
        {
            inputs = Array.Empty<double[]>();
            var inputNames = func.Inputs;
            if (inputNames == null || inputNames.Length == 0)
            {
                return false;
            }

            var realInputs = SplitRealInputs(
                input,
                inputNames.Count(n => n.Equals("Real", StringComparison.OrdinalIgnoreCase)));
            if (realInputs == null)
            {
                return false;
            }

            inputs = new double[inputNames.Length][];
            var realIndex = 0;

            for (var i = 0; i < inputNames.Length; i++)
            {
                var name = inputNames[i];
                if (name.Equals("Real", StringComparison.OrdinalIgnoreCase))
                {
                    inputs[i] = BuildSeries(candles, realInputs[realIndex++]);
                    continue;
                }

                inputs[i] = BuildSeries(candles, name);
            }

            return true;
        }

        private static string[]? SplitRealInputs(string input, int count)
        {
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            if (count == 1)
            {
                var selected = string.IsNullOrWhiteSpace(input) ? "Close" : input.Trim();
                return new[] { selected };
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != count)
            {
                return null;
            }

            return parts;
        }

        private static double[] BuildSeries(List<OHLCV> candles, string input)
        {
            var result = new double[candles.Count];
            var key = input?.Trim() ?? string.Empty;

            for (var i = 0; i < candles.Count; i++)
            {
                result[i] = ResolveValue(candles[i], key);
            }

            return result;
        }

        internal static double ResolveValue(OHLCV candle, string input)
        {
            var key = input.ToUpperInvariant();
            var open = candle.open ?? double.NaN;
            var high = candle.high ?? double.NaN;
            var low = candle.low ?? double.NaN;
            var close = candle.close ?? double.NaN;
            var volume = candle.volume ?? double.NaN;

            return key switch
            {
                "OPEN" => open,
                "HIGH" => high,
                "LOW" => low,
                "CLOSE" => close,
                "VOLUME" => volume,
                "HL2" => (high + low) / 2.0,
                "HLC3" => (high + low + close) / 3.0,
                "OHLC4" => (open + high + low + close) / 4.0,
                "OC2" => (open + close) / 2.0,
                "HLCC4" => (high + low + close + close) / 4.0,
                _ => close
            };
        }

        private int ResolveOutputIndex(Abstract.IndicatorFunction func, IndicatorKey key)
        {
            var outputKey = key.Output;
            if (string.IsNullOrWhiteSpace(outputKey) ||
                outputKey.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                outputKey.Equals("Real", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (_catalog != null && _catalog.TryGet(key.Indicator, out var def))
            {
                var index = def.Outputs.FindIndex(o =>
                    outputKey.Equals(o.Key, StringComparison.OrdinalIgnoreCase) ||
                    outputKey.Equals(o.Hint, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    return index;
                }
            }

            var fallbackIndex = Array.FindIndex(func.Outputs, o =>
                outputKey.Equals(o.displayName, StringComparison.OrdinalIgnoreCase));
            return fallbackIndex >= 0 ? fallbackIndex : 0;
        }
    }
}
