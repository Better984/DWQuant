using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Modules.ExchangeApiKeys.Domain;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Models;
using ServerTest.Options;
using System.Globalization;
using System.Collections.Generic;

namespace ServerTest.Modules.TradingExecution.Infrastructure
{
    public sealed class CcxtOrderExecutor : IOrderExecutor
    {
        private readonly UserExchangeApiKeyRepository _apiKeyRepository;
        private readonly ILogger<CcxtOrderExecutor> _logger;
        private readonly IOptionsMonitor<TradingOptions> _tradingOptions;

        public CcxtOrderExecutor(
            UserExchangeApiKeyRepository apiKeyRepository,
            ILogger<CcxtOrderExecutor> logger,
            IOptionsMonitor<TradingOptions> tradingOptions)
        {
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tradingOptions = tradingOptions ?? throw new ArgumentNullException(nameof(tradingOptions));
        }

        public async Task<OrderExecutionResult> PlaceMarketOrderAsync(OrderExecutionRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return new OrderExecutionResult { Success = false, ErrorMessage = "Request is null" };
            }

            var exchangeType = NormalizeExchange(request.Exchange);
            if (string.IsNullOrWhiteSpace(exchangeType))
            {
                return new OrderExecutionResult { Success = false, ErrorMessage = "Exchange is required" };
            }

            var sandboxEnabled = _tradingOptions.CurrentValue.EnableSandboxMode;
            var apiKey = await ResolveApiKeyAsync(request, exchangeType, ct).ConfigureAwait(false);
            
            // 如果启用了模拟盘模式且没有找到 API key，使用测试用的假 API key
            if (apiKey == null && sandboxEnabled)
            {
                _logger.LogWarning("模拟盘模式：未找到 API key，使用测试 API key: exchange={Exchange} uid={Uid}", exchangeType, request.Uid);
                apiKey = CreateTestApiKey(exchangeType);
            }
            
            if (apiKey == null)
            {
                var errorMsg = sandboxEnabled
                    ? "Exchange API key not found. 模拟盘模式需要配置测试环境的 API key，或系统将使用测试 API key"
                    : "Exchange API key not found";
                return new OrderExecutionResult { Success = false, ErrorMessage = errorMsg };
            }

