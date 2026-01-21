using System;
using System.Globalization;
using System.Linq;
using ServerTest.Models;
using ServerTest.Models.Strategy;

namespace ServerTest.Models.Indicator
{
    public static class IndicatorKeyFactory
    {
        public static IndicatorRequest? BuildRequest(TradeConfig trade, StrategyValueRef reference)
        {
            if (trade == null || reference == null)
            {
                return null;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(trade.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(trade.Symbol);
            var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(reference.Timeframe);
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                timeframe = MarketDataKeyNormalizer.TimeframeFromSeconds(trade.TimeframeSec);
            }

            if (string.IsNullOrWhiteSpace(exchange) ||
                string.IsNullOrWhiteSpace(symbol) ||
                string.IsNullOrWhiteSpace(timeframe))
            {
                return null;
            }

            var indicator = reference.Indicator?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(indicator))
            {
                return null;
            }

            var input = reference.Input?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                input = "CLOSE";
            }
            var output = string.IsNullOrWhiteSpace(reference.Output) ? "Value" : reference.Output.Trim();
            var calcMode = string.IsNullOrWhiteSpace(reference.CalcMode) ? "OnBarClose" : reference.CalcMode.Trim();

            var parameters = reference.Params?.ToArray() ?? Array.Empty<double>();
            var paramsKey = string.Join(
                ",",
                parameters.Select(p => p.ToString("G17", CultureInfo.InvariantCulture)));

            var maxOffset = 0;
            if (reference.OffsetRange != null && reference.OffsetRange.Length > 0)
            {
                maxOffset = reference.OffsetRange.Max();
            }

            var key = new IndicatorKey(
                exchange,
                symbol,
                timeframe,
                indicator,
                input,
                output,
                calcMode,
                paramsKey);

            return new IndicatorRequest(key, parameters, maxOffset);
        }
    }
}
