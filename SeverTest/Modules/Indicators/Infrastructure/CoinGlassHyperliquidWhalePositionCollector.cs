using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass Hyperliquid 鲸鱼持仓采集器。
    /// </summary>
    public sealed class CoinGlassHyperliquidWhalePositionCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassHyperliquidWhalePositionCollector> _logger;

        public CoinGlassHyperliquidWhalePositionCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassHyperliquidWhalePositionCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.hyperliquid_whale_position.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.hyperliquid_whale_position", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            _logger.LogInformation(
                "[coinglass][Hyperliquid][鲸鱼持仓] 开始拉取鲸鱼持仓: scopeKey={ScopeKey}",
                scopeKey);

            using var response = await _coinGlassClient
                .GetHyperliquidWhalePositionAsync(ct)
                .ConfigureAwait(false);

            var orderedItems = ParseResponse(response.RootElement)
                .OrderByDescending(item => item.PositionValueUsd)
                .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 鲸鱼持仓接口返回为空，无法生成指标数据");
            }

            var displayItems = orderedItems
                .Take(Math.Max(1, _coinGlassOptions.HyperliquidWhalePositionTopCount))
                .ToList();
            var sourceTs = orderedItems
                .Select(item => item.UpdateTime ?? item.CreateTime)
                .DefaultIfEmpty(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Max();
            var totalPositionValueUsd = orderedItems.Sum(item => item.PositionValueUsd);
            var totalMarginBalance = orderedItems.Sum(item => item.MarginBalance ?? 0m);
            var longCount = orderedItems.Count(item => item.PositionSize > 0m);
            var shortCount = orderedItems.Count(item => item.PositionSize < 0m);
            var leader = displayItems[0];

            var payload = new
            {
                value = totalPositionValueUsd,
                sourceTs,
                totalPositionValueUsd,
                totalMarginBalance,
                totalPositionCount = orderedItems.Count,
                displayCount = displayItems.Count,
                longCount,
                shortCount,
                leadingSymbol = leader.Symbol,
                leadingUser = leader.User,
                leadingPositionValueUsd = leader.PositionValueUsd,
                items = displayItems.Select(item => new
                {
                    user = item.User,
                    symbol = item.Symbol,
                    positionSize = item.PositionSize,
                    entryPrice = item.EntryPrice,
                    markPrice = item.MarkPrice,
                    liqPrice = item.LiqPrice,
                    leverage = item.Leverage,
                    marginBalance = item.MarginBalance,
                    positionValueUsd = item.PositionValueUsd,
                    unrealizedPnl = item.UnrealizedPnl,
                    fundingFee = item.FundingFee,
                    marginMode = item.MarginMode,
                    createTime = item.CreateTime,
                    updateTime = item.UpdateTime
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

        private static IReadOnlyList<HyperliquidWhalePositionItem> ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "Hyperliquid 鲸鱼持仓");

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var dataRoot) ||
                dataRoot.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 鲸鱼持仓接口返回结构异常，未找到 data 数组");
            }

            var items = new List<HyperliquidWhalePositionItem>();
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

        private static HyperliquidWhalePositionItem? TryParseItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var user = CoinGlassCollectorJsonHelper.TryReadString(item, "user");
            var symbol = CoinGlassCollectorJsonHelper.TryReadString(item, "symbol");
            var positionValueUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "position_value_usd", "positionValueUsd");
            if (string.IsNullOrWhiteSpace(user) ||
                string.IsNullOrWhiteSpace(symbol) ||
                !positionValueUsd.HasValue)
            {
                return null;
            }

            return new HyperliquidWhalePositionItem(
                User: user.Trim(),
                Symbol: symbol.Trim().ToUpperInvariant(),
                PositionSize: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "position_size", "positionSize") ?? 0m,
                EntryPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "entry_price", "entryPrice"),
                MarkPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "mark_price", "markPrice"),
                LiqPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "liq_price", "liqPrice"),
                Leverage: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "leverage"),
                MarginBalance: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "margin_balance", "marginBalance"),
                PositionValueUsd: positionValueUsd.Value,
                UnrealizedPnl: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "unrealized_pnl", "unrealizedPnl"),
                FundingFee: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "funding_fee", "fundingFee"),
                MarginMode: CoinGlassCollectorJsonHelper.TryReadString(item, "margin_mode", "marginMode")?.Trim(),
                CreateTime: CoinGlassCollectorJsonHelper.TryReadLong(item, "create_time", "createTime")
                    ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdateTime: CoinGlassCollectorJsonHelper.TryReadLong(item, "update_time", "updateTime"));
        }

        private readonly record struct HyperliquidWhalePositionItem(
            string User,
            string Symbol,
            decimal PositionSize,
            decimal? EntryPrice,
            decimal? MarkPrice,
            decimal? LiqPrice,
            decimal? Leverage,
            decimal? MarginBalance,
            decimal PositionValueUsd,
            decimal? UnrealizedPnl,
            decimal? FundingFee,
            string? MarginMode,
            long CreateTime,
            long? UpdateTime);
    }
}
