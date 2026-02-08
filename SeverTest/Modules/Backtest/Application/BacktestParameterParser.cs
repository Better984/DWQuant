using System;
using System.Collections.Generic;
using System.Linq;
using ServerTest.Models;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.MarketData.Domain;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测参数解析与验证（从 BacktestRunner 拆分）
    /// </summary>
    internal static class BacktestParameterParser
    {
        private static readonly string[] SupportedEquityGranularities = { "1m", "15m", "1h", "4h", "1d", "3d", "7d" };
        private static readonly Dictionary<string, long> EquityGranularityToMs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 60_000L,
            ["15m"] = 15 * 60_000L,
            ["1h"] = 60 * 60_000L,
            ["4h"] = 4 * 60 * 60_000L,
            ["1d"] = 24 * 60 * 60_000L,
            ["3d"] = 3 * 24 * 60 * 60_000L,
            ["7d"] = 7 * 24 * 60 * 60_000L
        };

        /// <summary>
        /// 解析并验证回测请求参数，返回回测参数上下文。
        /// </summary>
        public static BacktestParameterContext Parse(
            BacktestRunRequest request,
            Models.Strategy.StrategyConfig config,
            int maxQueryBars)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var trade = config.Trade ?? throw new InvalidOperationException("策略配置缺少 Trade 信息");

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(
                !string.IsNullOrWhiteSpace(request.Exchange) ? request.Exchange : trade.Exchange);
            if (string.IsNullOrWhiteSpace(exchange))
                throw new InvalidOperationException("交易所不能为空");

            var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(
                !string.IsNullOrWhiteSpace(request.Timeframe)
                    ? request.Timeframe
                    : MarketDataKeyNormalizer.TimeframeFromSeconds(trade.TimeframeSec));
            if (string.IsNullOrWhiteSpace(timeframe))
                throw new InvalidOperationException("策略周期不能为空");

            var timeframeMs = MarketDataConfig.TimeframeToMs(timeframe);
            var symbols = NormalizeSymbols(request.Symbols, trade.Symbol);
            if (symbols.Count == 0)
                throw new InvalidOperationException("回测标的不能为空");

            var (startTime, endTime) = ParseRange(request.StartTime, request.EndTime);
            var useRange = startTime.HasValue || endTime.HasValue;
            if (useRange && (!startTime.HasValue || !endTime.HasValue))
                throw new InvalidOperationException("起止时间需同时提供，或改用 BarCount");

            var barCount = ResolveBarCount(useRange, request.BarCount, maxQueryBars);
            var output = request.Output ?? new BacktestOutputOptions();
            var (equityCurveGranularity, equityCurveGranularityMs) = ResolveEquityCurveGranularity(output.EquityCurveGranularity);
            var runtimeConfig = request.Runtime ?? config.Runtime;

            return new BacktestParameterContext
            {
                Exchange = exchange,
                Timeframe = timeframe,
                TimeframeMs = timeframeMs,
                Symbols = symbols,
                StartTime = startTime,
                EndTime = endTime,
                UseRange = useRange,
                BarCount = barCount,
                Output = output,
                EquityCurveGranularity = equityCurveGranularity,
                EquityCurveGranularityMs = equityCurveGranularityMs,
                RuntimeConfig = runtimeConfig
            };
        }

        public static List<string> NormalizeSymbols(IReadOnlyList<string>? symbols, string fallback)
        {
            var list = new List<string>();
            if (symbols != null && symbols.Count > 0)
            {
                list.AddRange(symbols);
            }
            else if (!string.IsNullOrWhiteSpace(fallback))
            {
                list.Add(fallback);
            }

            return list
                .Select(MarketDataKeyNormalizer.NormalizeSymbol)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static (DateTimeOffset? Start, DateTimeOffset? End) ParseRange(string? startRaw, string? endRaw)
        {
            var start = ParseDateTime(startRaw, "开始时间");
            var end = ParseDateTime(endRaw, "结束时间");

            if (start.HasValue && end.HasValue && start.Value > end.Value)
                throw new InvalidOperationException("开始时间不能晚于结束时间");

            return (start, end);
        }

        public static DateTimeOffset? ParseDateTime(string? raw, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!DateTime.TryParse(raw, out var parsed))
                throw new InvalidOperationException($"{fieldName}格式错误，请使用 yyyy-MM-dd HH:mm:ss");

            if (parsed.Kind == DateTimeKind.Unspecified)
                parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);

            return new DateTimeOffset(parsed);
        }

        public static int ResolveBarCount(bool useRange, int? barCount, int maxQueryBars)
        {
            const int DefaultBarCount = 1000;

            if (useRange) return Math.Max(1, barCount ?? 0);

            var max = maxQueryBars > 0 ? maxQueryBars : DefaultBarCount;
            var fallback = Math.Min(DefaultBarCount, max);
            var count = barCount.HasValue && barCount.Value > 0 ? barCount.Value : fallback;
            return Math.Max(1, count);
        }

        public static (string Granularity, long IntervalMs) ResolveEquityCurveGranularity(string? rawGranularity)
        {
            var normalized = string.IsNullOrWhiteSpace(rawGranularity)
                ? "1m"
                : rawGranularity.Trim().ToLowerInvariant();

            if (EquityGranularityToMs.TryGetValue(normalized, out var intervalMs))
                return (normalized, intervalMs);

            throw new InvalidOperationException(
                $"资金曲线颗粒度不支持: {rawGranularity}，仅支持 {string.Join("/", SupportedEquityGranularities)}");
        }
    }

    /// <summary>
    /// 回测参数解析后的上下文对象
    /// </summary>
    internal sealed class BacktestParameterContext
    {
        public string Exchange { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long TimeframeMs { get; set; }
        public List<string> Symbols { get; set; } = new();
        public DateTimeOffset? StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
        public bool UseRange { get; set; }
        public int BarCount { get; set; }
        public BacktestOutputOptions Output { get; set; } = new();
        public string EquityCurveGranularity { get; set; } = "1m";
        public long EquityCurveGranularityMs { get; set; }
        public Models.Strategy.StrategyRuntimeConfig? RuntimeConfig { get; set; }
    }
}
