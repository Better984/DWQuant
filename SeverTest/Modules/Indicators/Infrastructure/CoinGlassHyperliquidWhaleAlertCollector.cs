using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass Hyperliquid 鲸鱼提醒采集器。
    /// </summary>
    public sealed class CoinGlassHyperliquidWhaleAlertCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassHyperliquidWhaleAlertCollector> _logger;

        public CoinGlassHyperliquidWhaleAlertCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassHyperliquidWhaleAlertCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.hyperliquid_whale_alert.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.hyperliquid_whale_alert", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            _logger.LogInformation(
                "[coinglass][Hyperliquid][鲸鱼提醒] 开始拉取鲸鱼提醒: scopeKey={ScopeKey}",
                scopeKey);

            using var response = await _coinGlassClient
                .GetHyperliquidWhaleAlertAsync(ct)
                .ConfigureAwait(false);

            var orderedItems = ParseResponse(response.RootElement)
                .OrderByDescending(item => item.PositionValueUsd)
                .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 鲸鱼提醒接口返回为空，无法生成指标数据");
            }

            var displayItems = orderedItems
                .Take(Math.Max(1, _coinGlassOptions.HyperliquidWhaleAlertTopCount))
                .ToList();
            var sourceTs = orderedItems
                .Select(item => item.CreateTime)
                .DefaultIfEmpty(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Max();
            var totalPositionValueUsd = orderedItems.Sum(item => item.PositionValueUsd);
            var longAlertCount = orderedItems.Count(item => item.PositionSize > 0m);
            var shortAlertCount = orderedItems.Count(item => item.PositionSize < 0m);
            var leader = displayItems[0];

            var payload = new
            {
                value = totalPositionValueUsd,
                sourceTs,
                totalPositionValueUsd,
                totalAlertCount = orderedItems.Count,
                displayCount = displayItems.Count,
                longAlertCount,
                shortAlertCount,
                leadingSymbol = leader.Symbol,
                leadingUser = leader.User,
                leadingPositionValueUsd = leader.PositionValueUsd,
                items = displayItems.Select(item => new
                {
                    user = item.User,
                    symbol = item.Symbol,
                    positionSize = item.PositionSize,
                    entryPrice = item.EntryPrice,
                    liqPrice = item.LiqPrice,
                    positionValueUsd = item.PositionValueUsd,
                    positionAction = item.PositionAction,
                    createTime = item.CreateTime
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

        private static IReadOnlyList<HyperliquidWhaleAlertItem> ParseResponse(JsonElement root)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "Hyperliquid 鲸鱼提醒");

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var dataRoot) ||
                dataRoot.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("CoinGlass Hyperliquid 鲸鱼提醒接口返回结构异常，未找到 data 数组");
            }

            var items = new List<HyperliquidWhaleAlertItem>();
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

        private static HyperliquidWhaleAlertItem? TryParseItem(JsonElement item)
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

            return new HyperliquidWhaleAlertItem(
                User: user.Trim(),
                Symbol: symbol.Trim().ToUpperInvariant(),
                PositionSize: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "position_size", "positionSize") ?? 0m,
                EntryPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "entry_price", "entryPrice"),
                LiqPrice: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "liq_price", "liqPrice"),
                PositionValueUsd: positionValueUsd.Value,
                PositionAction: CoinGlassCollectorJsonHelper.TryReadLong(item, "position_action", "positionAction"),
                CreateTime: CoinGlassCollectorJsonHelper.TryReadLong(item, "create_time", "createTime")
                    ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        private readonly record struct HyperliquidWhaleAlertItem(
            string User,
            string Symbol,
            decimal PositionSize,
            decimal? EntryPrice,
            decimal? LiqPrice,
            decimal PositionValueUsd,
            long? PositionAction,
            long CreateTime);
    }
}
