using ccxt;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Repositories;

namespace ServerTest.Services
{
    public sealed class OrderExecutionRequest
    {
        public long Uid { get; init; }
        public long? ExchangeApiKeyId { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty; // buy/sell
        public decimal Qty { get; init; }
        public bool ReduceOnly { get; init; }
    }

    public sealed class OrderExecutionResult
    {
        public bool Success { get; init; }
        public string? ExchangeOrderId { get; init; }
        public decimal? AveragePrice { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public interface IOrderExecutor
    {
        Task<OrderExecutionResult> PlaceMarketOrderAsync(OrderExecutionRequest request, CancellationToken ct);
    }

    public sealed class CcxtOrderExecutor : IOrderExecutor
    {
        private readonly UserExchangeApiKeyRepository _apiKeyRepository;
        private readonly ILogger<CcxtOrderExecutor> _logger;

        public CcxtOrderExecutor(UserExchangeApiKeyRepository apiKeyRepository, ILogger<CcxtOrderExecutor> logger)
        {
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var apiKey = await ResolveApiKeyAsync(request, exchangeType, ct).ConfigureAwait(false);
            if (apiKey == null)
            {
                return new OrderExecutionResult { Success = false, ErrorMessage = "Exchange API key not found" };
            }

            Exchange? exchangeClient = null;
            try
            {
                exchangeClient = CreateExchange(exchangeType, apiKey);
                var parameters = new Dictionary<string, object>();
                if (request.ReduceOnly)
                {
                    parameters["reduceOnly"] = true;
                }

                var order = await exchangeClient.createOrder(
                    request.Symbol,
                    "market",
                    request.Side,
                    (double)request.Qty,
                    null,
                    parameters).ConfigureAwait(false);

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
                _logger.LogError(ex, "CCXT order failed: exchange={Exchange} symbol={Symbol} side={Side}", exchangeType, request.Symbol, request.Side);
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

        private static Exchange CreateExchange(string exchangeType, UserExchangeApiKeyRecord apiKey)
        {
            var options = new Dictionary<string, object>
            {
                ["apiKey"] = apiKey.ApiKey,
                ["secret"] = apiKey.ApiSecret,
                ["enableRateLimit"] = true,
                ["options"] = new Dictionary<string, object>
                {
                    ["defaultType"] = "swap"
                }
            };

            if (!string.IsNullOrWhiteSpace(apiKey.ApiPassword))
            {
                options["password"] = apiKey.ApiPassword;
            }

            return exchangeType switch
            {
                "binance" => new binanceusdm(options),
                "okx" => new okx(options),
                "bitget" => new bitget(options),
                "bybit" => new bybit(options),
                "gate" => new gate(options),
                _ => throw new NotSupportedException($"Exchange not supported: {exchangeType}")
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
    }
}
