using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 现货 ETF 净流入采集器（支持 BTC/ETH/SOL/XRP）。
    /// </summary>
    public sealed class CoinGlassEtfFlowCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassEtfFlowCollector> _logger;

        public CoinGlassEtfFlowCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassEtfFlowCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.etf_flow.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.etf_flow", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var limit = _coinGlassOptions.EtfFlowSeriesLimit;
            var asset = ResolveAsset(scopeKey);
            _logger.LogInformation("[coinglass][ETF净流入] 开始拉取 ETF 净流入指标: asset={Asset}, limit={Limit}, scopeKey={ScopeKey}", asset, limit, scopeKey);

            using var response = await _coinGlassClient
                .GetEtfFlowHistoryAsync(asset, limit, ct)
                .ConfigureAwait(false);

            var points = ExtractPoints(response.RootElement);
            _logger.LogInformation(
                "[coinglass][ETF净流入] ETF 净流入接口返回解析: 原始根类型={RootKind}, 解析得到点位数量={PointCount}",
                response.RootElement.ValueKind.ToString(),
                points.Count);

            if (points.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass ETF 净流入接口返回格式无法解析有效点位");
            }

            var ordered = points
                .OrderBy(point => point.Timestamp)
                .ToList();

            if (limit > 0 && ordered.Count > limit)
            {
                ordered = ordered.Skip(ordered.Count - limit).ToList();
            }

            var series = BuildSeriesWithSignals(ordered);
            var latest = series[^1];
            var latestPayload = new
            {
                asset,
                value = latest.Point.NetFlowUsd,
                netFlowUsd = latest.Point.NetFlowUsd,
                priceUsd = latest.Point.PriceUsd,
                sourceTs = latest.Point.Timestamp,
                signals = ToSignalPayload(latest.Signals),
                etfFlows = latest.Point.EtfFlows.Select(flow => new
                {
                    ticker = flow.Ticker,
                    flowUsd = flow.FlowUsd
                }).ToList(),
                series = series.Select(item => new
                {
                    asset,
                    ts = item.Point.Timestamp,
                    value = item.Point.NetFlowUsd,
                    netFlowUsd = item.Point.NetFlowUsd,
                    priceUsd = item.Point.PriceUsd,
                    signals = ToSignalPayload(item.Signals),
                    etfFlows = item.Point.EtfFlows.Select(flow => new
                    {
                        ticker = flow.Ticker,
                        flowUsd = flow.FlowUsd
                    }).ToList()
                }).ToList()
            };

            var history = series
                .Select(item => new IndicatorHistoryPoint
                {
                    SourceTs = item.Point.Timestamp,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        asset,
                        value = item.Point.NetFlowUsd,
                        netFlowUsd = item.Point.NetFlowUsd,
                        priceUsd = item.Point.PriceUsd,
                        signals = ToSignalPayload(item.Signals),
                        etfFlows = item.Point.EtfFlows.Select(flow => new
                        {
                            ticker = flow.Ticker,
                            flowUsd = flow.FlowUsd
                        }).ToList()
                    })
                })
                .ToList();

            _logger.LogInformation(
                "[coinglass][ETF净流入] ETF 净流入采集完成: asset={Asset}, count={Count}, latestFlowUsd={LatestFlowUsd}, latestTs={LatestTs}",
                asset,
                series.Count,
                latest.Point.NetFlowUsd,
                latest.Point.Timestamp);

            return new IndicatorCollectResult
            {
                SourceTs = latest.Point.Timestamp,
                PayloadJson = ProtocolJson.Serialize(latestPayload),
                History = history
            };
        }

        private static string ResolveAsset(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey) ||
                string.Equals(scopeKey, "global", StringComparison.OrdinalIgnoreCase))
            {
                return "BTC";
            }

            var segments = scopeKey.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                var kv = segment.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2 ||
                    !string.Equals(kv[0], "asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return NormalizeAsset(kv[1]);
            }

            return "BTC";
        }

        private static string NormalizeAsset(string asset)
        {
            return (asset ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "" => "BTC",
                "BTC" or "BITCOIN" => "BTC",
                "ETH" or "ETHEREUM" => "ETH",
                "SOL" or "SOLANA" => "SOL",
                "XRP" => "XRP",
                var unsupported => throw new InvalidOperationException($"暂不支持的 ETF 资产: {unsupported}")
            };
        }

        private static List<EtfFlowSeriesPoint> BuildSeriesWithSignals(IReadOnlyList<EtfFlowPoint> ordered)
        {
            var result = new List<EtfFlowSeriesPoint>(ordered.Count);
            var inflowStreakDays = 0;
            var outflowStreakDays = 0;
            var last3Flows = new Queue<decimal>(3);
            var last3PositiveCount = 0;
            var last7Flows = new Queue<decimal>(7);
            var netFlow7dSumUsd = 0m;
            var inflow7dUsd = 0m;
            var outflow7dAbsUsd = 0m;

            foreach (var point in ordered)
            {
                var flow = point.NetFlowUsd;
                var isNetInflow = flow > 0m;
                var isNetOutflow = flow < 0m;

                inflowStreakDays = isNetInflow ? inflowStreakDays + 1 : 0;
                outflowStreakDays = isNetOutflow ? outflowStreakDays + 1 : 0;

                if (last3Flows.Count == 3)
                {
                    var removed3 = last3Flows.Dequeue();
                    if (removed3 > 0m)
                    {
                        last3PositiveCount--;
                    }
                }

                last3Flows.Enqueue(flow);
                if (flow > 0m)
                {
                    last3PositiveCount++;
                }

                if (last7Flows.Count == 7)
                {
                    var removed7 = last7Flows.Dequeue();
                    netFlow7dSumUsd -= removed7;
                    if (removed7 > 0m)
                    {
                        inflow7dUsd -= removed7;
                    }
                    else if (removed7 < 0m)
                    {
                        outflow7dAbsUsd -= Math.Abs(removed7);
                    }
                }

                last7Flows.Enqueue(flow);
                netFlow7dSumUsd += flow;
                if (flow > 0m)
                {
                    inflow7dUsd += flow;
                }
                else if (flow < 0m)
                {
                    outflow7dAbsUsd += Math.Abs(flow);
                }

                var window3dReady = last3Flows.Count == 3;
                var window7dReady = last7Flows.Count == 7;
                var inflow7dDenominator = inflow7dUsd + outflow7dAbsUsd;
                var inflow7dRatio = inflow7dDenominator > 0m
                    ? inflow7dUsd / inflow7dDenominator
                    : 0m;

                var signals = new EtfDerivedSignals(
                    IsNetInflow: isNetInflow,
                    IsNetOutflow: isNetOutflow,
                    NetInflowStreakDays: inflowStreakDays,
                    NetOutflowStreakDays: outflowStreakDays,
                    Window3dReady: window3dReady,
                    NetInflow3dAllPositive: window3dReady && last3PositiveCount == 3,
                    Window7dReady: window7dReady,
                    NetFlow7dSumUsd: netFlow7dSumUsd,
                    NetFlow7dSumPositive: window7dReady && netFlow7dSumUsd > 0m,
                    Inflow7dUsd: inflow7dUsd,
                    Outflow7dAbsUsd: outflow7dAbsUsd,
                    Inflow7dRatio: inflow7dRatio);

                result.Add(new EtfFlowSeriesPoint(point, signals));
            }

            return result;
        }

        private static object ToSignalPayload(EtfDerivedSignals signals)
        {
            return new
            {
                isNetInflow = signals.IsNetInflow,
                isNetOutflow = signals.IsNetOutflow,
                netInflowStreakDays = signals.NetInflowStreakDays,
                netOutflowStreakDays = signals.NetOutflowStreakDays,
                window3dReady = signals.Window3dReady,
                netInflow3dAllPositive = signals.NetInflow3dAllPositive,
                window7dReady = signals.Window7dReady,
                netFlow7dSumUsd = signals.NetFlow7dSumUsd,
                netFlow7dSumPositive = signals.NetFlow7dSumPositive,
                inflow7dUsd = signals.Inflow7dUsd,
                outflow7dAbsUsd = signals.Outflow7dAbsUsd,
                inflow7dRatio = signals.Inflow7dRatio
            };
        }

        private static List<EtfFlowPoint> ExtractPoints(JsonElement root)
        {
            var points = new List<EtfFlowPoint>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            CollectPointCandidates(root, points, visited, depth: 0);

            return points
                .Where(point => point.Timestamp > 0)
                .GroupBy(point => point.Timestamp)
                .Select(group => group.Last())
                .ToList();
        }

        /// <summary>
        /// 递归扫描响应结构，兼容 data 包裹、数组直出与嵌套对象三种常见格式。
        /// </summary>
        private static void CollectPointCandidates(
            JsonElement element,
            List<EtfFlowPoint> output,
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

            if (TryParsePoint(element, out var current))
            {
                output.Add(current);
                return;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    continue;
                }

                var marker = $"{depth}:{property.Name}:{property.Value.ValueKind}";
                if (visited.Add(marker))
                {
                    CollectPointCandidates(property.Value, output, visited, depth + 1);
                }
            }
        }

        private static bool TryParsePoint(JsonElement item, out EtfFlowPoint point)
        {
            point = default;
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var timestamp = TryReadTimestamp(item, "timestamp")
                ?? TryReadTimestamp(item, "ts")
                ?? TryReadTimestamp(item, "time")
                ?? TryReadTimestamp(item, "date");
            var netFlowUsd = TryReadDecimal(item, "flow_usd")
                ?? TryReadDecimal(item, "netFlowUsd")
                ?? TryReadDecimal(item, "net_flow_usd")
                ?? TryReadDecimal(item, "flowUsd")
                ?? TryReadDecimal(item, "flow")
                ?? TryReadDecimal(item, "value");
            if (!timestamp.HasValue || !netFlowUsd.HasValue)
            {
                return false;
            }

            var priceUsd = TryReadDecimal(item, "price_usd")
                ?? TryReadDecimal(item, "priceUsd")
                ?? TryReadDecimal(item, "btc_price_usd")
                ?? TryReadDecimal(item, "price");

            var etfFlows = TryReadEtfFlows(item);
            point = new EtfFlowPoint(timestamp.Value, netFlowUsd.Value, priceUsd, etfFlows);
            return true;
        }

        private static IReadOnlyList<EtfTickerFlow> TryReadEtfFlows(JsonElement obj)
        {
            if (!TryGetArrayProperty(obj, out var array, "etf_flows", "etfFlows", "flows"))
            {
                return Array.Empty<EtfTickerFlow>();
            }

            var result = new List<EtfTickerFlow>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var ticker = TryReadString(item, "etf_ticker")
                    ?? TryReadString(item, "ticker")
                    ?? TryReadString(item, "symbol");
                if (string.IsNullOrWhiteSpace(ticker))
                {
                    continue;
                }

                var flowUsd = TryReadDecimal(item, "flow_usd")
                    ?? TryReadDecimal(item, "flowUsd")
                    ?? TryReadDecimal(item, "value");
                result.Add(new EtfTickerFlow(ticker.Trim(), flowUsd));
            }

            return result;
        }

        private static bool TryGetArrayProperty(JsonElement obj, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
                {
                    return true;
                }
            }

            value = default;
            return false;
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
                if (!string.IsNullOrWhiteSpace(text) && decimal.TryParse(text, out var parsed))
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

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static long NormalizeTimestamp(long raw)
        {
            // 小于 10^11 基本可判定为秒级时间戳。
            return raw < 100_000_000_000L ? raw * 1000 : raw;
        }

        private readonly record struct EtfFlowPoint(
            long Timestamp,
            decimal NetFlowUsd,
            decimal? PriceUsd,
            IReadOnlyList<EtfTickerFlow> EtfFlows);

        private readonly record struct EtfTickerFlow(string Ticker, decimal? FlowUsd);

        private readonly record struct EtfFlowSeriesPoint(EtfFlowPoint Point, EtfDerivedSignals Signals);

        private readonly record struct EtfDerivedSignals(
            bool IsNetInflow,
            bool IsNetOutflow,
            int NetInflowStreakDays,
            int NetOutflowStreakDays,
            bool Window3dReady,
            bool NetInflow3dAllPositive,
            bool Window7dReady,
            decimal NetFlow7dSumUsd,
            bool NetFlow7dSumPositive,
            decimal Inflow7dUsd,
            decimal Outflow7dAbsUsd,
            decimal Inflow7dRatio);
    }
}
