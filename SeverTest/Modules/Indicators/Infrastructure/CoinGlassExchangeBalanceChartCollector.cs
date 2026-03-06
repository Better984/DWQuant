using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 交易所余额趋势采集器。
    /// </summary>
    public sealed class CoinGlassExchangeBalanceChartCollector : IIndicatorCollector
    {
        private readonly CoinGlassClient _coinGlassClient;
        private readonly CoinGlassOptions _coinGlassOptions;
        private readonly ILogger<CoinGlassExchangeBalanceChartCollector> _logger;

        public CoinGlassExchangeBalanceChartCollector(
            CoinGlassClient coinGlassClient,
            IOptions<CoinGlassOptions> coinGlassOptions,
            ILogger<CoinGlassExchangeBalanceChartCollector> logger)
        {
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _coinGlassOptions = coinGlassOptions?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CollectorName => "coinglass.exchange_balance_chart.collector";

        public bool CanHandle(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.Provider, "coinglass", StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Code, "coinglass.exchange_balance_chart", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct)
        {
            var symbol = ResolveScopeValue(scopeKey, "symbol", _coinGlassOptions.ExchangeBalanceChartDefaultSymbol)
                .Trim()
                .ToUpperInvariant();
            _logger.LogInformation(
                "[coinglass][交易所余额趋势] 开始拉取交易所余额趋势: symbol={Symbol}, scopeKey={ScopeKey}",
                symbol,
                scopeKey);

            using var response = await _coinGlassClient
                .GetExchangeBalanceChartAsync(symbol, ct)
                .ConfigureAwait(false);

            var parsed = ParseResponse(response.RootElement, symbol);
            var normalized = NormalizeAndTrim(parsed, _coinGlassOptions.ExchangeBalanceChartPointLimit);
            var orderedSeries = normalized.Series
                .OrderByDescending(item => item.LatestBalance ?? 0m)
                .ThenBy(item => item.ExchangeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedSeries.Count == 0 || normalized.TimeList.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 交易所余额趋势接口返回为空，无法生成指标数据");
            }

            var displaySeries = orderedSeries
                .Take(Math.Max(1, _coinGlassOptions.ExchangeBalanceChartSeriesTopCount))
                .ToList();
            var latestTotalBalance = orderedSeries.Sum(item => item.LatestBalance ?? 0m);
            var sourceTs = normalized.TimeList[^1];

            var payload = new
            {
                value = latestTotalBalance,
                symbol = normalized.Symbol,
                sourceTs,
                latestTotalBalance,
                totalSeriesCount = orderedSeries.Count,
                displaySeriesCount = displaySeries.Count,
                pointCount = normalized.TimeList.Count,
                timeList = normalized.TimeList,
                priceList = normalized.PriceList,
                series = displaySeries.Select(item => new
                {
                    exchangeName = item.ExchangeName,
                    latestBalance = item.LatestBalance,
                    values = item.Values
                }).ToList()
            };

            return new IndicatorCollectResult
            {
                SourceTs = sourceTs,
                PayloadJson = ProtocolJson.Serialize(payload),
                History = BuildHistory(normalized.Symbol, normalized.TimeList, normalized.PriceList, displaySeries)
            };
        }

        private static ParsedExchangeBalanceChartResponse ParseResponse(JsonElement root, string symbol)
        {
            CoinGlassCollectorJsonHelper.ValidateResponseCode(root, "交易所余额趋势");

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var dataRoot) ||
                dataRoot.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("CoinGlass 交易所余额趋势接口返回结构异常，未找到 data 对象");
            }

            if (!dataRoot.TryGetProperty("timeList", out var timeListElement) ||
                timeListElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("CoinGlass 交易所余额趋势接口缺少 timeList");
            }

            var timeList = CoinGlassCollectorJsonHelper
                .ReadTimestampArray(timeListElement)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToList();

            if (timeList.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 交易所余额趋势接口 timeList 为空");
            }

            var priceList = new List<decimal?>();
            if (dataRoot.TryGetProperty("priceList", out var priceListElement) &&
                priceListElement.ValueKind == JsonValueKind.Array)
            {
                priceList = CoinGlassCollectorJsonHelper.ReadDecimalArray(priceListElement);
            }

            var series = new List<ExchangeBalanceSeries>();
            if (dataRoot.TryGetProperty("dataMap", out var dataMapElement) &&
                dataMapElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dataMapElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array ||
                        string.IsNullOrWhiteSpace(property.Name))
                    {
                        continue;
                    }

                    var values = CoinGlassCollectorJsonHelper.ReadDecimalArray(property.Value);
                    series.Add(new ExchangeBalanceSeries(property.Name.Trim(), values));
                }
            }

            if (series.Count == 0)
            {
                throw new InvalidOperationException("CoinGlass 交易所余额趋势接口 dataMap 为空");
            }

            return new ParsedExchangeBalanceChartResponse(symbol, timeList, priceList, series);
        }

        private static NormalizedExchangeBalanceChartResponse NormalizeAndTrim(
            ParsedExchangeBalanceChartResponse parsed,
            int pointLimit)
        {
            var normalizedPriceList = NormalizeValues(parsed.PriceList, parsed.TimeList.Count);
            var normalizedSeries = parsed.Series
                .Select(item =>
                {
                    var values = NormalizeValues(item.Values, parsed.TimeList.Count);
                    var latestBalance = values.LastOrDefault(value => value.HasValue);
                    return new NormalizedExchangeBalanceSeries(item.ExchangeName, values, latestBalance);
                })
                .ToList();

            var safePointLimit = Math.Max(1, pointLimit);
            if (parsed.TimeList.Count <= safePointLimit)
            {
                return new NormalizedExchangeBalanceChartResponse(parsed.Symbol, parsed.TimeList, normalizedPriceList, normalizedSeries);
            }

            var skipCount = parsed.TimeList.Count - safePointLimit;
            var trimmedTimeList = parsed.TimeList.Skip(skipCount).ToList();
            var trimmedPriceList = normalizedPriceList.Skip(skipCount).ToList();
            var trimmedSeries = normalizedSeries
                .Select(item =>
                {
                    var trimmedValues = item.Values.Skip(skipCount).ToList();
                    return new NormalizedExchangeBalanceSeries(
                        item.ExchangeName,
                        trimmedValues,
                        trimmedValues.LastOrDefault(value => value.HasValue));
                })
                .ToList();

            return new NormalizedExchangeBalanceChartResponse(parsed.Symbol, trimmedTimeList, trimmedPriceList, trimmedSeries);
        }

        private static List<decimal?> NormalizeValues(IReadOnlyList<decimal?> values, int targetCount)
        {
            if (targetCount <= 0)
            {
                return new List<decimal?>();
            }

            if (values.Count == targetCount)
            {
                return values.ToList();
            }

            if (values.Count > targetCount)
            {
                return values.Skip(values.Count - targetCount).ToList();
            }

            var result = new List<decimal?>(targetCount);
            for (var index = 0; index < targetCount - values.Count; index++)
            {
                result.Add(null);
            }

            result.AddRange(values);
            return result;
        }

        private static IReadOnlyList<IndicatorHistoryPoint> BuildHistory(
            string symbol,
            IReadOnlyList<long> timeList,
            IReadOnlyList<decimal?> priceList,
            IReadOnlyList<NormalizedExchangeBalanceSeries> series)
        {
            var history = new List<IndicatorHistoryPoint>(timeList.Count);
            for (var index = 0; index < timeList.Count; index++)
            {
                history.Add(new IndicatorHistoryPoint
                {
                    SourceTs = timeList[index],
                    PayloadJson = ProtocolJson.Serialize(new
                    {
                        symbol,
                        price = index < priceList.Count ? priceList[index] : null,
                        balances = series.Select(item => new
                        {
                            exchangeName = item.ExchangeName,
                            balance = index < item.Values.Count ? item.Values[index] : null
                        }).ToList()
                    })
                });
            }

            return history;
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

        private sealed record ParsedExchangeBalanceChartResponse(
            string Symbol,
            IReadOnlyList<long> TimeList,
            IReadOnlyList<decimal?> PriceList,
            IReadOnlyList<ExchangeBalanceSeries> Series);

        private sealed record NormalizedExchangeBalanceChartResponse(
            string Symbol,
            IReadOnlyList<long> TimeList,
            IReadOnlyList<decimal?> PriceList,
            IReadOnlyList<NormalizedExchangeBalanceSeries> Series);

        private readonly record struct ExchangeBalanceSeries(string ExchangeName, IReadOnlyList<decimal?> Values);

        private readonly record struct NormalizedExchangeBalanceSeries(
            string ExchangeName,
            IReadOnlyList<decimal?> Values,
            decimal? LatestBalance);
    }
}
