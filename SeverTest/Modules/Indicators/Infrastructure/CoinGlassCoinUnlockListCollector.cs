using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 代币解锁列表采集器。
    /// </summary>
    public sealed class CoinGlassCoinUnlockListCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassCoinUnlockListCollector> _logger;

        public CoinGlassCoinUnlockListCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassCoinUnlockListCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.coin_unlock_list.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.coin_unlock_list", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            _logger.LogInformation("[coinglass][代币解锁列表] 开始拉取代币解锁列表: scopeKey={ScopeKey}", scopeKey);

            using var response = await _coinGlassClient
                .GetCoinUnlockListAsync(ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement);
            var orderedItems = parsed.Items
                .OrderBy(item => item.NextUnlockTime > 0 ? 0 : 1)
                .ThenBy(item => item.NextUnlockTime > 0 ? item.NextUnlockTime : long.MaxValue)
                .ThenByDescending(item => item.NextUnlockValue ?? item.MarketCap ?? 0m)
                .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 代币解锁列表接口返回为空，无法生成指标数据");
            }

            var displayItems = orderedItems
                .Take(Math.Max(1, _coinGlassOptions.CoinUnlockListTopCount))
                .ToList();
            var totalMarketCap = orderedItems.Sum(item => item.MarketCap ?? 0m);
            var totalNextUnlockValue = orderedItems.Sum(item => item.NextUnlockValue ?? 0m);
            var nextUnlockItem = orderedItems
                .Where(item => item.NextUnlockTime > 0)
                .OrderBy(item => item.NextUnlockTime)
                .ThenByDescending(item => item.NextUnlockValue ?? 0m)
                .FirstOrDefault();

            var payload = new
            {
                value = totalNextUnlockValue,
                sourceTs = parsed.SourceTs,
                totalCoinCount = orderedItems.Count,
                totalMarketCap,
                totalNextUnlockValue,
                nextUnlockSymbol = nextUnlockItem.Symbol,
                nextUnlockTime = nextUnlockItem.NextUnlockTime > 0 ? nextUnlockItem.NextUnlockTime : (long?)null,
                nextUnlockValue = nextUnlockItem.NextUnlockValue,
                items = displayItems.Select(item => new
                {
                    symbol = item.Symbol,
                    name = item.Name,
                    iconUrl = item.IconUrl,
                    price = item.Price,
                    priceChange24h = item.PriceChange24h,
                    marketCap = item.MarketCap,
                    unlockedSupply = item.UnlockedSupply,
                    lockedSupply = item.LockedSupply,
                    unlockedPercent = item.UnlockedPercent,
                    lockedPercent = item.LockedPercent,
                    nextUnlockTime = item.NextUnlockTime > 0 ? item.NextUnlockTime : (long?)null,
                    nextUnlockAmount = item.NextUnlockAmount,
                    nextUnlockPercent = item.NextUnlockPercent,
                    nextUnlockValue = item.NextUnlockValue,
                    updateTime = item.UpdateTime > 0 ? item.UpdateTime : (long?)null
                }).ToList()
            };

            _logger.LogInformation(
                "[coinglass][代币解锁列表] 采集完成: count={Count}, totalNextUnlockValue={TotalNextUnlockValue}, nextUnlockSymbol={NextUnlockSymbol}, sourceTs={SourceTs}",
                orderedItems.Count,
                totalNextUnlockValue,
                nextUnlockItem.Symbol,
                parsed.SourceTs);

            var payloadJson = ProtocolJson.Serialize(payload);
            return new IndicatorCollectResult
            {
                SourceTs = parsed.SourceTs,
                PayloadJson = payloadJson,
                History = new[]
                {
                    new IndicatorHistoryPoint
                    {
                        SourceTs = parsed.SourceTs,
                        PayloadJson = payloadJson
                    }
                }
            };
        }

        private static ParsedCoinUnlockListResponse ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "代币解锁列表");

            var dataRoot = ResolveDataArray(root);
            var items = new List<CoinUnlockItem>();
            foreach (var item in dataRoot.EnumerateArray())
            {
                var parsedItem = TryParseItem(item);
                if (parsedItem.HasValue)
                {
                    items.Add(parsedItem.Value);
                }
            }

            if (items.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 代币解锁列表接口返回结构无法解析有效数据");
            }

            var sourceTs = items
                .Select(item => Math.Max(item.UpdateTime, item.NextUnlockTime))
                .DefaultIfEmpty(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Max();

            return new ParsedCoinUnlockListResponse(sourceTs, items);
        }

        private static JsonElement ResolveDataArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("CoinGlass 代币解锁列表接口返回结构异常");
            }

            var directArray = CoinGlassCollectorJsonHelper.TryReadArray(root, "data", "list", "items", "coins", "rows");
            if (directArray.HasValue)
            {
                return directArray.Value;
            }

            var dataObject = CoinGlassCollectorJsonHelper.TryReadObject(root, "data", "result");
            if (dataObject.HasValue)
            {
                var nestedArray = CoinGlassCollectorJsonHelper.TryReadArray(dataObject.Value, "list", "items", "coins", "rows", "data");
                if (nestedArray.HasValue)
                {
                    return nestedArray.Value;
                }
            }

            throw new InvalidOperationException("CoinGlass 代币解锁列表接口返回结构异常，未找到列表数组");
        }

        private static CoinUnlockItem? TryParseItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var symbol = CoinGlassCollectorJsonHelper.TryReadString(item, "symbol", "coin", "coinSymbol", "tokenSymbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var unlockedSupply = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockedSupply", "unlocked_supply", "circulatingSupply", "circulating_supply");
            var lockedSupply = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "lockedSupply", "locked_supply");
            var totalSupply = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "totalSupply", "total_supply");
            if (!lockedSupply.HasValue && totalSupply.HasValue && unlockedSupply.HasValue)
            {
                lockedSupply = Math.Max(0m, totalSupply.Value - unlockedSupply.Value);
            }

            var unlockedPercent = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockedPercent", "unlocked_percent", "circulatingPercent", "circulating_percent");
            var lockedPercent = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "lockedPercent", "locked_percent");
            var derivedTotalSupply = totalSupply ?? ((unlockedSupply ?? 0m) + (lockedSupply ?? 0m));
            if (!unlockedPercent.HasValue && unlockedSupply.HasValue && derivedTotalSupply > 0m)
            {
                unlockedPercent = unlockedSupply.Value / derivedTotalSupply * 100m;
            }

            if (!lockedPercent.HasValue && lockedSupply.HasValue && derivedTotalSupply > 0m)
            {
                lockedPercent = lockedSupply.Value / derivedTotalSupply * 100m;
            }

            if (!lockedPercent.HasValue && unlockedPercent.HasValue)
            {
                lockedPercent = Math.Max(0m, 100m - unlockedPercent.Value);
            }

            if (!unlockedPercent.HasValue && lockedPercent.HasValue)
            {
                unlockedPercent = Math.Max(0m, 100m - lockedPercent.Value);
            }

            var price = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "price", "currentPrice", "current_price");
            var marketCap = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "marketCap", "market_cap");
            if (!marketCap.HasValue && unlockedSupply.HasValue && price.HasValue)
            {
                marketCap = unlockedSupply.Value * price.Value;
            }

            var nextUnlockAmount = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "nextUnlockAmount", "next_unlock_amount", "nextUnlockSupply", "next_unlock_supply");
            var nextUnlockPercent = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "nextUnlockPercent", "next_unlock_percent");
            var nextUnlockValue = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "nextUnlockValue", "next_unlock_value", "nextUnlockUsd", "next_unlock_usd");
            if (!nextUnlockValue.HasValue && nextUnlockAmount.HasValue && price.HasValue)
            {
                nextUnlockValue = nextUnlockAmount.Value * price.Value;
            }

            return new CoinUnlockItem(
                Symbol: symbol.Trim().ToUpperInvariant(),
                Name: CoinGlassCollectorJsonHelper.TryReadString(item, "name", "coinName", "coin_name", "fullName", "tokenName")?.Trim(),
                IconUrl: CoinGlassCollectorJsonHelper.TryReadString(item, "iconUrl", "icon_url", "icon", "image", "logo"),
                Price: price,
                PriceChange24h: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "priceChange24h", "price_change_24h", "change24h", "change_24h"),
                MarketCap: marketCap,
                UnlockedSupply: unlockedSupply,
                LockedSupply: lockedSupply,
                UnlockedPercent: unlockedPercent,
                LockedPercent: lockedPercent,
                NextUnlockTime: CoinGlassCollectorJsonHelper.TryReadLong(item, "nextUnlockTime", "next_unlock_time", "nextUnlockDate", "next_unlock_date") ?? 0L,
                NextUnlockAmount: nextUnlockAmount,
                NextUnlockPercent: nextUnlockPercent,
                NextUnlockValue: nextUnlockValue,
                UpdateTime: CoinGlassCollectorJsonHelper.TryReadLong(item, "updateTime", "update_time", "timestamp") ?? 0L);
        }

        private sealed record ParsedCoinUnlockListResponse(
            long SourceTs,
            IReadOnlyList<CoinUnlockItem> Items);

        private readonly record struct CoinUnlockItem(
            string Symbol,
            string? Name,
            string? IconUrl,
            decimal? Price,
            decimal? PriceChange24h,
            decimal? MarketCap,
            decimal? UnlockedSupply,
            decimal? LockedSupply,
            decimal? UnlockedPercent,
            decimal? LockedPercent,
            long NextUnlockTime,
            decimal? NextUnlockAmount,
            decimal? NextUnlockPercent,
            decimal? NextUnlockValue,
            long UpdateTime);
    }
}
