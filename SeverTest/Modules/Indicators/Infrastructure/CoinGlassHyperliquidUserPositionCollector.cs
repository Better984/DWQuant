using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass Hyperliquid 用户持仓采集器。
    /// </summary>
    public sealed class CoinGlassHyperliquidUserPositionCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassHyperliquidUserPositionCollector> _logger;

        public CoinGlassHyperliquidUserPositionCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassHyperliquidUserPositionCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.hyperliquid_user_position.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.hyperliquid_user_position", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var userAddress = ResolveScopeValue(
                scopeKey,
                _coinGlassOptions.HyperliquidUserPositionDefaultUserAddress,
                "userAddress",
                "user_address");

            _logger.LogInformation(
                "[coinglass][Hyperliquid][用户持仓] 开始拉取用户持仓: userAddress={UserAddress}, scopeKey={ScopeKey}",
                userAddress,
                scopeKey);

            using var response = await _coinGlassClient
                .GetHyperliquidUserPositionAsync(userAddress, ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement);
            var orderedPositions = parsed.AssetPositions
                .OrderByDescending(item => Math.Abs(item.PositionValue))
                .ThenBy(item => item.Coin, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var summary = parsed.MarginSummary ?? parsed.CrossMarginSummary;
            if (summary == null)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 用户持仓接口缺少账户汇总信息");
            }

            var summaryValue = summary.Value;
            var sourceTs = parsed.UpdateTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var payload = new
            {
                value = summaryValue.AccountValue,
                userAddress,
                sourceTs,
                marginSummary = summaryValue,
                crossMarginSummary = parsed.CrossMarginSummary,
                crossMaintenanceMarginUsed = parsed.CrossMaintenanceMarginUsed,
                withdrawable = parsed.Withdrawable,
                accountValue = summaryValue.AccountValue,
                totalNotionalPosition = summaryValue.TotalNtlPos,
                totalMarginUsed = summaryValue.TotalMarginUsed,
                assetPositionCount = orderedPositions.Count,
                assetPositions = orderedPositions.Select(item => new
                {
                    type = item.Type,
                    coin = item.Coin,
                    size = item.Size,
                    leverageType = item.LeverageType,
                    leverageValue = item.LeverageValue,
                    entryPrice = item.EntryPrice,
                    positionValue = item.PositionValue,
                    unrealizedPnl = item.UnrealizedPnl,
                    returnOnEquity = item.ReturnOnEquity,
                    liquidationPrice = item.LiquidationPrice,
                    maxLeverage = item.MaxLeverage,
                    cumFundingAllTime = item.CumFundingAllTime,
                    cumFundingSinceOpen = item.CumFundingSinceOpen,
                    cumFundingSinceChange = item.CumFundingSinceChange
                }).ToList()
            };

            var payloadJson = ProtocolJson.Serialize(payload);
            return new IndicatorCollectResult
            {
                SourceTs = sourceTs,
                PayloadJson = payloadJson,
                History = new[]
                {
                    new IndicatorHistoryPoint
                    {
                        SourceTs = sourceTs,
                        PayloadJson = payloadJson
                    }
                }
            };
        }

        private static ParsedHyperliquidUserPositionResponse ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "Hyperliquid 用户持仓");

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var dataRoot) ||
                dataRoot.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 用户持仓接口返回结构异常，未找到 data 对象");
            }

            var marginSummary = TryParseMarginSummary(dataRoot, "margin_summary", "marginSummary");
            var crossMarginSummary = TryParseMarginSummary(dataRoot, "cross_margin_summary", "crossMarginSummary");
            var assetPositions = new List<HyperliquidUserAssetPosition>();
            if (dataRoot.TryGetProperty("asset_positions", out var positionsRoot) && positionsRoot.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in positionsRoot.EnumerateArray())
                {
                    var parsed = TryParseAssetPosition(item);
                    if (parsed.HasValue)
                    {
                        assetPositions.Add(parsed.Value);
                    }
                }
            }

            return new ParsedHyperliquidUserPositionResponse(
                marginSummary,
                crossMarginSummary,
                CoinGlassCollectorJsonHelper.TryReadDecimal(dataRoot, "cross_maintenance_margin_used", "crossMaintenanceMarginUsed"),
                CoinGlassCollectorJsonHelper.TryReadDecimal(dataRoot, "withdrawable"),
                assetPositions,
                CoinGlassCollectorJsonHelper.TryReadLong(dataRoot, "update_time", "updateTime"));
        }

        private static HyperliquidMarginSummary? TryParseMarginSummary(JsonElement root, params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                if (!root.TryGetProperty(fieldName, out var node) || node.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var accountValue = CoinGlassCollectorJsonHelper.TryReadDecimal(node, "account_value", "accountValue");
                var totalNtlPos = CoinGlassCollectorJsonHelper.TryReadDecimal(node, "total_ntl_pos", "totalNtlPos");
                var totalRawUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(node, "total_raw_usd", "totalRawUsd");
                var totalMarginUsed = CoinGlassCollectorJsonHelper.TryReadDecimal(node, "total_margin_used", "totalMarginUsed");
                if (!accountValue.HasValue)
                {
                    continue;
                }

                return new HyperliquidMarginSummary(
                    accountValue.Value,
                    totalNtlPos,
                    totalRawUsd,
                    totalMarginUsed);
            }

            return null;
        }

        private static HyperliquidUserAssetPosition? TryParseAssetPosition(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var type = CoinGlassCollectorJsonHelper.TryReadString(item, "type")?.Trim();
            if (!item.TryGetProperty("position", out var positionRoot) || positionRoot.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var coin = CoinGlassCollectorJsonHelper.TryReadString(positionRoot, "coin");
            var size = CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "szi", "size");
            var positionValue = CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "position_value", "positionValue");
            if (string.IsNullOrWhiteSpace(coin) || !size.HasValue || !positionValue.HasValue)
            {
                return null;
            }

            var leverageType = string.Empty;
            decimal? leverageValue = null;
            if (positionRoot.TryGetProperty("leverage", out var leverageRoot) &&
                leverageRoot.ValueKind == JsonValueKind.Object)
            {
                leverageType = CoinGlassCollectorJsonHelper.TryReadString(leverageRoot, "type")?.Trim() ?? string.Empty;
                leverageValue = CoinGlassCollectorJsonHelper.TryReadDecimal(leverageRoot, "value");
            }

            decimal? cumFundingAllTime = null;
            decimal? cumFundingSinceOpen = null;
            decimal? cumFundingSinceChange = null;
            if (positionRoot.TryGetProperty("cum_funding", out var cumFundingRoot) &&
                cumFundingRoot.ValueKind == JsonValueKind.Object)
            {
                cumFundingAllTime = CoinGlassCollectorJsonHelper.TryReadDecimal(cumFundingRoot, "all_time", "allTime");
                cumFundingSinceOpen = CoinGlassCollectorJsonHelper.TryReadDecimal(cumFundingRoot, "since_open", "sinceOpen");
                cumFundingSinceChange = CoinGlassCollectorJsonHelper.TryReadDecimal(cumFundingRoot, "since_change", "sinceChange");
            }

            return new HyperliquidUserAssetPosition(
                Type: type,
                Coin: coin.Trim().ToUpperInvariant(),
                Size: size.Value,
                LeverageType: leverageType,
                LeverageValue: leverageValue,
                EntryPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "entry_px", "entryPrice"),
                PositionValue: positionValue.Value,
                UnrealizedPnl: CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "unrealized_pnl", "unrealizedPnl"),
                ReturnOnEquity: CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "return_on_equity", "returnOnEquity"),
                LiquidationPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "liquidation_px", "liquidationPrice"),
                MaxLeverage: CoinGlassCollectorJsonHelper.TryReadDecimal(positionRoot, "max_leverage", "maxLeverage"),
                CumFundingAllTime: cumFundingAllTime,
                CumFundingSinceOpen: cumFundingSinceOpen,
                CumFundingSinceChange: cumFundingSinceChange);
        }

        private static string ResolveScopeValue(string scopeKey, string fallback, params string[] keys)
        {
            if (!string.IsNullOrWhiteSpace(scopeKey))
            {
                foreach (var segment in scopeKey.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var kv = segment.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length != 2)
                    {
                        continue;
                    }

                    if (keys.Any(key => string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(kv[1]))
                    {
                        return Uri.UnescapeDataString(kv[1]);
                    }
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private sealed record ParsedHyperliquidUserPositionResponse(
            HyperliquidMarginSummary? MarginSummary,
            HyperliquidMarginSummary? CrossMarginSummary,
            decimal? CrossMaintenanceMarginUsed,
            decimal? Withdrawable,
            IReadOnlyList<HyperliquidUserAssetPosition> AssetPositions,
            long? UpdateTime);

        private readonly record struct HyperliquidMarginSummary(
            decimal AccountValue,
            decimal? TotalNtlPos,
            decimal? TotalRawUsd,
            decimal? TotalMarginUsed);

        private readonly record struct HyperliquidUserAssetPosition(
            string? Type,
            string Coin,
            decimal Size,
            string LeverageType,
            decimal? LeverageValue,
            decimal? EntryPrice,
            decimal PositionValue,
            decimal? UnrealizedPnl,
            decimal? ReturnOnEquity,
            decimal? LiquidationPrice,
            decimal? MaxLeverage,
            decimal? CumFundingAllTime,
            decimal? CumFundingSinceOpen,
            decimal? CumFundingSinceChange);
    }
}
