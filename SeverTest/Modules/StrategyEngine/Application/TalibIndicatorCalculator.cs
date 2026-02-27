using ccxt;
using Microsoft.Extensions.Logging;
using ServerTest.Models.Indicator;
using TALib;

namespace ServerTest.Modules.StrategyEngine.Application
{
    internal sealed class TalibIndicatorCalculator
    {
        private readonly TalibIndicatorCatalog? _catalog;
        private readonly TalibWasmNodePool? _wasmPool;

        public TalibIndicatorCalculator(TalibIndicatorCatalog? catalog, TalibWasmNodePool? wasmPool = null)
        {
            _catalog = catalog;
            _wasmPool = wasmPool;
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
                intOptions[i] = TalibCalcRules.RoundToIntOption(parameters[i]);
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
            out List<IndicatorPoint> points,
            out string computeCore,
            ILogger? logger = null)
        {
            points = new List<IndicatorPoint>();
            computeCore = "未知";
            if (candles == null || candles.Count == 0)
            {
                computeCore = "无K线";
                return false;
            }

            if (!TryBuildInputs(func, key.Input, candles, out var inputs))
            {
                computeCore = "输入构建失败";
                return false;
            }

            var outputIndex = ResolveOutputIndex(func, key);
            if (outputIndex < 0)
            {
                outputIndex = 0;
            }

            if (_wasmPool is { IsEnabled: true })
            {
                if (_wasmPool.TryCompute(
                    key.Indicator,
                    inputs,
                    parameters,
                    candles.Count,
                    out var wasmOutputs,
                    out var wasmError))
                {
                    computeCore = "WASM";
                    return TryBuildPointsFromAlignedOutputs(
                        candles,
                        outputIndex,
                        maxPoints,
                        wasmOutputs,
                        out points);
                }

                computeCore = "WASM失败回退TALib";
                logger?.LogWarning(
                    "同核心计算失败，指标={Indicator}，错误={Error}",
                    key.Indicator,
                    wasmError);
                // 极简稳定模式：WASM 失败后继续使用本地 TALib 兜底，避免任务直接失败。
            }
            else
            {
                computeCore = "TALib";
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
                computeCore = $"{computeCore}-失败:{retCode}";
                logger?.LogWarning("更新指标出错: {RetCode}", retCode);
                return false;
            }

            if (outputIndex < 0 || outputIndex >= outputs.Length)
            {
                outputIndex = 0;
            }

            var outStart = outRange.Start.Value;
            var outEnd = outRange.End.Value;
            if (outEnd <= outStart)
            {
                computeCore = $"{computeCore}-无输出范围";
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

            if (points.Count == 0)
            {
                computeCore = $"{computeCore}-无有效点";
                return false;
            }

            return true;
        }

        private static bool TryBuildPointsFromAlignedOutputs(
            List<OHLCV> candles,
            int outputIndex,
            int maxPoints,
            List<double?[]> outputs,
            out List<IndicatorPoint> points)
        {
            points = new List<IndicatorPoint>();
            if (outputs.Count == 0 || outputIndex >= outputs.Count)
            {
                return false;
            }

            var targetOutput = outputs[outputIndex];
            if (targetOutput == null || targetOutput.Length == 0)
            {
                return false;
            }

            var length = Math.Min(candles.Count, targetOutput.Length);
            if (length <= 0)
            {
                return false;
            }

            var needed = Math.Min(length, Math.Max(1, maxPoints));
            var startIndex = length - needed;

            for (var i = startIndex; i < length; i++)
            {
                var maybeValue = targetOutput[i];
                if (maybeValue is null || double.IsNaN(maybeValue.Value))
                {
                    continue;
                }

                var timestamp = candles[i].timestamp ?? 0;
                if (timestamp <= 0)
                {
                    continue;
                }

                points.Add(new IndicatorPoint(timestamp, maybeValue.Value));
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

            var realCount = inputNames.Count(n => n.Equals("Real", StringComparison.OrdinalIgnoreCase));
            var namedInputs = ParseNamedInputs(input);
            var realInputs = SplitRealInputs(
                input,
                realCount);
            if (realCount > 0 && realInputs == null && namedInputs.Count == 0)
            {
                return false;
            }

            inputs = new double[inputNames.Length][];
            var realIndex = 0;
            var periodsIndex = 0;

            for (var i = 0; i < inputNames.Length; i++)
            {
                var name = inputNames[i];
                if (name.Equals("Real", StringComparison.OrdinalIgnoreCase))
                {
                    realIndex++;
                    var source = ResolveNamedInput(namedInputs, "REAL", realIndex);
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        source = realInputs != null && realInputs.Length >= realIndex
                            ? realInputs[realIndex - 1]
                            : "Close";
                    }

                    inputs[i] = BuildSeries(candles, source);
                    continue;
                }

                if (name.Equals("Periods", StringComparison.OrdinalIgnoreCase))
                {
                    periodsIndex++;
                    // MAVP 等指标的 Periods 输入支持单独配置，未配置时兼容回退 Close。
                    var source = ResolveNamedInput(namedInputs, "PERIODS", periodsIndex) ?? "Close";
                    inputs[i] = BuildSeries(candles, source);
                    continue;
                }

                inputs[i] = BuildSeries(candles, name);
            }

            return true;
        }

        private static Dictionary<string, string> ParseNamedInputs(string input)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(input) || !input.Contains('='))
            {
                return map;
            }

            var separator = input.Contains(';') ? ';' : ',';
            var pairs = input.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                var equalIndex = pair.IndexOf('=');
                if (equalIndex <= 0 || equalIndex >= pair.Length - 1)
                {
                    continue;
                }

                var rawKey = pair[..equalIndex].Trim();
                var rawValue = pair[(equalIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(rawKey) || string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                var key = rawKey.ToUpperInvariant();
                var value = TalibCalcRules.NormalizeInputSource(rawValue);
                map[key] = value;
            }

            return map;
        }

        private static string? ResolveNamedInput(
            IReadOnlyDictionary<string, string> namedInputs,
            string baseName,
            int index)
        {
            var indexedKey = $"{baseName}{index}";
            if (namedInputs.TryGetValue(indexedKey, out var indexedValue) &&
                !string.IsNullOrWhiteSpace(indexedValue))
            {
                return indexedValue;
            }

            if (namedInputs.TryGetValue(baseName, out var baseValue) &&
                !string.IsNullOrWhiteSpace(baseValue))
            {
                return baseValue;
            }

            return null;
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
                selected = TalibCalcRules.NormalizeInputSource(selected);
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

            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = TalibCalcRules.NormalizeInputSource(parts[i]);
            }

            return parts;
        }

        private static double[] BuildSeries(List<OHLCV> candles, string input)
        {
            var result = new double[candles.Count];
            var key = TalibCalcRules.NormalizeInputSource(input);

            for (var i = 0; i < candles.Count; i++)
            {
                result[i] = ResolveValue(candles[i], key);
            }

            return result;
        }

        internal static double ResolveValue(OHLCV candle, string input)
        {
            return TalibCalcRules.ResolveSourceValue(candle, input);
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
