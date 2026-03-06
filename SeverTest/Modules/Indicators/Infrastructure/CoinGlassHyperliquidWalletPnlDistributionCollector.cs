using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass Hyperliquid 钱包盈亏分布采集器。
    /// </summary>
    public sealed class CoinGlassHyperliquidWalletPnlDistributionCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassHyperliquidWalletPnlDistributionCollector> _logger;

        public CoinGlassHyperliquidWalletPnlDistributionCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassHyperliquidWalletPnlDistributionCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.hyperliquid_wallet_pnl_distribution.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.hyperliquid_wallet_pnl_distribution", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            _logger.LogInformation(
                "[coinglass][Hyperliquid][钱包盈亏分布] 开始拉取钱包盈亏分布: scopeKey={ScopeKey}",
                scopeKey);

            using var response = await _coinGlassClient
                .GetHyperliquidWalletPnlDistributionAsync(ct)
                .ConfigureAwait(false);

            var orderedItems = ParseResponse(response.RootElement)
                .OrderByDescending(item => item.PositionUsd)
                .ThenBy(item => item.GroupName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 钱包盈亏分布接口返回为空，无法生成指标数据");
            }

            var displayItems = orderedItems
                .Take(Math.Max(1, _coinGlassOptions.HyperliquidWalletPnlDistributionTopCount))
                .ToList();
            var sourceTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var totalPositionUsd = orderedItems.Sum(item => item.PositionUsd);
            var totalPositionAddressCount = orderedItems.Sum(item => item.PositionAddressCount ?? 0m);
            var leader = displayItems[0];

            var payload = new
            {
                value = totalPositionUsd,
                sourceTs,
                totalPositionUsd,
                totalGroupCount = orderedItems.Count,
                totalPositionAddressCount,
                displayCount = displayItems.Count,
                leadingGroupName = leader.GroupName,
                leadingPositionUsd = leader.PositionUsd,
                items = displayItems.Select(item => new
                {
                    groupName = item.GroupName,
                    allAddressCount = item.AllAddressCount,
                    positionAddressCount = item.PositionAddressCount,
                    positionAddressPercent = item.PositionAddressPercent,
                    biasScore = item.BiasScore,
                    biasRemark = item.BiasRemark,
                    minimumAmount = item.MinimumAmount,
                    maximumAmount = item.MaximumAmount,
                    longPositionUsd = item.LongPositionUsd,
                    shortPositionUsd = item.ShortPositionUsd,
                    longPositionUsdPercent = item.LongPositionUsdPercent,
                    shortPositionUsdPercent = item.ShortPositionUsdPercent,
                    positionUsd = item.PositionUsd,
                    profitAddressCount = item.ProfitAddressCount,
                    lossAddressCount = item.LossAddressCount,
                    profitAddressPercent = item.ProfitAddressPercent,
                    lossAddressPercent = item.LossAddressPercent
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

        private static IReadOnlyList<HyperliquidWalletPnlDistributionItem> ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "Hyperliquid 钱包盈亏分布");

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var dataRoot) ||
                dataRoot.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 钱包盈亏分布接口返回结构异常，未找到 data 数组");
            }

            var items = new List<HyperliquidWalletPnlDistributionItem>();
            foreach (var item in dataRoot.EnumerateArray())
            {
                var parsed = TryParseItem(item);
                if (parsed.HasValue)
                {
                    items.Add(parsed.Value);
                }
            }

            return items;
        }

        private static HyperliquidWalletPnlDistributionItem? TryParseItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var groupName = CoinGlassCollectorJsonHelper.TryReadString(item, "group_name", "groupName");
            var positionUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "position_usd", "positionUsd");
            if (string.IsNullOrWhiteSpace(groupName) || !positionUsd.HasValue)
            {
                return null;
            }

            return new HyperliquidWalletPnlDistributionItem(
                GroupName: groupName.Trim(),
                AllAddressCount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "all_address_count", "allAddressCount"),
                PositionAddressCount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "position_address_count", "positionAddressCount"),
                PositionAddressPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "position_address_percent", "positionAddressPercent"),
                BiasScore: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "bias_score", "biasScore"),
                BiasRemark: CoinGlassCollectorJsonHelper.TryReadString(item, "bias_remark", "biasRemark")?.Trim(),
                MinimumAmount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "minimum_amount", "minimumAmount"),
                MaximumAmount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "maximum_amount", "maximumAmount"),
                LongPositionUsd: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "long_position_usd", "longPositionUsd"),
                ShortPositionUsd: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "short_position_usd", "shortPositionUsd"),
                LongPositionUsdPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "long_position_usd_percent", "longPositionUsdPercent"),
                ShortPositionUsdPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "short_position_usd_percent", "shortPositionUsdPercent"),
                PositionUsd: positionUsd.Value,
                ProfitAddressCount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "profit_address_count", "profitAddressCount"),
                LossAddressCount: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "loss_address_count", "lossAddressCount"),
                ProfitAddressPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "profit_address_percent", "profitAddressPercent"),
                LossAddressPercent: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "loss_address_percent", "lossAddressPercent"));
        }

        private readonly record struct HyperliquidWalletPnlDistributionItem(
            string GroupName,
            decimal? AllAddressCount,
            decimal? PositionAddressCount,
            decimal? PositionAddressPercent,
            decimal? BiasScore,
            string? BiasRemark,
            decimal? MinimumAmount,
            decimal? MaximumAmount,
            decimal? LongPositionUsd,
            decimal? ShortPositionUsd,
            decimal? LongPositionUsdPercent,
            decimal? ShortPositionUsdPercent,
            decimal PositionUsd,
            decimal? ProfitAddressCount,
            decimal? LossAddressCount,
            decimal? ProfitAddressPercent,
            decimal? LossAddressPercent);
    }
}
