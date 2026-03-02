using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Globalization;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 交易对爆仓热力图（模型1）采集器。
    /// </summary>
    public sealed class CoinGlassLiquidationHeatmapCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassLiquidationHeatmapCollector> _logger;

        public CoinGlassLiquidationHeatmapCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassLiquidationHeatmapCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.liquidation_heatmap_model1.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.liquidation_heatmap_model1", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var exchange = ResolveScopeValue(scopeKey, "exchange", _coinGlassOptions.LiquidationHeatmapDefaultExchange);
            var symbol = ResolveScopeValue(scopeKey, "symbol", _coinGlassOptions.LiquidationHeatmapDefaultSymbol);
            var range = ResolveScopeValue(scopeKey, "range", _coinGlassOptions.LiquidationHeatmapDefaultRange);

            _logger.LogInformation(
                "[coinglass][爆仓热力图] 开始拉取爆仓热力图（模型1）: exchange={Exchange}, symbol={Symbol}, range={Range}, scopeKey={ScopeKey}",
                exchange,
                symbol,
                range,
                scopeKey);

            using var response = await _coinGlassClient
                .GetLiquidationHeatmapModel1Async(exchange, symbol, range, ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement, exchange, symbol, range);

            var payload = new
            {
                exchange = parsed.Exchange,
                symbol = parsed.Symbol,
                range = parsed.Range,
                sourceTs = parsed.SourceTs,
                yAxis = parsed.YAxis,
                liquidationLeverageData = parsed.HeatmapPoints.Select(point => new
                {
                    xIndex = point.XIndex,
                    yIndex = point.YIndex,
                    liquidationUsd = point.LiquidationUsd
                }).ToList(),
                priceCandlesticks = parsed.Candlesticks.Select(candle => new
                {
                    ts = candle.TimestampMs,
                    open = candle.Open,
                    high = candle.High,
                    low = candle.Low,
                    close = candle.Close,
                    volumeUsd = candle.VolumeUsd
                }).ToList(),
                stats = new
                {
                    yAxisCount = parsed.YAxis.Count,
                    heatmapPointCount = parsed.HeatmapPoints.Count,
                    candleCount = parsed.Candlesticks.Count
                }
            };

            var history = parsed.Candlesticks
                .Select(candle => new IndicatorHistoryPoint
                {
                    SourceTs = candle.TimestampMs,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        open = candle.Open,
                        high = candle.High,
                        low = candle.Low,
                        close = candle.Close,
                        volumeUsd = candle.VolumeUsd
                    })
                })
                .ToList();

            if (history.Count == 0)
            {
                history.Add(new IndicatorHistoryPoint
                {
                    SourceTs = parsed.SourceTs,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        yAxisCount = parsed.YAxis.Count,
                        heatmapPointCount = parsed.HeatmapPoints.Count
                    })
                });
            }

            _logger.LogInformation(
                "[coinglass][爆仓热力图] 爆仓热力图采集完成: exchange={Exchange}, symbol={Symbol}, range={Range}, yAxisCount={YAxisCount}, heatmapPointCount={HeatmapPointCount}, candleCount={CandleCount}, sourceTs={SourceTs}",
                parsed.Exchange,
                parsed.Symbol,
                parsed.Range,
                parsed.YAxis.Count,
                parsed.HeatmapPoints.Count,
                parsed.Candlesticks.Count,
                parsed.SourceTs);

            return new IndicatorCollectResult
            {
                SourceTs = parsed.SourceTs,
                PayloadJson = ProtocolJson.Serialize(payload),
                History = history
            };
        }

        private static ParsedHeatmapResponse ParseResponse(
            JsonElement root,
            string exchange,
            string symbol,
            string range)
        {
            ValidateResponseCode(root);

            var dataRoot = root;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object)
            {
                dataRoot = data;
            }

            var yAxis = ReadYAxis(dataRoot);
            var heatmapPoints = ReadHeatmapPoints(dataRoot);
            var candlesticks = ReadCandlesticks(dataRoot);

            if (yAxis.Count == 0 && heatmapPoints.Count == 0 && candlesticks.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 爆仓热力图接口返回结构为空，无法解析有效数据");
            }

            var sourceTs = candlesticks.Count > 0
                ? candlesticks.Max(item => item.TimestampMs)
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return new ParsedHeatmapResponse(
                Exchange: exchange,
                Symbol: symbol,
                Range: range,
                SourceTs: sourceTs,
                YAxis: yAxis,
                HeatmapPoints: heatmapPoints,
                Candlesticks: candlesticks);
        }

        private static void ValidateResponseCode(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("code", out var codeElement))
            {
                return;
            }

            var codeText = codeElement.ValueKind switch
            {
                JsonValueKind.String => codeElement.GetString(),
                JsonValueKind.Number when codeElement.TryGetInt64(out var codeNum) => codeNum.ToString(CultureInfo.InvariantCulture),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(codeText) || string.Equals(codeText, "0", StringComparison.Ordinal))
            {
                return;
            }

            var message = TryReadString(root, "msg")
                ?? TryReadString(root, "message")
                ?? "未知错误";
            throw new InvalidOperationException($"CoinGlass 爆仓热力图接口返回错误: code={codeText}, msg={message}");
        }

        private static List<decimal> ReadYAxis(JsonElement dataRoot)
        {
            if (!TryGetArrayProperty(dataRoot, out var array, "y_axis", "yAxis"))
            {
                return new List<decimal>();
            }

            var result = new List<decimal>();
            foreach (var item in array.EnumerateArray())
            {
                var value = TryReadDecimal(item);
                if (value.HasValue)
                {
                    result.Add(value.Value);
                }
            }

            return result;
        }

        private static List<HeatmapPoint> ReadHeatmapPoints(JsonElement dataRoot)
        {
            if (!TryGetArrayProperty(dataRoot, out var array, "liquidation_leverage_data", "liquidationLeverageData"))
            {
                return new List<HeatmapPoint>();
            }

            var result = new List<HeatmapPoint>();
            foreach (var item in array.EnumerateArray())
            {
                var parsed = TryParseHeatmapPoint(item);
                if (parsed.HasValue)
                {
                    result.Add(parsed.Value);
                }
            }

            return result
                .GroupBy(item => $"{item.XIndex}:{item.YIndex}")
                .Select(group => group.Last())
                .ToList();
        }

        private static HeatmapPoint? TryParseHeatmapPoint(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().ToList();
                if (values.Count >= 3)
                {
                    var x = TryReadInt(values[0]);
                    var y = TryReadInt(values[1]);
                    var amount = TryReadDecimal(values[2]);
                    if (x.HasValue && y.HasValue && amount.HasValue)
                    {
                        return new HeatmapPoint(x.Value, y.Value, amount.Value);
                    }
                }

                return null;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var x = TryReadInt(item, "x")
                    ?? TryReadInt(item, "xIndex")
                    ?? TryReadInt(item, "x_index");
                var y = TryReadInt(item, "y")
                    ?? TryReadInt(item, "yIndex")
                    ?? TryReadInt(item, "y_index");
                var amount = TryReadDecimal(item, "value")
                    ?? TryReadDecimal(item, "liquidationUsd")
                    ?? TryReadDecimal(item, "liquidation_usd");
                if (x.HasValue && y.HasValue && amount.HasValue)
                {
                    return new HeatmapPoint(x.Value, y.Value, amount.Value);
                }
            }

            return null;
        }

        private static List<CandlestickPoint> ReadCandlesticks(JsonElement dataRoot)
        {
            if (!TryGetArrayProperty(dataRoot, out var array, "price_candlesticks", "priceCandlesticks"))
            {
                return new List<CandlestickPoint>();
            }

            var result = new List<CandlestickPoint>();
            foreach (var item in array.EnumerateArray())
            {
                var parsed = TryParseCandlestick(item);
                if (parsed.HasValue)
                {
                    result.Add(parsed.Value);
                }
            }

            return result
                .GroupBy(item => item.TimestampMs)
                .Select(group => group.Last())
                .OrderBy(item => item.TimestampMs)
                .ToList();
        }

        private static CandlestickPoint? TryParseCandlestick(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().ToList();
                if (values.Count >= 6)
                {
                    var ts = TryReadLong(values[0]);
                    if (!ts.HasValue)
                    {
                        return null;
                    }

                    return new CandlestickPoint(
                        TimestampMs: NormalizeTimestamp(ts.Value),
                        Open: TryReadDecimal(values[1]),
                        High: TryReadDecimal(values[2]),
                        Low: TryReadDecimal(values[3]),
                        Close: TryReadDecimal(values[4]),
                        VolumeUsd: TryReadDecimal(values[5]));
                }

                return null;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var ts = TryReadLong(item, "ts")
                    ?? TryReadLong(item, "timestamp")
                    ?? TryReadLong(item, "time");
                if (!ts.HasValue)
                {
                    return null;
                }

                return new CandlestickPoint(
                    TimestampMs: NormalizeTimestamp(ts.Value),
                    Open: TryReadDecimal(item, "open"),
                    High: TryReadDecimal(item, "high"),
                    Low: TryReadDecimal(item, "low"),
                    Close: TryReadDecimal(item, "close"),
                    VolumeUsd: TryReadDecimal(item, "volume")
                        ?? TryReadDecimal(item, "volumeUsd")
                        ?? TryReadDecimal(item, "volume_usd"));
            }

            return null;
        }

        private static string ResolveScopeValue(string scopeKey, string key, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(scopeKey))
            {
                var parts = scopeKey.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    var segments = part.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (segments.Length == 2 &&
                        string.Equals(segments[0], key, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(segments[1]))
                    {
                        return Uri.UnescapeDataString(segments[1]);
                    }
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private static bool TryGetArrayProperty(JsonElement obj, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (obj.ValueKind == JsonValueKind.Object &&
                    obj.TryGetProperty(name, out value) &&
                    value.ValueKind == JsonValueKind.Array)
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? TryReadString(JsonElement obj, string fieldName)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static int? TryReadInt(JsonElement obj, string fieldName)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            return TryReadInt(value);
        }

        private static int? TryReadInt(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static long? TryReadLong(JsonElement obj, string fieldName)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            return TryReadLong(value);
        }

        private static long? TryReadLong(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static decimal? TryReadDecimal(JsonElement obj, string fieldName)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            return TryReadDecimal(value);
        }

        private static decimal? TryReadDecimal(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static long NormalizeTimestamp(long raw)
        {
            // 小于 10^11 基本可判定为秒级时间戳。
            return raw < 100_000_000_000L ? raw * 1000 : raw;
        }

        private sealed record ParsedHeatmapResponse(
            string Exchange,
            string Symbol,
            string Range,
            long SourceTs,
            IReadOnlyList<decimal> YAxis,
            IReadOnlyList<HeatmapPoint> HeatmapPoints,
            IReadOnlyList<CandlestickPoint> Candlesticks);

        private readonly record struct HeatmapPoint(int XIndex, int YIndex, decimal LiquidationUsd);

        private readonly record struct CandlestickPoint(
            long TimestampMs,
            decimal? Open,
            decimal? High,
            decimal? Low,
            decimal? Close,
            decimal? VolumeUsd);
    }
}
