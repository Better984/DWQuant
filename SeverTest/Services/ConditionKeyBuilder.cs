using ServerTest.Models;
using ServerTest.Models.Strategy;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ServerTest.Services
{
    public static class ConditionKeyBuilder
    {
        public static ConditionKey BuildKey(TradeConfig trade, StrategyMethod method)
        {
            var text = BuildKeyText(trade, method);
            var id = ComputeMd5(text);
            return new ConditionKey(id, text);
        }

        private static string BuildKeyText(TradeConfig trade, StrategyMethod method)
        {
            var exchange = MarketDataKeyNormalizer.NormalizeExchange(trade.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(trade.Symbol);
            var timeframe = MarketDataKeyNormalizer.TimeframeFromSeconds(trade.TimeframeSec);

            var builder = new StringBuilder();
            builder.Append("trade=").Append(exchange)
                .Append('|').Append(symbol)
                .Append('|').Append(timeframe)
                .Append('|').Append("method=").Append(method.Method ?? string.Empty);

            if (method.Param != null && method.Param.Length > 0)
            {
                builder.Append("|param=").Append(string.Join(",", method.Param));
            }

            if (method.Args != null)
            {
                foreach (var arg in method.Args)
                {
                    if (arg == null)
                    {
                        continue;
                    }

                    var argTimeframe = MarketDataKeyNormalizer.NormalizeTimeframe(arg.Timeframe);
                    if (string.IsNullOrWhiteSpace(argTimeframe))
                    {
                        argTimeframe = timeframe;
                    }

                    builder.Append("|arg=");
                    builder.Append(arg.RefType ?? string.Empty).Append('|')
                        .Append((arg.Indicator ?? string.Empty).Trim().ToUpperInvariant()).Append('|')
                        .Append(argTimeframe).Append('|')
                        .Append((arg.Input ?? string.Empty).Trim().ToUpperInvariant()).Append('|')
                        .Append((arg.Output ?? string.Empty).Trim().ToUpperInvariant()).Append('|')
                        .Append((arg.CalcMode ?? string.Empty).Trim());

                    if (arg.OffsetRange != null && arg.OffsetRange.Length > 0)
                    {
                        builder.Append("|offset=");
                        builder.Append(string.Join(",", arg.OffsetRange));
                    }

                    if (arg.Params != null && arg.Params.Count > 0)
                    {
                        builder.Append("|params=");
                        builder.Append(string.Join(",", arg.Params.Select(p => p.ToString("G17", CultureInfo.InvariantCulture))));
                    }
                }
            }

            return builder.ToString();
        }

        private static string ComputeMd5(string input)
        {
            using var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = md5.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
