using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 贪婪恐慌指数采集器。
    /// </summary>
    public sealed class CoinGlassFearGreedCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassFearGreedCollector> _logger;

        public CoinGlassFearGreedCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassFearGreedCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.fear_greed.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.fear_greed", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            using var response = await _coinGlassClient
                .GetFearGreedHistoryAsync(_coinGlassOptions.FearGreedSeriesLimit, ct)
                .ConfigureAwait(false);

            var points = ExtractPoints(response.RootElement);
            if (points.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 贪婪恐慌接口返回格式无法解析有效点位");
            }

            var ordered = points
                .OrderBy(point => point.Timestamp)
                .ToList();
            var latest = ordered[^1];

            var latestPayload = new
            {
                value = latest.Value,
                classification = latest.Classification,
                sourceTs = latest.Timestamp,
                series = ordered.Select(point => new
                {
                    ts = point.Timestamp,
                    value = point.Value,
                    classification = point.Classification
                }).ToList()
            };

            var history = ordered
                .Select(point => new IndicatorHistoryPoint
                {
                    SourceTs = point.Timestamp,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        value = point.Value,
                        classification = point.Classification
                    })
                })
                .ToList();

            _logger.LogInformation(
                "CoinGlass 贪婪恐慌采集完成: count={Count}, latestValue={LatestValue}, latestTs={LatestTs}",
                ordered.Count,
                latest.Value,
                latest.Timestamp);

            return new IndicatorCollectResult
            {
                SourceTs = latest.Timestamp,
                PayloadJson = ProtocolJson.Serialize(latestPayload),
                History = history
            };
        }

        private static List<FearGreedPoint> ExtractPoints(JsonElement root)
        {
            var points = new List<FearGreedPoint>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            CollectPointCandidates(root, points, visited, depth: 0);

            return points
                .Where(point => point.Timestamp > 0)
                .GroupBy(point => point.Timestamp)
                .Select(group => group.Last())
                .ToList();
        }

        /// <summary>
        /// 递归扫描响应结构，尽可能从常见字段中提取点位。
        /// </summary>
        private static void CollectPointCandidates(
            JsonElement element,
            List<FearGreedPoint> output,
            HashSet<string> visited,
            int depth)
        {
            if (depth > 8)
            {
                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryParsePoint(item, out var point))
                    {
                        output.Add(point);
                    }
                    else
                    {
                        CollectPointCandidates(item, output, visited, depth + 1);
                    }
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var marker = $"{depth}:{property.Name}:{property.Value.ValueKind}";
                    if (visited.Add(marker))
                    {
                        CollectPointCandidates(property.Value, output, visited, depth + 1);
                    }
                }
            }
        }

        private static bool TryParsePoint(JsonElement item, out FearGreedPoint point)
        {
            point = default;
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var timestamp = TryReadTimestamp(item, "ts")
                ?? TryReadTimestamp(item, "timestamp")
                ?? TryReadTimestamp(item, "time")
                ?? TryReadTimestamp(item, "date")
                ?? TryReadTimestamp(item, "updateTime")
                ?? TryReadTimestamp(item, "createTime");

            var value = TryReadDecimal(item, "value")
                ?? TryReadDecimal(item, "score")
                ?? TryReadDecimal(item, "index")
                ?? TryReadDecimal(item, "fearGreedValue")
                ?? TryReadDecimal(item, "v");

            if (!timestamp.HasValue || !value.HasValue)
            {
                return false;
            }

            var classification = TryReadString(item, "classification")
                ?? TryReadString(item, "label")
                ?? TryReadString(item, "name")
                ?? TryReadString(item, "text");

            point = new FearGreedPoint(timestamp.Value, value.Value, classification);
            return true;
        }

        private static long? TryReadTimestamp(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
            {
                return NormalizeTimestamp(numeric);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (long.TryParse(text, out var numericString))
                {
                    return NormalizeTimestamp(numericString);
                }

                if (DateTimeOffset.TryParse(text, out var dateTime))
                {
                    return dateTime.ToUnixTimeMilliseconds();
                }
            }

            return null;
        }

        private static decimal? TryReadDecimal(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
            {
                return decimalValue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (decimal.TryParse(text, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static string? TryReadString(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static long NormalizeTimestamp(long raw)
        {
            // 小于 10^11 基本可判定为秒级时间戳。
            return raw < 100_000_000_000L ? raw * 1000 : raw;
        }

        private readonly record struct FearGreedPoint(long Timestamp, decimal Value, string? Classification);
    }
}