            Exchange? exchangeClient = null;
            try
            {
                exchangeClient = CcxtExchangeFactory.Create(exchangeType, apiKey);

                // 模拟盘模式：开启 sandbox 配置
                CcxtExchangeFactory.ConfigureSandbox(exchangeClient, exchangeType, sandboxEnabled);
                if (sandboxEnabled)
                {
                    _logger.LogInformation("模拟盘模式已启用: exchange={Exchange} uid={Uid}", exchangeType, request.Uid);
                }

                var normalizedSymbol = MarketDataKeyNormalizer.NormalizeSymbol(request.Symbol);
                var orderSymbol = await ResolveContractSymbolAsync(exchangeClient, normalizedSymbol).ConfigureAwait(false);
                
                var parameters = new Dictionary<string, object>();
                if (request.ReduceOnly)
                {
                    parameters["reduceOnly"] = true;
                }
                if (CcxtExchangeFactory.IsBitget(exchangeType))
                {
                    await TryConfigureBitgetPositionModeAsync(exchangeClient, orderSymbol, parameters).ConfigureAwait(false);
                }

                object order;
                try
                {
                    order = await exchangeClient.createOrder(
                        orderSymbol,
                        "market",
                        request.Side,
                        (double)request.Qty,
                        null,
                        parameters).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsMarketBuy(request.Side) && IsMarketBuyPriceRequired(ex))
                {
                    var price = await TryFetchMarketPriceAsync(exchangeClient, orderSymbol).ConfigureAwait(false);
                    if (!price.HasValue)
                    {
                        _logger.LogError(ex, "Market buy requires price but ticker unavailable: exchange={Exchange} symbol={Symbol}", exchangeType, request.Symbol);
                        return new OrderExecutionResult
                        {
                            Success = false,
                            ErrorMessage = "Market buy requires price but ticker unavailable"
                        };
                    }

                    order = await exchangeClient.createOrder(
                        orderSymbol,
                        "market",
                        request.Side,
                        (double)request.Qty,
                        price.Value,
                        parameters).ConfigureAwait(false);
                }

                var orderId = TryGetOrderId(order);
                var avg = TryGetAveragePrice(order);

                return new OrderExecutionResult
                {
                    Success = true,
                    ExchangeOrderId = orderId,
                    AveragePrice = avg
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CCXT订单失败: exchange={Exchange} symbol={Symbol} side={Side}", exchangeType, request.Symbol, request.Side);
                return new OrderExecutionResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                if (exchangeClient is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private async Task<UserExchangeApiKeyRecord?> ResolveApiKeyAsync(OrderExecutionRequest request, string exchangeType, CancellationToken ct)
        {
            if (request.ExchangeApiKeyId.HasValue && request.ExchangeApiKeyId.Value > 0)
            {
                return await _apiKeyRepository.GetByIdAsync(request.ExchangeApiKeyId.Value, request.Uid, ct).ConfigureAwait(false);
            }

            return await _apiKeyRepository.GetLatestByUidAsync(request.Uid, exchangeType, ct).ConfigureAwait(false);
        }

        private static string NormalizeExchange(string exchange) => exchange?.Trim().ToLowerInvariant() ?? string.Empty;

        /// <summary>
        /// 创建测试用的 API key（用于模拟盘模式）
        /// 注意：某些交易所的 sandbox 模式可能仍然需要有效的测试环境 API key
        /// 如果使用假 API key 失败，请在交易所的测试环境中创建真实的测试 API key
        /// </summary>
        private static UserExchangeApiKeyRecord CreateTestApiKey(string exchangeType)
        {
            // 使用测试用的假 API key
            // 注意：某些交易所（如 Binance）的 sandbox 模式可能需要真实的测试环境 API key
            return new UserExchangeApiKeyRecord
            {
                Id = 0,
                Uid = 0,
                ExchangeType = exchangeType,
                ApiKey = "test_api_key",
                ApiSecret = "test_api_secret",
                ApiPassword = null
            };
        }

        private static string? TryGetOrderId(object? order)
        {
            if (order == null)
            {
                return null;
            }

            if (order is IDictionary<string, object> dict && dict.TryGetValue("id", out var idValue))
            {
                return idValue?.ToString();
            }

            try
            {
                return ((dynamic)order).id?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static decimal? TryGetAveragePrice(object? order)
        {
            if (order == null)
            {
                return null;
            }

            if (order is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue("average", out var avgValue) && avgValue != null)
                {
                    return Convert.ToDecimal(avgValue);
                }

                if (dict.TryGetValue("price", out var priceValue) && priceValue != null)
                {
                    return Convert.ToDecimal(priceValue);
                }
            }

            try
            {
                var avg = ((dynamic)order).average;
                if (avg != null)
                {
                    return Convert.ToDecimal(avg);
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                var price = ((dynamic)order).price;
                if (price != null)
                {
                    return Convert.ToDecimal(price);
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static bool IsMarketBuy(string side)
        {
            return string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task TryConfigureBitgetPositionModeAsync(
            Exchange exchangeClient,
            string symbol,
            Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            try
            {
                dynamic dyn = exchangeClient;
                var result = await dyn.fetchMarginMode(symbol, new Dictionary<string, object>
                {
                    ["type"] = "swap"
                }).ConfigureAwait(false);

                var posMode = TryExtractPosMode(result);
                if (string.Equals(posMode, "hedge_mode", StringComparison.OrdinalIgnoreCase))
                {
                    parameters["hedged"] = true;
                }
                else if (string.Equals(posMode, "one_way_mode", StringComparison.OrdinalIgnoreCase))
                {
                    parameters["oneWayMode"] = true;
                    parameters["hedged"] = false;
                }
            }
            catch
            {
                // ignore: fallback to exchange default
            }
        }

        private static string? TryExtractPosMode(object? result)
        {
            if (result == null)
            {
                return null;
            }

            if (result is IDictionary<string, object> dict)
            {
                var posMode = GetString(dict, "posMode");
                if (!string.IsNullOrWhiteSpace(posMode))
                {
                    return posMode;
                }

                if (dict.TryGetValue("info", out var infoObj))
                {
                    if (infoObj is IDictionary<string, object> infoDict)
                    {
                        posMode = GetString(infoDict, "posMode") ?? GetString(infoDict, "holdMode");
                        if (!string.IsNullOrWhiteSpace(posMode))
                        {
                            return posMode;
                        }
                    }
                }
            }

            try
            {
                dynamic dyn = result;
                var posMode = dyn.posMode ?? dyn.holdMode;
                return posMode?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> ResolveContractSymbolAsync(Exchange exchangeClient, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return symbol;
            }

            var normalized = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            if (normalized.Contains(":", StringComparison.Ordinal))
            {
                return normalized;
            }

            try
            {
                await exchangeClient.loadMarkets().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            try
            {
                dynamic dynamicExchange = exchangeClient;
                var markets = dynamicExchange.markets as Dictionary<string, object>;
                if (markets != null && markets.Count > 0)
                {
                    if (markets.TryGetValue(normalized, out var direct) && IsContractMarket(direct))
                    {
                        return normalized;
                    }

                    var parts = normalized.Split('/');
                    var baseCoin = parts.Length > 0 ? parts[0] : normalized;
                    var quote = parts.Length > 1 ? parts[1] : "USDT";

                    foreach (var entry in markets)
                    {
                        if (entry.Value is not Dictionary<string, object> marketDict)
                        {
                            continue;
                        }

                        if (!IsContractMarket(marketDict))
                        {
                            continue;
                        }

                        var baseId = GetString(marketDict, "baseId") ?? GetString(marketDict, "base");
                        var quoteId = GetString(marketDict, "quoteId") ?? GetString(marketDict, "quote");
                        if (!string.Equals(baseId, baseCoin, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(quoteId, quote, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var marketSymbol = GetString(marketDict, "symbol");
                        if (!string.IsNullOrWhiteSpace(marketSymbol))
                        {
                            return marketSymbol;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (string.Equals(exchangeClient.id?.ToString(), "bitget", StringComparison.OrdinalIgnoreCase))
            {
                return normalized + ":USDT";
            }

            return normalized;
        }

        private static bool IsContractMarket(object? market)
        {
            if (market is not Dictionary<string, object> dict)
            {
                return false;
            }

            return GetBool(dict, "swap") || GetBool(dict, "future") || GetBool(dict, "contract");
        }

        private static string? GetString(IDictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static bool GetBool(IDictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            try
            {
                if (value is bool b) return b;
                if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
        }


        private static bool IsMarketBuyPriceRequired(Exception ex)
        {
            var msg = ex?.Message ?? string.Empty;
            return msg.Contains("requires the price argument for market buy orders", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("createMarketBuyOrderRequiresPrice", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<double?> TryFetchMarketPriceAsync(Exchange exchangeClient, string symbol)
        {
            try
            {
                var ticker = await exchangeClient.fetchTicker(symbol).ConfigureAwait(false);
                var price = TryExtractPrice(ticker);
                if (price.HasValue && price.Value > 0)
                {
                    return price.Value;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static double? TryExtractPrice(object? ticker)
        {
            if (ticker == null)
            {
                return null;
            }

            if (ticker is IDictionary<string, object> dict)
            {
                var ask = TryGetDouble(dict, "ask");
                if (ask.HasValue)
                {
                    return ask.Value;
                }

                var last = TryGetDouble(dict, "last");
                if (last.HasValue)
                {
                    return last.Value;
                }

                var close = TryGetDouble(dict, "close");
                if (close.HasValue)
                {
                    return close.Value;
                }

                var bid = TryGetDouble(dict, "bid");
                if (bid.HasValue)
                {
                    return bid.Value;
                }
            }

            try
            {
                var dynamicTicker = (dynamic)ticker;
                if (dynamicTicker == null)
                {
                    return null;
                }

                double? ask = TryGetDynamicDouble(dynamicTicker, "ask");
                if (ask.HasValue)
                {
                    return ask.Value;
                }

                double? last = TryGetDynamicDouble(dynamicTicker, "last");
                if (last.HasValue)
                {
                    return last.Value;
                }

                double? close = TryGetDynamicDouble(dynamicTicker, "close");
                if (close.HasValue)
                {
                    return close.Value;
                }

                double? bid = TryGetDynamicDouble(dynamicTicker, "bid");
                if (bid.HasValue)
                {
                    return bid.Value;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static double? TryGetDouble(IDictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var raw) || raw == null)
            {
                return null;
            }

            try
            {
                if (raw is double d) return d;
                if (raw is float f) return f;
                if (raw is decimal dec) return (double)dec;
                if (raw is long l) return l;
                if (raw is int i) return i;

                if (raw is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static double? TryGetDynamicDouble(dynamic obj, string memberName)
        {
            try
            {
                var value = obj?.GetType()?.GetProperty(memberName)?.GetValue(obj);
                if (value == null)
                {
                    return null;
                }

                if (value is double d) return d;
                if (value is float f) return f;
                if (value is decimal dec) return (double)dec;
                if (value is long l) return l;
                if (value is int i) return i;

                if (value is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }
    }
}
