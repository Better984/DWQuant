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
            var limit = _coinGlassOptions.FearGreedSeriesLimit;
            _logger.LogInformation("[coinglass][贪婪恐慌] 开始拉取贪婪恐慌指标: limit={Limit}, scopeKey={ScopeKey}", limit, scopeKey);

            using var response = await _coinGlassClient
                .GetFearGreedHistoryAsync(limit, ct)
                .ConfigureAwait(false);

            var points = ExtractPoints(response.RootElement);
            _logger.LogInformation(
                "[coinglass][贪婪恐慌] 贪婪恐慌接口返回解析: 原始根类型={RootKind},数据{Json} 解析得到点位数量={PointCount}",
                response.RootElement.ValueKind.ToString(),
                response.RootElement,
                points.Count);

            if (points.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 贪婪恐慌接口返回格式无法解析有效点位");
            }

            var ordered = points
                .OrderBy(point => point.Timestamp)
                .ToList();
            var series = BuildSeriesWithSignals(ordered);
            var latest = series[^1];

            var latestPayload = new
            {
                value = latest.Point.Value,
                classification = latest.Point.Classification,
                sourceTs = latest.Point.Timestamp,
                signals = ToSignalPayload(latest.Signals),
                series = series.Select(item => new
                {
                    ts = item.Point.Timestamp,
                    value = item.Point.Value,
                    classification = item.Point.Classification,
                    signals = ToSignalPayload(item.Signals)
                }).ToList()
            };

            var history = series
                .Select(item => new IndicatorHistoryPoint
                {
                    SourceTs = item.Point.Timestamp,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        value = item.Point.Value,
                        classification = item.Point.Classification,
                        signals = ToSignalPayload(item.Signals)
                    })
                })
                .ToList();

            _logger.LogInformation(
                "[coinglass][贪婪恐慌] 贪婪恐慌采集完成: count={Count}, latestValue={LatestValue}, latestClassification={LatestClassification}, latestTs={LatestTs}",
                series.Count,
                latest.Point.Value,
                latest.Point.Classification ?? "(null)",
                latest.Point.Timestamp);

            return new IndicatorCollectResult
            {
                SourceTs = latest.Point.Timestamp,
                PayloadJson = ProtocolJson.Serialize(latestPayload),
                History = history
            };
        }

        private static List<FearGreedSeriesPoint> BuildSeriesWithSignals(IReadOnlyList<FearGreedPoint> ordered)
        {
            var result = new List<FearGreedSeriesPoint>(ordered.Count);
            var below9Streak = 0;
            var below10Streak = 0;

            foreach (var point in ordered)
            {
                var below9 = point.Value < 9m;
                var below10 = point.Value < 10m;
                below9Streak = below9 ? below9Streak + 1 : 0;
                below10Streak = below10 ? below10Streak + 1 : 0;

                var signals = new FearGreedDerivedSignals(
                    Below9: below9,
                    Below10: below10,
                    Below9StreakDays: below9Streak,
                    Below10StreakDays: below10Streak,
                    Below9Consecutive3d: below9Streak >= 3,
                    Below10Consecutive3d: below10Streak >= 3);

                result.Add(new FearGreedSeriesPoint(point, signals));
            }

            return result;
        }

        private static object ToSignalPayload(FearGreedDerivedSignals signals)
        {
            return new
            {
                below9 = signals.Below9,
                below10 = signals.Below10,
                below9StreakDays = signals.Below9StreakDays,
                below10StreakDays = signals.Below10StreakDays,
                below9Consecutive3d = signals.Below9Consecutive3d,
                below10Consecutive3d = signals.Below10Consecutive3d
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

            // 兼容 CoinGlass 的列式历史结构：data.values + data.dates（可选 labels/classifications）。
            if (TryCollectColumnarPoints(element, output))
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

        private static bool TryCollectColumnarPoints(JsonElement obj, List<FearGreedPoint> output)
        {
            var values = TryReadDecimalArray(obj, "values")
                ?? TryReadDecimalArray(obj, "valueList")
                ?? TryReadDecimalArray(obj, "fearGreedValues");
            if (values == null || values.Count == 0)
            {
                return false;
            }

            var timestamps = TryReadTimestampArray(obj, "dates")
                ?? TryReadTimestampArray(obj, "timestamps")
                ?? TryReadTimestampArray(obj, "times");
            if (timestamps == null || timestamps.Count == 0)
            {
                return false;
            }

            var classifications = TryReadStringArray(obj, "classifications")
                ?? TryReadStringArray(obj, "labels")
                ?? TryReadStringArray(obj, "texts");

            var count = Math.Min(values.Count, timestamps.Count);
            var added = 0;
            for (var index = 0; index < count; index++)
            {
                if (!values[index].HasValue || !timestamps[index].HasValue)
                {
                    continue;
                }

                string? classification = null;
                if (classifications != null && index < classifications.Count)
                {
                    classification = classifications[index];
                }

                output.Add(new FearGreedPoint(
                    timestamps[index]!.Value,
                    values[index]!.Value,
                    classification));
                added++;
            }

            return added > 0;
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

        private static List<long?>? TryReadTimestampArray(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<long?>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var numeric))
                {
                    result.Add(NormalizeTimestamp(numeric));
                    continue;
                }

                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (long.TryParse(text, out var numericText))
                        {
                            result.Add(NormalizeTimestamp(numericText));
                            continue;
                        }

                        if (DateTimeOffset.TryParse(text, out var dateTime))
                        {
                            result.Add(dateTime.ToUnixTimeMilliseconds());
                            continue;
                        }
                    }
                }

                result.Add(null);
            }

            return result;
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

        private static List<decimal?>? TryReadDecimalArray(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<decimal?>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var numeric))
                {
                    result.Add(numeric);
                    continue;
                }

                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && decimal.TryParse(text, out var numericText))
                    {
                        result.Add(numericText);
                        continue;
                    }
                }

                result.Add(null);
            }

            return result;
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

        private static List<string?>? TryReadStringArray(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<string?>();
            foreach (var item in value.EnumerateArray())
            {
                result.Add(item.ValueKind == JsonValueKind.String ? item.GetString() : null);
            }

            return result;
        }

        private static long NormalizeTimestamp(long raw)
        {
            // 小于 10^11 基本可判定为秒级时间戳。
            return raw < 100_000_000_000L ? raw * 1000 : raw;
        }

        private readonly record struct FearGreedPoint(long Timestamp, decimal Value, string? Classification);

        private readonly record struct FearGreedSeriesPoint(FearGreedPoint Point, FearGreedDerivedSignals Signals);

        private readonly record struct FearGreedDerivedSignals(
            bool Below9,
            bool Below10,
            int Below9StreakDays,
            int Below10StreakDays,
            bool Below9Consecutive3d,
            bool Below10Consecutive3d);
    }
}
