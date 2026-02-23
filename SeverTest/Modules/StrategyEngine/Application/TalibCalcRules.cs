using ccxt;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// TA 指标统一计算规则。
    /// 该规则需要与前端 talibCalcRules.ts 保持一致。
    /// </summary>
    internal static class TalibCalcRules
    {
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
    }
}
