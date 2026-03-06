using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 交易所余额排行采集器。
    /// </summary>
    public sealed class CoinGlassExchangeBalanceListCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassExchangeBalanceListCollector> _logger;

        public CoinGlassExchangeBalanceListCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassExchangeBalanceListCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.exchange_balance_list.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.exchange_balance_list", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var symbol = ResolveScopeValue(scopeKey, "symbol", _coinGlassOptions.ExchangeBalanceListDefaultSymbol)
                .Trim()
                .ToUpperInvariant();
            _logger.LogInformation(
                "[coinglass][交易所余额排行] 开始拉取交易所余额排行: symbol={Symbol}, scopeKey={ScopeKey}",
                symbol,
                scopeKey);

            using var response = await _coinGlassClient
                .GetExchangeBalanceListAsync(symbol, ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement, symbol);
            var orderedItems = parsed.Items
                .OrderByDescending(item => item.Balance)
                .ThenBy(item => item.ExchangeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 交易所余额排行接口返回为空，无法生成指标数据");
            }

            var displayItems = orderedItems
                .Take(Math.Max(1, _coinGlassOptions.ExchangeBalanceListTopCount))
                .ToList();
            var totalBalance = orderedItems.Sum(item => item.Balance);
            var leader = displayItems[0];
            var payload = new
            {
                value = totalBalance,
                symbol = parsed.Symbol,
                sourceTs = parsed.SourceTs,
                totalBalance,
                totalExchangeCount = orderedItems.Count,
                displayCount = displayItems.Count,
                leadingExchangeName = leader.ExchangeName,
                leadingBalance = leader.Balance,
                items = displayItems.Select(item => new
                {
                    exchangeName = item.ExchangeName,
                    balance = item.Balance,
                    change1d = item.Change1d,
                    changePercent1d = item.ChangePercent1d,
                    change7d = item.Change7d,
                    changePercent7d = item.ChangePercent7d,
                    change30d = item.Change30d,
                    changePercent30d = item.ChangePercent30d
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

        private static ParsedExchangeBalanceListResponse ParseResponse(JsonElement root, string symbol)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "交易所余额排行");

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
                throw new InvalidOperationException("CoinGlass 交易所余额排行接口返回结构异常，未找到 data 数组");
            }

            var items = new List<ExchangeBalanceListItem>();
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
                throw new InvalidOperationException("CoinGlass 交易所余额排行接口返回结构无法解析有效排行数据");
            }

            return new ParsedExchangeBalanceListResponse(
                SourceTs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Symbol: symbol,
                Items: items);
        }

        private static ExchangeBalanceListItem? TryParseItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var exchangeName = CoinGlassCollectorJsonHelper.TryReadString(item, "exchangeName", "exchange_name");
            if (string.IsNullOrWhiteSpace(exchangeName))
            {
                return null;
            }

            return new ExchangeBalanceListItem(
                ExchangeName: exchangeName.Trim(),
                Balance: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "balance") ?? 0m,
                Change1d: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "change1d", "change_1d"),
                ChangePercent1d: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "changePercent1d", "change_percent_1d"),
                Change7d: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "change7d", "change_7d"),
                ChangePercent7d: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "changePercent7d", "change_percent_7d"),
                Change30d: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "change30d", "change_30d"),
                ChangePercent30d: CoinGlassCollectorJsonHelper.TryReadDecimal(item, "changePercent30d", "change_percent_30d"));
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

        private sealed record ParsedExchangeBalanceListResponse(
            long SourceTs,
            string Symbol,
            IReadOnlyList<ExchangeBalanceListItem> Items);

        private readonly record struct ExchangeBalanceListItem(
            string ExchangeName,
            decimal Balance,
            decimal? Change1d,
            decimal? ChangePercent1d,
            decimal? Change7d,
            decimal? ChangePercent7d,
            decimal? Change30d,
            decimal? ChangePercent30d);
    }
}
