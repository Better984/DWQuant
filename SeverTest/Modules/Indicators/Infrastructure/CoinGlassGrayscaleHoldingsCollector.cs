using Microsoft.Extensions.Logging;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Protocol;
using System.Globalization;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 灰度持仓采集器。
    /// </summary>
    public sealed class CoinGlassGrayscaleHoldingsCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly ILogger<CoinGlassGrayscaleHoldingsCollector> _logger;

        public CoinGlassGrayscaleHoldingsCollector(
            CoinGlassClient coinGlassClient,
            ILogger<CoinGlassGrayscaleHoldingsCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.grayscale_holdings.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.grayscale_holdings", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            _logger.LogInformation("[coinglass][灰度持仓] 开始拉取灰度持仓列表: scopeKey={ScopeKey}", scopeKey);

            using var response = await _coinGlassClient
                .GetGrayscaleHoldingsAsync(ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement);
            var orderedItems = parsed.Items
                .OrderByDescending(item => item.HoldingsUsd)
                .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedItems.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 灰度持仓接口返回为空，无法生成指标数据");
            }

            var totalHoldingsUsd = orderedItems.Sum(item => item.HoldingsUsd);
            var maxHolding = orderedItems[0];
            var premiumPositiveCount = orderedItems.Count(item => item.PremiumRate > 0m);
            var premiumNegativeCount = orderedItems.Count(item => item.PremiumRate < 0m);
            var sourceTs = orderedItems
                .Select(item => Math.Max(item.UpdateTime, item.CloseTime))
                .DefaultIfEmpty(parsed.SourceTs)
                .Max();

            var payload = new
            {
                value = totalHoldingsUsd,
                totalHoldingsUsd,
                assetCount = orderedItems.Count,
                maxHoldingSymbol = maxHolding.Symbol,
                maxHoldingUsd = maxHolding.HoldingsUsd,
                premiumPositiveCount,
                premiumNegativeCount,
                sourceTs,
                items = orderedItems.Select(item => new
                {
                    symbol = item.Symbol,
                    primaryMarketPrice = item.PrimaryMarketPrice,
                    secondaryMarketPrice = item.SecondaryMarketPrice,
                    premiumRate = item.PremiumRate,
                    holdingsAmount = item.HoldingsAmount,
                    holdingsUsd = item.HoldingsUsd,
                    holdingsAmountChange1d = item.HoldingsAmountChange1d,
                    holdingsAmountChange7d = item.HoldingsAmountChange7d,
                    holdingsAmountChange30d = item.HoldingsAmountChange30d,
                    closeTime = item.CloseTime,
                    updateTime = item.UpdateTime
                }).ToList()
            };

            _logger.LogInformation(
                "[coinglass][灰度持仓] 灰度持仓采集完成: count={Count}, totalHoldingsUsd={TotalHoldingsUsd}, maxHoldingSymbol={MaxHoldingSymbol}, sourceTs={SourceTs}",
                orderedItems.Count,
                totalHoldingsUsd,
                maxHolding.Symbol,
                sourceTs);

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

        private static ParsedGrayscaleResponse ParseResponse(JsonElement root)
        {
            ValidateResponseCode(root);

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
                throw new InvalidOperationException("CoinGlass 灰度持仓接口返回结构异常，未找到 data 数组");
            }

            var items = new List<GrayscaleHoldingItem>();
            foreach (var item in dataRoot.EnumerateArray())
            {
                var parsed = TryParseItem(item);
                if (parsed.HasValue)
                {
                    items.Add(parsed.Value);
                }
            }

            if (items.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 灰度持仓接口返回结构无法解析有效持仓明细");
            }

            var sourceTs = items
                .Select(item => Math.Max(item.UpdateTime, item.CloseTime))
                .DefaultIfEmpty(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Max();

            return new ParsedGrayscaleResponse(sourceTs, items);
        }

        private static void ValidateResponseCode(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("code", out var codeElement))
            {
                return;
            }

            var codeText = codeElement.ValueKind switch
            {
                JsonValueKind.String => codeElement.GetString(),
                JsonValueKind.Number when codeElement.TryGetInt64(out var numeric) => numeric.ToString(CultureInfo.InvariantCulture),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(codeText) || string.Equals(codeText, "0", StringComparison.Ordinal))
            {
                return;
            }

            var message = TryReadString(root, "msg")
                ?? TryReadString(root, "message")
                ?? "未知错误";
            throw new InvalidOperationException($"CoinGlass 灰度持仓接口返回错误: code={codeText}, msg={message}");
        }

        private static GrayscaleHoldingItem? TryParseItem(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var symbol = TryReadString(item, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var primaryMarketPrice = TryReadDecimal(item, "primary_market_price")
                ?? TryReadDecimal(item, "primaryMarketPrice")
                ?? 0m;
            var secondaryMarketPrice = TryReadDecimal(item, "secondary_market_price")
                ?? TryReadDecimal(item, "secondaryMarketPrice")
                ?? 0m;
            var premiumRate = TryReadDecimal(item, "premium_rate")
                ?? TryReadDecimal(item, "premiumRate")
                ?? 0m;
            var holdingsAmount = TryReadDecimal(item, "holdings_amount")
                ?? TryReadDecimal(item, "holdingsAmount")
                ?? 0m;
            var holdingsUsd = TryReadDecimal(item, "holdings_usd")
                ?? TryReadDecimal(item, "holdingsUsd")
                ?? holdingsAmount * (secondaryMarketPrice > 0m ? secondaryMarketPrice : primaryMarketPrice);
            var holdingsAmountChange30d = TryReadDecimal(item, "holdings_amount_change_30d")
                ?? TryReadDecimal(item, "holdingsAmountChange30d")
                ?? 0m;
            var holdingsAmountChange7d = TryReadDecimal(item, "holdings_amount_change_7d")
                ?? TryReadDecimal(item, "holdingsAmountChange7d")
                ?? 0m;
            var holdingsAmountChange1d = TryReadDecimal(item, "holdings_amount_change1d")
                ?? TryReadDecimal(item, "holdings_amount_change_1d")
                ?? TryReadDecimal(item, "holdingsAmountChange1d")
                ?? 0m;
            var closeTime = TryReadLong(item, "close_time")
                ?? TryReadLong(item, "closeTime")
                ?? 0L;
            var updateTime = TryReadLong(item, "update_time")
                ?? TryReadLong(item, "updateTime")
                ?? closeTime;

            return new GrayscaleHoldingItem(
                Symbol: symbol.Trim().ToUpperInvariant(),
                PrimaryMarketPrice: primaryMarketPrice,
                SecondaryMarketPrice: secondaryMarketPrice,
                PremiumRate: premiumRate,
                HoldingsAmount: holdingsAmount,
                HoldingsUsd: holdingsUsd,
                HoldingsAmountChange30d: holdingsAmountChange30d,
                HoldingsAmountChange7d: holdingsAmountChange7d,
                HoldingsAmountChange1d: holdingsAmountChange1d,
                CloseTime: NormalizeTimestamp(closeTime),
                UpdateTime: NormalizeTimestamp(updateTime));
        }

        private static string? TryReadString(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static decimal? TryReadDecimal(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static long? TryReadLong(JsonElement obj, string fieldName)
        {
            if (!obj.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static long NormalizeTimestamp(long raw)
        {
            // 小于 10^11 基本可判定为秒级时间戳。
            return raw > 0 && raw < 100_000_000_000L ? raw * 1000 : raw;
        }

        private sealed record ParsedGrayscaleResponse(
            long SourceTs,
            IReadOnlyList<GrayscaleHoldingItem> Items);

        private readonly record struct GrayscaleHoldingItem(
            string Symbol,
            decimal PrimaryMarketPrice,
            decimal SecondaryMarketPrice,
            decimal PremiumRate,
            decimal HoldingsAmount,
            decimal HoldingsUsd,
            decimal HoldingsAmountChange30d,
            decimal HoldingsAmountChange7d,
            decimal HoldingsAmountChange1d,
            long CloseTime,
            long UpdateTime);
    }
}
