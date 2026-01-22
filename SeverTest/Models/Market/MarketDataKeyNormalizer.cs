using System.Text.RegularExpressions;

namespace ServerTest.Models
{
    public static class MarketDataKeyNormalizer
    {
        public static string NormalizeExchange(string exchange)
        {
            return string.IsNullOrWhiteSpace(exchange)
                ? string.Empty
                : exchange.Trim().ToLowerInvariant();
        }

        public static string NormalizeSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return string.Empty;
            }

            var trimmed = symbol.Trim().Replace("_", "/", StringComparison.Ordinal);
            if (trimmed.Contains("/"))
            {
                var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    return $"{parts[0].ToUpperInvariant()}/{parts[1].ToUpperInvariant()}";
                }
            }

            var upper = trimmed.ToUpperInvariant();
            if (upper.EndsWith("USDT", StringComparison.Ordinal) && upper.Length > 4)
            {
                return $"{upper[..^4]}/USDT";
            }

            return upper;
        }

        public static string NormalizeTimeframe(string timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                return string.Empty;
            }

            var tf = timeframe.Trim();
            tf = tf.Replace(" ", string.Empty, StringComparison.Ordinal);
            tf = tf.ToLowerInvariant();

            if (Regex.IsMatch(tf, @"^\d+mo$"))
            {
                return tf;
            }

            if (Regex.IsMatch(tf, @"^mo\d+$"))
            {
                return tf[2..] + "mo";
            }

            if (Regex.IsMatch(tf, @"^\d+[mhdw]$"))
            {
                return tf;
            }

            if (Regex.IsMatch(tf, @"^[mhdw]\d+$"))
            {
                return tf[1..] + tf[0];
            }

            if (tf.StartsWith("mo", StringComparison.Ordinal))
            {
                return tf.Length > 2 ? tf[2..] + "mo" : "mo";
            }

            if (tf.StartsWith("h", StringComparison.Ordinal))
            {
                return tf[1..] + "h";
            }

            if (tf.StartsWith("m", StringComparison.Ordinal))
            {
                return tf[1..] + "m";
            }

            if (tf.StartsWith("d", StringComparison.Ordinal))
            {
                return tf[1..] + "d";
            }

            if (tf.StartsWith("w", StringComparison.Ordinal))
            {
                return tf[1..] + "w";
            }

            return tf;
        }

        public static string TimeframeFromSeconds(int timeframeSec)
        {
            return timeframeSec switch
            {
                60 => "1m",
                180 => "3m",
                300 => "5m",
                900 => "15m",
                1800 => "30m",
                3600 => "1h",
                7200 => "2h",
                14400 => "4h",
                21600 => "6h",
                28800 => "8h",
                43200 => "12h",
                86400 => "1d",
                259200 => "3d",
                604800 => "1w",
                2592000 => "1mo",
                _ => string.Empty
            };
        }
    }
}
