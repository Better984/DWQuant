using ServerTest.Modules.Monitoring.Domain;
using ServerTest.Modules.Monitoring.Infrastructure;

namespace ServerTest.Modules.Monitoring.Application
{
    public sealed class ProtocolPerformanceService
    {
        private const int SlowServerThresholdMs = 1000;
        private const int SlowClientThresholdMs = 1500;
        private const int MinAnalyzeSamples = 5;
        private const double RelativeDeviationFactor = 2d;
        private const double SlowRateThreshold = 20d;

        private readonly ProtocolPerformanceStorageFeature _storageFeature;
        private readonly ProtocolPerformanceRepository _repository;

        public ProtocolPerformanceService(
            ProtocolPerformanceStorageFeature storageFeature,
            ProtocolPerformanceRepository repository)
        {
            _storageFeature = storageFeature ?? throw new ArgumentNullException(nameof(storageFeature));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public bool IsStorageEnabled => _storageFeature.IsEnabled;

        public async Task<IReadOnlyList<ProtocolPerformanceSummaryItem>> GetSummaryAsync(
            int hours,
            string? transport,
            int top,
            CancellationToken ct = default)
        {
            if (!_storageFeature.IsEnabled)
            {
                return Array.Empty<ProtocolPerformanceSummaryItem>();
            }

            var (windowStart, windowEnd) = BuildWindow(hours);
            await _repository.EnsureTableAsync(ct).ConfigureAwait(false);
            return await _repository.GetSummaryAsync(
                windowStart,
                windowEnd,
                NormalizeTransport(transport),
                Math.Clamp(top, 1, 100),
                SlowServerThresholdMs,
                SlowClientThresholdMs,
                ct).ConfigureAwait(false);
        }

        public async Task<ProtocolPerformanceAnalysisResult> AnalyzeAsync(
            int hours,
            string? transport,
            int top,
            CancellationToken ct = default)
        {
            var (windowStart, windowEnd) = BuildWindow(hours);
            var normalizedTransport = NormalizeTransport(transport);

            if (!_storageFeature.IsEnabled)
            {
                return new ProtocolPerformanceAnalysisResult
                {
                    StorageEnabled = false,
                    Message = ProtocolPerformanceStorageFeature.DisabledMessage,
                    GeneratedAt = DateTime.UtcNow,
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    Items = Array.Empty<ProtocolPerformanceAnalysisItem>()
                };
            }

            await _repository.EnsureTableAsync(ct).ConfigureAwait(false);

            var summary = await _repository.GetSummaryAsync(
                windowStart,
                windowEnd,
                normalizedTransport,
                Math.Clamp(top, 1, 100),
                SlowServerThresholdMs,
                SlowClientThresholdMs,
                ct).ConfigureAwait(false);
            var global = await _repository.GetGlobalStatsAsync(windowStart, windowEnd, normalizedTransport, ct).ConfigureAwait(false);

            var items = new List<ProtocolPerformanceAnalysisItem>();
            foreach (var entry in summary)
            {
                if (entry.TotalCount < MinAnalyzeSamples)
                {
                    continue;
                }

                var reasons = new List<string>();
                var severityScore = 0;

                if (entry.AvgServerElapsedMs.HasValue && entry.AvgServerElapsedMs.Value >= SlowServerThresholdMs)
                {
                    reasons.Add($"服务端平均耗时 {entry.AvgServerElapsedMs.Value:F2}ms，超过 {SlowServerThresholdMs}ms 阈值");
                    severityScore += 3;
                }

                if (entry.AvgClientElapsedMs.HasValue && entry.AvgClientElapsedMs.Value >= SlowClientThresholdMs)
                {
                    reasons.Add($"前端平均总耗时 {entry.AvgClientElapsedMs.Value:F2}ms，超过 {SlowClientThresholdMs}ms 阈值");
                    severityScore += 3;
                }

                if (entry.SlowRate >= SlowRateThreshold)
                {
                    reasons.Add($"慢请求占比 {entry.SlowRate:F2}% ，超过 {SlowRateThreshold}% 阈值");
                    severityScore += 2;
                }

                if (global.AvgServerElapsedMs.HasValue
                    && global.AvgServerElapsedMs.Value > 0
                    && entry.AvgServerElapsedMs.HasValue
                    && entry.AvgServerElapsedMs.Value >= global.AvgServerElapsedMs.Value * RelativeDeviationFactor)
                {
                    reasons.Add($"服务端平均耗时是全局均值的 {entry.AvgServerElapsedMs.Value / global.AvgServerElapsedMs.Value:F2} 倍");
                    severityScore += 2;
                }

                if (global.AvgClientElapsedMs.HasValue
                    && global.AvgClientElapsedMs.Value > 0
                    && entry.AvgClientElapsedMs.HasValue
                    && entry.AvgClientElapsedMs.Value >= global.AvgClientElapsedMs.Value * RelativeDeviationFactor)
                {
                    reasons.Add($"前端平均总耗时是全局均值的 {entry.AvgClientElapsedMs.Value / global.AvgClientElapsedMs.Value:F2} 倍");
                    severityScore += 2;
                }

                if (entry.MaxServerElapsedMs.HasValue && entry.MaxServerElapsedMs.Value >= SlowServerThresholdMs * 2)
                {
                    reasons.Add($"服务端峰值耗时达到 {entry.MaxServerElapsedMs.Value}ms，存在明显尖峰");
                    severityScore += 1;
                }

                if (entry.MaxClientElapsedMs.HasValue && entry.MaxClientElapsedMs.Value >= SlowClientThresholdMs * 2)
                {
                    reasons.Add($"前端峰值总耗时达到 {entry.MaxClientElapsedMs.Value}ms，存在明显尖峰");
                    severityScore += 1;
                }

                if (reasons.Count == 0)
                {
                    continue;
                }

                items.Add(new ProtocolPerformanceAnalysisItem
                {
                    ProtocolType = entry.ProtocolType,
                    Transport = entry.Transport,
                    Severity = severityScore >= 6 ? "高" : severityScore >= 3 ? "中" : "低",
                    TotalCount = entry.TotalCount,
                    AvgServerElapsedMs = entry.AvgServerElapsedMs,
                    MaxServerElapsedMs = entry.MaxServerElapsedMs,
                    AvgClientElapsedMs = entry.AvgClientElapsedMs,
                    MaxClientElapsedMs = entry.MaxClientElapsedMs,
                    AvgClientNetworkOverheadMs = entry.AvgClientNetworkOverheadMs,
                    SlowCount = entry.SlowCount,
                    SlowRate = entry.SlowRate,
                    Reasons = reasons
                });
            }

            return new ProtocolPerformanceAnalysisResult
            {
                StorageEnabled = true,
                GeneratedAt = DateTime.UtcNow,
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                GlobalAvgServerElapsedMs = global.AvgServerElapsedMs,
                GlobalAvgClientElapsedMs = global.AvgClientElapsedMs,
                Items = items
                    .OrderByDescending(item => item.Severity == "高")
                    .ThenByDescending(item => item.Severity == "中")
                    .ThenByDescending(item => item.AvgClientElapsedMs ?? item.AvgServerElapsedMs ?? 0d)
                    .ToArray()
            };
        }

        private static (DateTime WindowStart, DateTime WindowEnd) BuildWindow(int hours)
        {
            var normalizedHours = Math.Clamp(hours, 1, 24 * 30);
            var windowEnd = DateTime.UtcNow;
            return (windowEnd.AddHours(-normalizedHours), windowEnd);
        }

        private static string? NormalizeTransport(string? transport)
        {
            if (string.IsNullOrWhiteSpace(transport))
            {
                return null;
            }

            return ProtocolPerformanceTransport.Normalize(transport);
        }
    }
}
