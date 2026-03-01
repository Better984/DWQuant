using ccxt;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// TA 指标统一计算规则。
    /// 该规则需要与前端 talibCalcRules.ts 保持一致。
    /// </summary>
    internal static class TalibCalcRules
    {
        /// <summary>
        /// 指标最小历史根数下限，避免空窗口导致计算失败。
        /// </summary>
        private const int MinRequiredBars = 5;

        /// <summary>
        /// EMA 族指标的最小预热根数（用于降低短窗口初值偏移）。
        /// </summary>
        private const int EmaFamilyMinWarmupBars = 200;

        public static int RoundToIntOption(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public static string NormalizeInputSource(string? source)
        {
            var normalized = (source ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return normalized switch
            {
                "OPEN" => "OPEN",
                "HIGH" => "HIGH",
                "LOW" => "LOW",
                "CLOSE" => "CLOSE",
                "VOLUME" => "VOLUME",
                "HL2" => "HL2",
                "HLC3" => "HLC3",
                "OHLC4" => "OHLC4",
                "OC2" => "OC2",
                "HLCC4" => "HLCC4",
                _ => "CLOSE"
            };
        }

        public static double ResolveSourceValue(OHLCV candle, string? source)
        {
            var key = NormalizeInputSource(source);
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

        /// <summary>
        /// 统一计算指标所需历史根数。
        /// 规则：
        /// 1) 基础规则：lookback + maxOffset + 2；
        /// 2) EMA 族规则：额外要求至少 200 根预热，降低初值偏移。
        /// </summary>
        public static int ResolveRequiredBars(string? indicator, int lookback, int maxOffset)
        {
            var safeLookback = Math.Max(0, lookback);
            var safeMaxOffset = Math.Max(0, maxOffset);
            var baseRequired = Math.Max(MinRequiredBars, safeLookback + safeMaxOffset + 2);
            if (!IsEmaFamily(indicator))
            {
                return baseRequired;
            }

            var emaRequired = safeMaxOffset + EmaFamilyMinWarmupBars;
            return Math.Max(baseRequired, emaRequired);
        }

        private static bool IsEmaFamily(string? indicator)
        {
            var code = (indicator ?? string.Empty).Trim().ToUpperInvariant();
            return code switch
            {
                "EMA" => true,
                "DEMA" => true,
                "TEMA" => true,
                "TRIX" => true,
                "T3" => true,
                "KAMA" => true,
                "MAMA" => true,
                _ => false
            };
        }
    }
}
