using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 代币解锁详情采集器。
    /// </summary>
    public sealed class CoinGlassCoinVestingCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassCoinVestingCollector> _logger;

        public CoinGlassCoinVestingCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassCoinVestingCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.coin_vesting.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.coin_vesting", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var symbol = ResolveScopeValue(scopeKey, "symbol", _coinGlassOptions.CoinVestingDefaultSymbol);
            _logger.LogInformation("[coinglass][代币解锁详情] 开始拉取代币解锁详情: symbol={Symbol}, scopeKey={ScopeKey}", symbol, scopeKey);

            using var response = await _coinGlassClient
                .GetCoinVestingAsync(symbol, ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(
                response.RootElement,
                symbol,
                _coinGlassOptions.CoinVestingAllocationLimit,
                _coinGlassOptions.CoinVestingScheduleLimit);

            var payload = new
            {
                value = parsed.NextUnlockValue ?? parsed.MarketCap ?? 0m,
                sourceTs = parsed.SourceTs,
                symbol = parsed.Symbol,
                name = parsed.Name,
                iconUrl = parsed.IconUrl,
                price = parsed.Price,
                priceChange24h = parsed.PriceChange24h,
                marketCap = parsed.MarketCap,
                circulatingSupply = parsed.CirculatingSupply,
                totalSupply = parsed.TotalSupply,
                unlockedSupply = parsed.UnlockedSupply,
                lockedSupply = parsed.LockedSupply,
                unlockedPercent = parsed.UnlockedPercent,
                lockedPercent = parsed.LockedPercent,
                nextUnlockTime = parsed.NextUnlockTime > 0 ? parsed.NextUnlockTime : (long?)null,
                nextUnlockAmount = parsed.NextUnlockAmount,
                nextUnlockPercent = parsed.NextUnlockPercent,
                nextUnlockValue = parsed.NextUnlockValue,
                allocationItems = parsed.AllocationItems.Select(item => new
                {
                    label = item.Label,
                    unlockedPercent = item.UnlockedPercent,
                    lockedPercent = item.LockedPercent,
                    unlockedAmount = item.UnlockedAmount,
                    lockedAmount = item.LockedAmount,
                    nextUnlockTime = item.NextUnlockTime > 0 ? item.NextUnlockTime : (long?)null,
                    nextUnlockAmount = item.NextUnlockAmount
                }).ToList(),
                scheduleItems = parsed.ScheduleItems.Select(item => new
                {
                    label = item.Label,
                    unlockTime = item.UnlockTime > 0 ? item.UnlockTime : (long?)null,
                    unlockAmount = item.UnlockAmount,
                    unlockPercent = item.UnlockPercent,
                    unlockValue = item.UnlockValue
                }).ToList()
            };

            _logger.LogInformation(
                "[coinglass][代币解锁详情] 采集完成: symbol={Symbol}, allocationCount={AllocationCount}, scheduleCount={ScheduleCount}, sourceTs={SourceTs}",
                parsed.Symbol,
                parsed.AllocationItems.Count,
                parsed.ScheduleItems.Count,
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

        private static ParsedCoinVestingResponse ParseResponse(
            JsonElement root,
            string preferredSymbol,
            int allocationLimit,
            int scheduleLimit)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "代币解锁详情");

            var dataRoot = ResolveDataObject(root);
            var candidates = CoinGlassCollectorJsonHelper
                .EnumerateCandidateObjects(dataRoot)
                .ToList();

            var symbol = ResolveString(candidates, "symbol", "coin", "coinSymbol", "tokenSymbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                symbol = preferredSymbol;
            }

            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new InvalidOperationException("CoinGlass 代币解锁详情接口缺少 symbol 字段");
            }

            var circulatingSupply = ResolveDecimal(candidates, "circulatingSupply", "circulating_supply", "unlockedSupply", "unlocked_supply");
            var totalSupply = ResolveDecimal(candidates, "totalSupply", "total_supply");
            var unlockedSupply = ResolveDecimal(candidates, "unlockedSupply", "unlocked_supply", "circulatingSupply", "circulating_supply");
            var lockedSupply = ResolveDecimal(candidates, "lockedSupply", "locked_supply");
            if (!lockedSupply.HasValue && totalSupply.HasValue && unlockedSupply.HasValue)
            {
                lockedSupply = Math.Max(0m, totalSupply.Value - unlockedSupply.Value);
            }

            if (!totalSupply.HasValue && unlockedSupply.HasValue && lockedSupply.HasValue)
            {
                totalSupply = unlockedSupply.Value + lockedSupply.Value;
            }

            var unlockedPercent = ResolveDecimal(candidates, "unlockedPercent", "unlocked_percent", "circulatingPercent", "circulating_percent");
            var lockedPercent = ResolveDecimal(candidates, "lockedPercent", "locked_percent");
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

            var price = ResolveDecimal(candidates, "price", "currentPrice", "current_price");
            var priceChange24h = ResolveDecimal(candidates, "priceChange24h", "price_change_24h", "change24h", "change_24h");
            var marketCap = ResolveDecimal(candidates, "marketCap", "market_cap");
            if (!marketCap.HasValue && circulatingSupply.HasValue && price.HasValue)
            {
                marketCap = circulatingSupply.Value * price.Value;
            }

            var nextUnlockTime = ResolveLong(candidates, "nextUnlockTime", "next_unlock_time", "nextUnlockDate", "next_unlock_date") ?? 0L;
            var nextUnlockAmount = ResolveDecimal(candidates, "nextUnlockAmount", "next_unlock_amount", "nextUnlockSupply", "next_unlock_supply");
            var nextUnlockPercent = ResolveDecimal(candidates, "nextUnlockPercent", "next_unlock_percent");
            var nextUnlockValue = ResolveDecimal(candidates, "nextUnlockValue", "next_unlock_value", "nextUnlockUsd", "next_unlock_usd");
            if (!nextUnlockValue.HasValue && nextUnlockAmount.HasValue && price.HasValue)
            {
                nextUnlockValue = nextUnlockAmount.Value * price.Value;
            }

            var allocationItems = ParseAllocationItems(dataRoot, allocationLimit);
            var scheduleItems = ParseScheduleItems(dataRoot, scheduleLimit, price);
            var sourceTs = new[]
            {
                ResolveLong(candidates, "updateTime", "update_time", "timestamp"),
                nextUnlockTime > 0 ? nextUnlockTime : null,
                scheduleItems.Select(item => item.UnlockTime > 0 ? item.UnlockTime : 0L).DefaultIfEmpty(0L).Max()
            }
            .Where(item => item.HasValue && item.Value > 0)
            .Select(item => item!.Value)
            .DefaultIfEmpty(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .Max();

            return new ParsedCoinVestingResponse(
                SourceTs: sourceTs,
                Symbol: symbol.Trim().ToUpperInvariant(),
                Name: ResolveString(candidates, "name", "coinName", "coin_name", "fullName", "tokenName")?.Trim(),
                IconUrl: ResolveString(candidates, "iconUrl", "icon_url", "icon", "image", "logo"),
                Price: price,
                PriceChange24h: priceChange24h,
                MarketCap: marketCap,
                CirculatingSupply: circulatingSupply,
                TotalSupply: totalSupply,
                UnlockedSupply: unlockedSupply,
                LockedSupply: lockedSupply,
                UnlockedPercent: unlockedPercent,
                LockedPercent: lockedPercent,
                NextUnlockTime: nextUnlockTime,
                NextUnlockAmount: nextUnlockAmount,
                NextUnlockPercent: nextUnlockPercent,
                NextUnlockValue: nextUnlockValue,
                AllocationItems: allocationItems,
                ScheduleItems: scheduleItems);
        }

        private static JsonElement ResolveDataObject(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                var directDataObject = CoinGlassCollectorJsonHelper.TryReadObject(root, "data", "result", "item");
                if (directDataObject.HasValue)
                {
                    return directDataObject.Value;
                }

                return root;
            }

            throw new InvalidOperationException("CoinGlass 代币解锁详情接口返回结构异常，未找到详情对象");
        }

        private static List<CoinVestingAllocationItem> ParseAllocationItems(JsonElement dataRoot, int limit)
        {
            var array = ResolveArray(dataRoot, "allocationItems", "allocation_items", "allocationList", "allocation_list", "allocations", "distributionList", "distribution_list");
            if (!array.HasValue)
            {
                return new List<CoinVestingAllocationItem>();
            }

            return array.Value
                .EnumerateArray()
                .Select(TryParseAllocationItem)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .OrderByDescending(item => (item.UnlockedPercent ?? 0m) + (item.LockedPercent ?? 0m))
                .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        private static List<CoinVestingScheduleItem> ParseScheduleItems(JsonElement dataRoot, int limit, decimal? price)
        {
            var array = ResolveArray(dataRoot, "scheduleItems", "schedule_items", "unlockSchedule", "unlock_schedule", "scheduleList", "schedule_list", "unlockList", "unlock_list", "unlockHistory", "unlock_history", "history");
            if (!array.HasValue)
            {
                return new List<CoinVestingScheduleItem>();
            }

            return array.Value
                .EnumerateArray()
                .Select(item => TryParseScheduleItem(item, price))
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .OrderBy(item => item.UnlockTime > 0 ? 0 : 1)
                .ThenBy(item => item.UnlockTime > 0 ? item.UnlockTime : long.MaxValue)
                .ThenByDescending(item => item.UnlockValue ?? 0m)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        private static CoinVestingAllocationItem? TryParseAllocationItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var label = CoinGlassCollectorJsonHelper.TryReadString(item, "label", "name", "category", "round", "groupName", "group_name");
            if (string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            return new CoinVestingAllocationItem(
                Label: label.Trim(),
                UnlockedPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockedPercent", "unlocked_percent"),
                LockedPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "lockedPercent", "locked_percent"),
                UnlockedAmount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockedAmount", "unlocked_amount", "circulatingSupply", "circulating_supply"),
                LockedAmount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "lockedAmount", "locked_amount"),
                NextUnlockTime: CoinGlassCollectorJsonHelper.TryReadLong(item, "nextUnlockTime", "next_unlock_time") ?? 0L,
                NextUnlockAmount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "nextUnlockAmount", "next_unlock_amount"));
        }

        private static CoinVestingScheduleItem? TryParseScheduleItem(JsonElement item, decimal? price)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var unlockTime = CoinGlassCollectorJsonHelper.TryReadLong(item, "unlockTime", "unlock_time", "timestamp", "time", "date") ?? 0L;
            var unlockAmount = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockAmount", "unlock_amount", "amount", "unlockSupply", "unlock_supply", "supply");
            var unlockPercent = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockPercent", "unlock_percent", "percent", "ratio");
            var unlockValue = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unlockValue", "unlock_value", "unlockUsd", "unlock_usd", "usdValue", "usd_value");
            if (!unlockValue.HasValue && unlockAmount.HasValue && price.HasValue)
            {
                unlockValue = unlockAmount.Value * price.Value;
            }

            if (unlockTime <= 0 && !unlockAmount.HasValue && !unlockPercent.HasValue && !unlockValue.HasValue)
            {
                return null;
            }

            return new CoinVestingScheduleItem(
                Label: CoinGlassCollectorJsonHelper.TryReadString(item, "label", "name", "round", "stage", "title"),
                UnlockTime: unlockTime,
                UnlockAmount: unlockAmount,
                UnlockPercent: unlockPercent,
                UnlockValue: unlockValue);
        }

        private static JsonElement? ResolveArray(JsonElement root, params string[] fieldNames)
        {
            foreach (var candidate in CoinGlassCollectorJsonHelper.EnumerateCandidateObjects(root))
            {
                var array = CoinGlassCollectorJsonHelper.TryReadArray(candidate, fieldNames);
                if (array.HasValue)
                {
                    return array.Value;
                }
            }

            return null;
        }

        private static string? ResolveString(IReadOnlyList<JsonElement> candidates, params string[] fieldNames)
        {
            foreach (var candidate in candidates)
            {
                var value = CoinGlassCollectorJsonHelper.TryReadString(candidate, fieldNames);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static decimal? ResolveDecimal(IReadOnlyList<JsonElement> candidates, params string[] fieldNames)
        {
            foreach (var candidate in candidates)
            {
                var value = CoinGlassCollectorJsonHelper.TryReadDecimal(candidate, fieldNames);
                if (value.HasValue)
                {
                    return value.Value;
                }
            }

            return null;
        }

        private static long? ResolveLong(IReadOnlyList<JsonElement> candidates, params string[] fieldNames)
        {
            foreach (var candidate in candidates)
            {
                var value = CoinGlassCollectorJsonHelper.TryReadLong(candidate, fieldNames);
                if (value.HasValue)
                {
                    return value.Value;
                }
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
                        return Uri.UnescapeDataString(segments[1]).Trim().ToUpperInvariant();
                    }
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim().ToUpperInvariant();
        }

        private sealed record ParsedCoinVestingResponse(
            long SourceTs,
            string Symbol,
            string? Name,
            string? IconUrl,
            decimal? Price,
            decimal? PriceChange24h,
            decimal? MarketCap,
            decimal? CirculatingSupply,
            decimal? TotalSupply,
            decimal? UnlockedSupply,
            decimal? LockedSupply,
            decimal? UnlockedPercent,
            decimal? LockedPercent,
            long NextUnlockTime,
            decimal? NextUnlockAmount,
            decimal? NextUnlockPercent,
            decimal? NextUnlockValue,
            IReadOnlyList<CoinVestingAllocationItem> AllocationItems,
            IReadOnlyList<CoinVestingScheduleItem> ScheduleItems);

        private readonly record struct CoinVestingAllocationItem(
            string Label,
            decimal? UnlockedPercent,
            decimal? LockedPercent,
            decimal? UnlockedAmount,
            decimal? LockedAmount,
            long NextUnlockTime,
            decimal? NextUnlockAmount);

        private readonly record struct CoinVestingScheduleItem(
            string? Label,
            long UnlockTime,
            decimal? UnlockAmount,
            decimal? UnlockPercent,
            decimal? UnlockValue);
    }
}
