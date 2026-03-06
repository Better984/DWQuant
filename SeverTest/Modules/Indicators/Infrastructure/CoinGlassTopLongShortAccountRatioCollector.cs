using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 大户账户数多空比采集器。
    /// 当前默认采集 Binance 的 BTC / ETH 15 分钟级别数据。
    /// </summary>
    public sealed class CoinGlassTopLongShortAccountRatioCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassTopLongShortAccountRatioCollector> _logger;

        public CoinGlassTopLongShortAccountRatioCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassTopLongShortAccountRatioCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.top_long_short_account_ratio.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.top_long_short_account_ratio", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var asset = ResolveAsset(scopeKey);
            var exchange = ResolveScopeValue(scopeKey, "exchange", _coinGlassOptions.TopLongShortAccountRatioDefaultExchange);
            var interval = ResolveInterval(ResolveScopeValue(scopeKey, "interval", _coinGlassOptions.TopLongShortAccountRatioDefaultInterval));
            var symbol = ResolveSymbol(asset);
            var limit = Math.Max(1, _coinGlassOptions.TopLongShortAccountRatioSeriesLimit);

            _logger.LogInformation(
                "[coinglass][大户账户数多空比] 开始拉取大户账户数多空比: asset={Asset}, exchange={Exchange}, symbol={Symbol}, interval={Interval}, limit={Limit}, scopeKey={ScopeKey}",
                asset,
                exchange,
                symbol,
                interval,
                limit,
                scopeKey);

            using var response = await _coinGlassClient
                .GetTopLongShortAccountRatioHistoryAsync(exchange, symbol, interval, limit, null, null, ct)
                .ConfigureAwait(false);

            var ordered = ParseResponse(response.RootElement)
                .OrderBy(point => point.Timestamp)
                .ToList();

            if (ordered.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 大户账户数多空比接口返回为空，无法生成指标数据");
            }

            if (ordered.Count > limit)
            {
                ordered = ordered.Skip(ordered.Count - limit).ToList();
            }

            var latest = ordered[^1];
            var latestPayload = new
            {
                asset,
                exchange,
                symbol,
                interval,
                value = latest.Ratio,
                latestRatio = latest.Ratio,
                topAccountLongPercent = latest.LongPercent,
                topAccountShortPercent = latest.ShortPercent,
                sourceTs = latest.Timestamp,
                series = ordered.Select(point => new
                {
                    asset,
                    exchange,
                    symbol,
                    interval,
                    ts = point.Timestamp,
                    value = point.Ratio,
                    latestRatio = point.Ratio,
                    topAccountLongPercent = point.LongPercent,
                    topAccountShortPercent = point.ShortPercent
                }).ToList()
            };

            var history = ordered
                .Select(point => new IndicatorHistoryPoint
                {
                    SourceTs = point.Timestamp,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        asset,
                        exchange,
                        symbol,
                        interval,
                        value = point.Ratio,
                        latestRatio = point.Ratio,
                        topAccountLongPercent = point.LongPercent,
                        topAccountShortPercent = point.ShortPercent
                    })
                })
                .ToList();

            _logger.LogInformation(
                "[coinglass][大户账户数多空比] 拉取完成: asset={Asset}, exchange={Exchange}, symbol={Symbol}, interval={Interval}, count={Count}, latestRatio={LatestRatio}, latestTs={LatestTs}",
                asset,
                exchange,
                symbol,
                interval,
                ordered.Count,
                latest.Ratio,
                latest.Timestamp);

            return new IndicatorCollectResult
            {
                SourceTs = latest.Timestamp,
                PayloadJson = ProtocolJson.Serialize(latestPayload),
                History = history
            };
        }

        private static IReadOnlyList<TopLongShortAccountRatioPoint> ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "大户账户数多空比");

            JsonElement dataRoot;
            if (root.ValueKind == JsonValueKind.Array)
            {
                dataRoot = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("data", out var data) &&
                     data.ValueKind == JsonValueKind.Array)
            {
                dataRoot = data;
            }
            else
            {
                throw new InvalidOperationException("CoinGlass 大户账户数多空比接口返回结构异常，未找到 data 数组");
            }

            var points = new List<TopLongShortAccountRatioPoint>();
            foreach (var item in dataRoot.EnumerateArray())
            {
                if (TryParsePoint(item, out var point))
                {
                    points.Add(point);
                }
            }

            if (points.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 大户账户数多空比接口返回结构无法解析有效点位");
            }

            return points
                .GroupBy(point => point.Timestamp)
                .Select(group => group.Last())
                .ToList();
        }

        private static bool TryParsePoint(JsonElement item, out TopLongShortAccountRatioPoint point)
        {
            point = default;
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var timestamp = CoinGlassCollectorJsonHelper.TryReadLong(item, "time", "timestamp", "ts");
            var longPercent = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "top_account_long_percent", "topAccountLongPercent");
            var shortPercent = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "top_account_short_percent", "topAccountShortPercent");
            var ratio = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "top_account_long_short_ratio", "topAccountLongShortRatio", "value");

            if (!shortPercent.HasValue && longPercent.HasValue)
            {
                shortPercent = 100m - longPercent.Value;
            }

            if (!ratio.HasValue &&
                longPercent.HasValue &&
                shortPercent.HasValue &&
                shortPercent.Value > 0m)
            {
                ratio = decimal.Round(longPercent.Value / shortPercent.Value, 6);
            }

            if (!timestamp.HasValue || !longPercent.HasValue || !shortPercent.HasValue || !ratio.HasValue)
            {
                return false;
            }

            point = new TopLongShortAccountRatioPoint(
                timestamp.Value,
                longPercent.Value,
                shortPercent.Value,
                ratio.Value);
            return true;
        }

        private static string ResolveAsset(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey) ||
                string.Equals(scopeKey, "global", StringComparison.OrdinalIgnoreCase))
            {
                return "BTC";
            }

            foreach (var segment in scopeKey.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = segment.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2)
                {
                    continue;
                }

                if (string.Equals(kv[0], "asset", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv[0], "symbol", StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeAsset(kv[1]);
                }
            }

            return "BTC";
        }

        private static string ResolveScopeValue(string scopeKey, string key, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(scopeKey))
            {
                foreach (var segment in scopeKey.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var kv = segment.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length == 2 &&
                        string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(kv[1]))
                    {
                        return Uri.UnescapeDataString(kv[1]);
                    }
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private static string NormalizeAsset(string asset)
        {
            return (asset ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "" => "BTC",
                "BTC" or "BTCUSDT" or "BITCOIN" => "BTC",
                "ETH" or "ETHUSDT" or "ETHEREUM" => "ETH",
                var unsupported => throw new InvalidOperationException($"暂不支持的大户账户数多空比资产: {unsupported}")
            };
        }

        private static string ResolveSymbol(string asset)
        {
            return asset switch
            {
                "BTC" => "BTCUSDT",
                "ETH" => "ETHUSDT",
                _ => throw new InvalidOperationException($"暂不支持的大户账户数多空比交易对: {asset}")
            };
        }

        private static string ResolveInterval(string interval)
        {
            return (interval ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "" => "15m",
                "15m" => "15m",
                var unsupported => throw new InvalidOperationException($"当前仅支持 15 分钟级别大户账户数多空比，收到 interval={unsupported}")
            };
        }

        private readonly record struct TopLongShortAccountRatioPoint(
            long Timestamp,
            decimal LongPercent,
            decimal ShortPercent,
            decimal Ratio);
    }
}
