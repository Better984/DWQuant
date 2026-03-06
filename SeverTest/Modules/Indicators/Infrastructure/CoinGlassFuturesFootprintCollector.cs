using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 合约足迹图采集器。
    /// 当前默认采集 Binance 的 BTC / ETH 15 分钟级别数据。
    /// </summary>
    public sealed class CoinGlassFuturesFootprintCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassFuturesFootprintCollector> _logger;

        public CoinGlassFuturesFootprintCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassFuturesFootprintCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.futures_footprint.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.futures_footprint", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var asset = ResolveAsset(scopeKey);
            var exchange = ResolveScopeValue(scopeKey, "exchange", _coinGlassOptions.FuturesFootprintDefaultExchange);
            var interval = ResolveInterval(ResolveScopeValue(scopeKey, "interval", _coinGlassOptions.FuturesFootprintDefaultInterval));
            var symbol = ResolveSymbol(asset);
            var limit = Math.Max(1, _coinGlassOptions.FuturesFootprintSeriesLimit);

            _logger.LogInformation(
                "[coinglass][合约足迹图] 开始拉取合约足迹图: asset={Asset}, exchange={Exchange}, symbol={Symbol}, interval={Interval}, limit={Limit}, scopeKey={ScopeKey}",
                asset,
                exchange,
                symbol,
                interval,
                limit,
                scopeKey);

            using var response = await _coinGlassClient
                .GetFuturesFootprintHistoryAsync(exchange, symbol, interval, limit, null, null, ct)
                .ConfigureAwait(false);

            var ordered = ParseResponse(response.RootElement)
                .OrderBy(candle => candle.Timestamp)
                .ToList();

            if (ordered.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 合约足迹图接口返回为空，无法生成指标数据");
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
                value = latest.NetDeltaUsd,
                latestNetDeltaUsd = latest.NetDeltaUsd,
                latestBuyUsd = latest.BuyUsd,
                latestSellUsd = latest.SellUsd,
                latestBuyVolume = latest.BuyVolume,
                latestSellVolume = latest.SellVolume,
                latestTotalTradeCount = latest.TotalTradeCount,
                latestPriceLow = latest.PriceLow,
                latestPriceHigh = latest.PriceHigh,
                sourceTs = latest.Timestamp,
                series = ordered.Select(candle => new
                {
                    asset,
                    exchange,
                    symbol,
                    interval,
                    ts = candle.Timestamp,
                    netDeltaUsd = candle.NetDeltaUsd,
                    buyUsd = candle.BuyUsd,
                    sellUsd = candle.SellUsd,
                    buyVolume = candle.BuyVolume,
                    sellVolume = candle.SellVolume,
                    totalTradeCount = candle.TotalTradeCount,
                    priceLow = candle.PriceLow,
                    priceHigh = candle.PriceHigh
                }).ToList(),
                latestBins = latest.Bins.Select(bin => new
                {
                    priceFrom = bin.PriceFrom,
                    priceTo = bin.PriceTo,
                    buyVolume = bin.BuyVolume,
                    sellVolume = bin.SellVolume,
                    buyUsd = bin.BuyUsd,
                    sellUsd = bin.SellUsd,
                    deltaUsd = bin.DeltaUsd,
                    buyTradeCount = bin.BuyTradeCount,
                    sellTradeCount = bin.SellTradeCount
                }).ToList()
            };

            var history = ordered
                .Select(candle => new IndicatorHistoryPoint
                {
                    SourceTs = candle.Timestamp,
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        asset,
                        exchange,
                        symbol,
                        interval,
                        value = candle.NetDeltaUsd,
                        netDeltaUsd = candle.NetDeltaUsd,
                        buyUsd = candle.BuyUsd,
                        sellUsd = candle.SellUsd,
                        buyVolume = candle.BuyVolume,
                        sellVolume = candle.SellVolume,
                        totalTradeCount = candle.TotalTradeCount,
                        priceLow = candle.PriceLow,
                        priceHigh = candle.PriceHigh,
                        bins = candle.Bins.Select(bin => new
                        {
                            priceFrom = bin.PriceFrom,
                            priceTo = bin.PriceTo,
                            buyVolume = bin.BuyVolume,
                            sellVolume = bin.SellVolume,
                            buyUsd = bin.BuyUsd,
                            sellUsd = bin.SellUsd,
                            deltaUsd = bin.DeltaUsd,
                            buyTradeCount = bin.BuyTradeCount,
                            sellTradeCount = bin.SellTradeCount
                        }).ToList()
                    })
                })
                .ToList();

            _logger.LogInformation(
                "[coinglass][合约足迹图] 拉取完成: asset={Asset}, exchange={Exchange}, symbol={Symbol}, interval={Interval}, count={Count}, latestNetDeltaUsd={LatestNetDeltaUsd}, latestTs={LatestTs}",
                asset,
                exchange,
                symbol,
                interval,
                ordered.Count,
                latest.NetDeltaUsd,
                latest.Timestamp);

            return new IndicatorCollectResult
            {
                SourceTs = latest.Timestamp,
                PayloadJson = ProtocolJson.Serialize(latestPayload),
                History = history
            };
        }

        private static IReadOnlyList<FootprintCandlePoint> ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "合约足迹图");

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
                throw new InvalidOperationException("CoinGlass 合约足迹图接口返回结构异常，未找到 data 数组");
            }

            var candles = new List<FootprintCandlePoint>();
            foreach (var item in dataRoot.EnumerateArray())
            {
                if (TryParseCandle(item, out var candle))
                {
                    candles.Add(candle);
                }
            }

            if (candles.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 合约足迹图接口返回结构无法解析有效 K 线");
            }

            return candles
                .GroupBy(candle => candle.Timestamp)
                .Select(group => group.Last())
                .ToList();
        }

        private static bool TryParseCandle(JsonElement item, out FootprintCandlePoint candle)
        {
            candle = default;

            if (item.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            long? timestamp = null;
            JsonElement binsRoot = default;
            var hasBinsRoot = false;

            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().ToList();
                if (values.Count < 2)
                {
                    return false;
                }

                timestamp = CoinGlassCollectorJsonHelper.TryReadLong(values[0]);
                binsRoot = values[1];
                hasBinsRoot = true;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                timestamp = CoinGlassCollectorJsonHelper.TryReadLong(item, "time", "timestamp", "ts");
                if (item.TryGetProperty("bins", out var binsElement) ||
                    item.TryGetProperty("items", out binsElement) ||
                    item.TryGetProperty("data", out binsElement))
                {
                    binsRoot = binsElement;
                    hasBinsRoot = true;
                }
            }

            if (!timestamp.HasValue || !hasBinsRoot || binsRoot.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var bins = new List<FootprintBinPoint>();
            foreach (var rawBin in binsRoot.EnumerateArray())
            {
                if (TryParseBin(rawBin, out var bin))
                {
                    bins.Add(bin);
                }
            }

            if (bins.Count == 0)
            {
                return false;
            }

            bins = bins
                .OrderBy(bin => bin.PriceFrom)
                .ThenBy(bin => bin.PriceTo)
                .ToList();

            var priceLow = bins.Min(bin => Math.Min(bin.PriceFrom, bin.PriceTo));
            var priceHigh = bins.Max(bin => Math.Max(bin.PriceFrom, bin.PriceTo));
            var buyUsd = bins.Sum(bin => bin.BuyUsd);
            var sellUsd = bins.Sum(bin => bin.SellUsd);
            var buyVolume = bins.Sum(bin => bin.BuyVolume);
            var sellVolume = bins.Sum(bin => bin.SellVolume);
            var totalTradeCount = bins.Sum(bin => bin.BuyTradeCount + bin.SellTradeCount);

            candle = new FootprintCandlePoint(
                Timestamp: timestamp.Value,
                PriceLow: priceLow,
                PriceHigh: priceHigh,
                BuyUsd: buyUsd,
                SellUsd: sellUsd,
                BuyVolume: buyVolume,
                SellVolume: sellVolume,
                TotalTradeCount: totalTradeCount,
                Bins: bins);
            return true;
        }

        private static bool TryParseBin(JsonElement item, out FootprintBinPoint bin)
        {
            bin = default;

            decimal? priceFrom = null;
            decimal? priceTo = null;
            decimal? buyVolume = null;
            decimal? sellVolume = null;
            decimal? buyUsd = null;
            decimal? sellUsd = null;
            long buyTradeCount = 0;
            long sellTradeCount = 0;

            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().ToList();
                if (values.Count < 6)
                {
                    return false;
                }

                priceFrom = CoinGlassCollectorJsonHelper.TryReadDecimal(values[0]);
                priceTo = CoinGlassCollectorJsonHelper.TryReadDecimal(values[1]);
                buyVolume = CoinGlassCollectorJsonHelper.TryReadDecimal(values[2]);
                sellVolume = CoinGlassCollectorJsonHelper.TryReadDecimal(values[3]);
                buyUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(values[4]);
                sellUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(values[5]);
                buyTradeCount = values.Count > 8
                    ? CoinGlassCollectorJsonHelper.TryReadLong(values[8]) ?? 0
                    : 0;
                sellTradeCount = values.Count > 9
                    ? CoinGlassCollectorJsonHelper.TryReadLong(values[9]) ?? 0
                    : 0;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                priceFrom = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "priceFrom", "price_from", "fromPrice", "from_price");
                priceTo = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "priceTo", "price_to", "toPrice", "to_price");
                buyVolume = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "buyVolume", "buy_volume");
                sellVolume = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "sellVolume", "sell_volume");
                buyUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "buyUsd", "buy_usd");
                sellUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "sellUsd", "sell_usd");
                buyTradeCount = CoinGlassCollectorJsonHelper.TryReadLong(item, "buyTradeCount", "buy_trade_count") ?? 0;
                sellTradeCount = CoinGlassCollectorJsonHelper.TryReadLong(item, "sellTradeCount", "sell_trade_count") ?? 0;
            }

            if (!priceFrom.HasValue ||
                !priceTo.HasValue ||
                !buyVolume.HasValue ||
                !sellVolume.HasValue ||
                !buyUsd.HasValue ||
                !sellUsd.HasValue)
            {
                return false;
            }

            bin = new FootprintBinPoint(
                PriceFrom: priceFrom.Value,
                PriceTo: priceTo.Value,
                BuyVolume: buyVolume.Value,
                SellVolume: sellVolume.Value,
                BuyUsd: buyUsd.Value,
                SellUsd: sellUsd.Value,
                BuyTradeCount: buyTradeCount,
                SellTradeCount: sellTradeCount);
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
                var unsupported => throw new InvalidOperationException($"暂不支持的合约足迹图资产: {unsupported}")
            };
        }

        private static string ResolveSymbol(string asset)
        {
            return asset switch
            {
                "BTC" => "BTCUSDT",
                "ETH" => "ETHUSDT",
                _ => throw new InvalidOperationException($"暂不支持的合约足迹图交易对: {asset}")
            };
        }

        private static string ResolveInterval(string interval)
        {
            return (interval ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "" => "15m",
                "15m" => "15m",
                var unsupported => throw new InvalidOperationException($"当前仅支持 15 分钟级别合约足迹图，收到 interval={unsupported}")
            };
        }

        private readonly record struct FootprintBinPoint(
            decimal PriceFrom,
            decimal PriceTo,
            decimal BuyVolume,
            decimal SellVolume,
            decimal BuyUsd,
            decimal SellUsd,
            long BuyTradeCount,
            long SellTradeCount)
        {
            public decimal DeltaUsd => BuyUsd - SellUsd;
        }

        private readonly record struct FootprintCandlePoint(
            long Timestamp,
            decimal PriceLow,
            decimal PriceHigh,
            decimal BuyUsd,
            decimal SellUsd,
            decimal BuyVolume,
            decimal SellVolume,
            long TotalTradeCount,
            IReadOnlyList<FootprintBinPoint> Bins)
        {
            public decimal NetDeltaUsd => BuyUsd - SellUsd;
        }
    }
}
