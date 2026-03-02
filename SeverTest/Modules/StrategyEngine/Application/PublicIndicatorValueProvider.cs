using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Indicators.Application;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Modules.Indicators.Infrastructure;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// 公共指标取值服务：
    /// - 支持 RefType=PublicIndicator 的条件取值
    /// - 支持按策略执行时间点 + offset 回看历史
    /// - 采用内存缓存降低高频条件评估开销
    /// </summary>
    public sealed class PublicIndicatorValueProvider
    {
        private const int DefaultHistoryLimit = 512;
        private const int MaxHistoryLimit = 4096;
        private const long CacheTtlMs = 15_000;

        private readonly IndicatorQueryService _queryService;
        private readonly IndicatorRepository _repository;
        private readonly ILogger<PublicIndicatorValueProvider> _logger;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

        public PublicIndicatorValueProvider(
            IndicatorQueryService queryService,
            IndicatorRepository repository,
            ILogger<PublicIndicatorValueProvider> logger)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryResolveValue(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int offset,
            out double value)
        {
            value = double.NaN;
            if (context == null || reference == null)
            {
                return false;
            }

            var code = reference.Indicator?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogInformation(
                    "公共指标取值失败: 指标编码为空 uid={Uid}",
                    context.Strategy.UidCode);
                return false;
            }

            var scopeKey = BuildScopeKey(reference.Input);
            var fieldPath = NormalizeFieldPath(reference.Output);
            var safeOffset = Math.Max(0, offset);
            var asOfTs = ResolveAsOfTimestamp(context, reference);
            var requiredCount = safeOffset + 1;

            if (!TryGetPoints(code, scopeKey, asOfTs, requiredCount, out var points))
            {
                return false;
            }

            if (!TryGetPointByOffset(points, asOfTs, safeOffset, out var point))
            {
                _logger.LogInformation(
                    "公共指标取值失败: 历史点位不足 uid={Uid} code={Code} scope={Scope} asOf={AsOf} offset={Offset}",
                    context.Strategy.UidCode,
                    code,
                    scopeKey,
                    asOfTs,
                    safeOffset);
                return false;
            }

            if (!TryGetFieldValue(point.Fields, fieldPath, out value))
            {
                _logger.LogInformation(
                    "公共指标取值失败: 字段不存在 uid={Uid} code={Code} scope={Scope} field={Field} ts={Ts}",
                    context.Strategy.UidCode,
                    code,
                    scopeKey,
                    fieldPath,
                    point.SourceTs);
                return false;
            }

            return true;
        }

        private bool TryGetPoints(
            string code,
            string scopeKey,
            long asOfTs,
            int requiredCount,
            out IReadOnlyList<PublicIndicatorPoint> points)
        {
            points = Array.Empty<PublicIndicatorPoint>();
            var cacheKey = BuildCacheKey(code, scopeKey);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var entry = _cache.GetOrAdd(cacheKey, _ => new CacheEntry());

            if (TryUseCache(entry, nowMs, asOfTs, requiredCount, out points))
            {
                return true;
            }

            lock (entry.Sync)
            {
                if (TryUseCache(entry, nowMs, asOfTs, requiredCount, out points))
                {
                    return true;
                }

                if (!TryLoadPoints(code, scopeKey, requiredCount, out var loaded))
                {
                    return false;
                }

                entry.Points = loaded;
                entry.LoadedAtMs = nowMs;
                points = loaded;
                return points.Count > 0;
            }
        }

        private static bool TryUseCache(
            CacheEntry entry,
            long nowMs,
            long asOfTs,
            int requiredCount,
            out IReadOnlyList<PublicIndicatorPoint> points)
        {
            points = entry.Points;
            if (points.Count == 0)
            {
                return false;
            }

            if (nowMs - entry.LoadedAtMs > CacheTtlMs)
            {
                return false;
            }

            if (!CanSatisfy(points, asOfTs, requiredCount))
            {
                return false;
            }

            return true;
        }

        private bool TryLoadPoints(
            string code,
            string scopeKey,
            int requiredCount,
            out IReadOnlyList<PublicIndicatorPoint> points)
        {
            points = Array.Empty<PublicIndicatorPoint>();
            try
            {
                _queryService.EnsureInitializedAsync(CancellationToken.None).GetAwaiter().GetResult();
                var scope = ParseScopeKey(scopeKey);
                var latest = _queryService
                    .GetLatestAsync(code, scope, allowStale: true, forceRefresh: false, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var historyLimit = Math.Clamp(
                    Math.Max(DefaultHistoryLimit, requiredCount + 64),
                    requiredCount,
                    MaxHistoryLimit);

                var history = _repository
                    .GetHistoryAsync(code, scopeKey, startMs: null, endMs: null, historyLimit, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var loaded = new List<PublicIndicatorPoint>(history.Count + 1);
                foreach (var item in history)
                {
                    if (TryParsePoint(item.SourceTs, item.PayloadJson, out var point))
                    {
                        loaded.Add(point);
                    }
                }

                // 历史为空时，至少回退到最新快照，避免公共指标首次接入时无法评估。
                if (loaded.Count == 0 && TryParsePoint(latest.Snapshot.SourceTs, latest.Snapshot.PayloadJson, out var latestPoint))
                {
                    loaded.Add(latestPoint);
                }

                if (loaded.Count == 0)
                {
                    return false;
                }

                points = loaded
                    .OrderBy(item => item.SourceTs)
                    .GroupBy(item => item.SourceTs)
                    .Select(group => group.Last())
                    .ToList();

                return points.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "加载公共指标历史失败: code={Code} scope={Scope}",
                    code,
                    scopeKey);
                return false;
            }
        }

        private static bool CanSatisfy(IReadOnlyList<PublicIndicatorPoint> points, long asOfTs, int requiredCount)
        {
            if (points.Count < requiredCount)
            {
                return false;
            }

            if (asOfTs == long.MaxValue)
            {
                return true;
            }

            if (asOfTs < points[0].SourceTs)
            {
                return false;
            }

            var latestIndex = FindLastIndexLessThanOrEqual(points, asOfTs);
            return latestIndex >= requiredCount - 1;
        }

        private static bool TryGetPointByOffset(
            IReadOnlyList<PublicIndicatorPoint> points,
            long asOfTs,
            int offset,
            out PublicIndicatorPoint point)
        {
            point = default;
            if (points.Count == 0 || offset < 0)
            {
                return false;
            }

            var latestIndex = asOfTs == long.MaxValue
                ? points.Count - 1
                : FindLastIndexLessThanOrEqual(points, asOfTs);

            if (latestIndex < 0)
            {
                return false;
            }

            var targetIndex = latestIndex - offset;
            if (targetIndex < 0 || targetIndex >= points.Count)
            {
                return false;
            }

            point = points[targetIndex];
            return true;
        }

        private static int FindLastIndexLessThanOrEqual(IReadOnlyList<PublicIndicatorPoint> points, long asOfTs)
        {
            var left = 0;
            var right = points.Count - 1;
            var result = -1;

            while (left <= right)
            {
                var mid = left + (right - left) / 2;
                if (points[mid].SourceTs <= asOfTs)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return result;
        }

        private static bool TryParsePoint(long sourceTs, string payloadJson, out PublicIndicatorPoint point)
        {
            point = default;
            if (sourceTs <= 0 || string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                var fields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                FlattenNumericFields(document.RootElement, string.Empty, fields);

                if (fields.Count == 0)
                {
                    return false;
                }

                point = new PublicIndicatorPoint(sourceTs, fields);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void FlattenNumericFields(
            JsonElement element,
            string prefix,
            IDictionary<string, double> output)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var nextPrefix = string.IsNullOrWhiteSpace(prefix)
                            ? property.Name
                            : $"{prefix}.{property.Name}";
                        FlattenNumericFields(property.Value, nextPrefix, output);
                    }
                    break;
                case JsonValueKind.Number:
                    if (!string.IsNullOrWhiteSpace(prefix) && element.TryGetDouble(out var numeric))
                    {
                        output[prefix] = numeric;
                    }
                    break;
                case JsonValueKind.True:
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        output[prefix] = 1d;
                    }
                    break;
                case JsonValueKind.False:
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        output[prefix] = 0d;
                    }
                    break;
                case JsonValueKind.String:
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        var text = element.GetString();
                        if (!string.IsNullOrWhiteSpace(text) &&
                            double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        {
                            output[prefix] = parsed;
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        private static bool TryGetFieldValue(
            IReadOnlyDictionary<string, double> fields,
            string fieldPath,
            out double value)
        {
            value = double.NaN;
            if (fields == null || fields.Count == 0)
            {
                return false;
            }

            if (fields.TryGetValue(fieldPath, out value))
            {
                return true;
            }

            if (fieldPath.StartsWith("payload.", StringComparison.OrdinalIgnoreCase) &&
                fields.TryGetValue(fieldPath["payload.".Length..], out value))
            {
                return true;
            }

            return false;
        }

        private static long ResolveAsOfTimestamp(StrategyExecutionContext context, StrategyValueRef reference)
        {
            if (string.Equals(reference.CalcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase))
            {
                return context.Task.CandleTimestamp;
            }

            return long.MaxValue;
        }

        private static string BuildScopeKey(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "global";
            }

            var scope = ParseScopeKey(input);
            return IndicatorScopeKey.Build(scope, input);
        }

        private static Dictionary<string, string>? ParseScopeKey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (!raw.Contains('='))
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pairs = raw.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                var segments = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (segments.Length == 2 &&
                    !string.IsNullOrWhiteSpace(segments[0]) &&
                    !string.IsNullOrWhiteSpace(segments[1]))
                {
                    map[segments[0]] = segments[1];
                }
            }

            return map.Count == 0 ? null : map;
        }

        private static string NormalizeFieldPath(string? output)
        {
            if (string.IsNullOrWhiteSpace(output) ||
                string.Equals(output, "Value", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(output, "Real", StringComparison.OrdinalIgnoreCase))
            {
                return "value";
            }

            var value = output.Trim();
            if (value.StartsWith("$.", StringComparison.Ordinal))
            {
                value = value[2..];
            }

            return value;
        }

        private static string BuildCacheKey(string code, string scopeKey)
        {
            return $"{code.Trim().ToLowerInvariant()}|{scopeKey.Trim().ToLowerInvariant()}";
        }

        private sealed class CacheEntry
        {
            public object Sync { get; } = new();
            public long LoadedAtMs { get; set; }
            public IReadOnlyList<PublicIndicatorPoint> Points { get; set; } = Array.Empty<PublicIndicatorPoint>();
        }

        private readonly record struct PublicIndicatorPoint(
            long SourceTs,
            IReadOnlyDictionary<string, double> Fields);
    }
}
