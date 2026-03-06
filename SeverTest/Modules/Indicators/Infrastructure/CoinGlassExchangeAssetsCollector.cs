using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 交易所资产明细采集器。
    /// </summary>
    public sealed class CoinGlassExchangeAssetsCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassExchangeAssetsCollector> _logger;

        public CoinGlassExchangeAssetsCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassExchangeAssetsCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.exchange_assets.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.exchange_assets", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var exchangeName = ResolveScopeValue(scopeKey, "exchangeName", _coinGlassOptions.ExchangeAssetsDefaultExchangeName);
            _logger.LogInformation(
                "[coinglass][交易所资产明细] 开始拉取交易所资产明细: exchangeName={ExchangeName}, scopeKey={ScopeKey}",
                exchangeName,
                scopeKey);

            using var response = await _coinGlassClient
                .GetExchangeAssetsAsync(exchangeName, ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement, exchangeName);
            var orderedItems = parsed.Items
                .OrderByDescending(item => item.BalanceUsd)
                .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 交易所资产明细接口返回为空，无法生成指标数据");
            }

            var displayItems = orderedItems
                .Take(Math.Max(1, _coinGlassOptions.ExchangeAssetsTopCount))
                .ToList();
            var totalBalanceUsd = orderedItems.Sum(item => item.BalanceUsd);
            var leader = displayItems[0];
            var payload = new
            {
                value = totalBalanceUsd,
                exchangeName = parsed.ExchangeName,
                sourceTs = parsed.SourceTs,
                totalBalanceUsd,
                totalAssetCount = orderedItems.Count,
                displayCount = displayItems.Count,
                leadingSymbol = leader.Symbol,
                leadingBalanceUsd = leader.BalanceUsd,
                items = displayItems.Select(item => new
                {
                    walletAddress = item.WalletAddress,
                    symbol = item.Symbol,
                    assetsName = item.AssetsName,
                    balance = item.Balance,
                    balanceUsd = item.BalanceUsd,
                    price = item.Price
                }).ToList()
            };

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

        private static ParsedExchangeAssetsResponse ParseResponse(JsonElement root, string exchangeName)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "交易所资产明细");

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
                throw new InvalidOperationException("CoinGlass 交易所资产明细接口返回结构异常，未找到 data 数组");
            }

            var items = new List<ExchangeAssetItem>();
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
                throw new InvalidOperationException("CoinGlass 交易所资产明细接口返回结构无法解析有效资产数据");
            }

            return new ParsedExchangeAssetsResponse(
                SourceTs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExchangeName: exchangeName,
                Items: items);
        }

        private static ExchangeAssetItem? TryParseItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var symbol = CoinGlassCollectorJsonHelper.TryReadString(item, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var balance = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "balance") ?? 0m;
            var balanceUsd = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "balanceUsd", "balance_usd");
            var price = CoinGlassCollectorJsonHelper.TryReadDecimal(item, "price");
            if (!balanceUsd.HasValue)
            {
                balanceUsd = price.HasValue ? balance * price.Value : 0m;
            }

            return new ExchangeAssetItem(
                WalletAddress: CoinGlassCollectorJsonHelper.TryReadString(item, "walletAddress", "wallet_address")?.Trim(),
                Symbol: symbol.Trim().ToUpperInvariant(),
                AssetsName: CoinGlassCollectorJsonHelper.TryReadString(item, "assetsName", "assets_name", "name")?.Trim(),
                Balance: balance,
                BalanceUsd: balanceUsd.Value,
                Price: price);
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
                        return Uri.UnescapeDataString(segments[1]);
                    }
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private sealed record ParsedExchangeAssetsResponse(
            long SourceTs,
            string ExchangeName,
            IReadOnlyList<ExchangeAssetItem> Items);

        private readonly record struct ExchangeAssetItem(
            string? WalletAddress,
            string Symbol,
            string? AssetsName,
            decimal Balance,
            decimal BalanceUsd,
            decimal? Price);
    }
}
