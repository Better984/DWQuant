using ccxt;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Services.StrategyRunCheck;
using System.Globalization;

namespace ServerTest.Services.StrategyRunCheck.Checks
{
    public sealed class BalanceRunCheck : IStrategyRunCheck
    {
        private readonly IOptionsMonitor<TradingOptions> _tradingOptions;

        public BalanceRunCheck(IOptionsMonitor<TradingOptions> tradingOptions)
        {
            _tradingOptions = tradingOptions ?? throw new ArgumentNullException(nameof(tradingOptions));
        }

        public async Task<StrategyRunCheckItem> CheckAsync(StrategyRunCheckContext context, CancellationToken ct)
        {
            if (!context.RequireBalanceCheck)
            {
                return new StrategyRunCheckItem
                {
                    Code = "balance",
                    Name = "资金余额校验",
                    Passed = true,
                    Blocker = false,
                    Message = "当前状态无需校验可用余额"
                };
            }

            Exchange? exchangeClient = null;
            try
            {
                exchangeClient = CcxtExchangeFactory.Create(context.Exchange, context.ApiKey);
                CcxtExchangeFactory.ConfigureSandbox(exchangeClient, context.Exchange, _tradingOptions.CurrentValue.EnableSandboxMode);

                var quoteCurrency = ResolveQuoteCurrency(context.Symbol);
                var (free, total) = await FetchBalanceAsync(exchangeClient, quoteCurrency).ConfigureAwait(false);

                if (!free.HasValue || free.Value <= 0)
                {
                    return new StrategyRunCheckItem
                    {
                        Code = "balance",
                        Name = "资金余额校验",
                        Passed = false,
                        Blocker = true,
                        Message = $"可用余额不足：{quoteCurrency} 可用 {FormatNumber(free)}",
                        Detail = new Dictionary<string, object>
                        {
                            ["currency"] = quoteCurrency,
                            ["free"] = free ?? 0,
                            ["total"] = total ?? 0
                        }
                    };
                }

                var requiredMargin = await EstimateRequiredMarginAsync(exchangeClient, context.Symbol, context.OrderQty, context.Leverage)
                    .ConfigureAwait(false);

                if (requiredMargin.HasValue && free.Value < requiredMargin.Value)
                {
                    return new StrategyRunCheckItem
                    {
                        Code = "balance",
                        Name = "资金余额校验",
                        Passed = false,
                        Blocker = true,
                        Message = $"可用余额不足：需要 {FormatNumber(requiredMargin)} {quoteCurrency}",
                        Detail = new Dictionary<string, object>
                        {
                            ["currency"] = quoteCurrency,
                            ["free"] = free.Value,
                            ["requiredMargin"] = requiredMargin.Value,
                            ["leverage"] = context.Leverage
                        }
                    };
                }

                return new StrategyRunCheckItem
                {
                    Code = "balance",
                    Name = "资金余额校验",
                    Passed = true,
                    Blocker = true,
                    Message = $"余额充足：{quoteCurrency} 可用 {FormatNumber(free)}",
                    Detail = new Dictionary<string, object>
                    {
                        ["currency"] = quoteCurrency,
                        ["free"] = free.Value,
                        ["total"] = total ?? 0,
                        ["requiredMargin"] = requiredMargin ?? 0
                    }
                };
            }
            catch (Exception ex)
            {
                return new StrategyRunCheckItem
                {
                    Code = "balance",
                    Name = "资金余额校验",
                    Passed = false,
                    Blocker = true,
                    Message = $"余额校验失败：{ex.Message}"
                };
            }
            finally
            {
                if (exchangeClient is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static string ResolveQuoteCurrency(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return "USDT";
            }

            var normalized = symbol.Trim();
            if (normalized.Contains('/'))
            {
                var parts = normalized.Split('/');
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    return parts[1].Trim().ToUpperInvariant();
                }
            }

            if (normalized.Contains(':'))
            {
                var parts = normalized.Split(':');
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    return parts[1].Trim().ToUpperInvariant();
                }
            }

            return "USDT";
        }

        private static async Task<(decimal? free, decimal? total)> FetchBalanceAsync(Exchange exchangeClient, string currency)
        {
            object? balance = null;
            try
            {
                dynamic dyn = exchangeClient;
                balance = await dyn.fetchBalance(new Dictionary<string, object>
                {
                    ["type"] = "swap"
                });
            }
            catch
            {
                // ignore
            }

            if (balance == null)
            {
                dynamic dyn = exchangeClient;
                balance = await dyn.fetchBalance();
            }

            var free = TryGetBalanceValue(balance, "free", currency);
            var total = TryGetBalanceValue(balance, "total", currency);
            return (free, total);
        }

        private static decimal? TryGetBalanceValue(object? balance, string key, string currency)
        {
            if (balance == null)
            {
                return null;
            }

            if (balance is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(key, out var rawMap))
                {
                    if (rawMap is IDictionary<string, object> map && map.TryGetValue(currency, out var value))
                    {
                        return TryParseDecimal(value);
                    }
                }

                if (dict.TryGetValue(currency, out var currencyObj))
                {
                    if (currencyObj is IDictionary<string, object> currencyDict)
                    {
                        if (currencyDict.TryGetValue(key, out var value))
                        {
                            return TryParseDecimal(value);
                        }
                    }
                }
            }

            try
            {
                dynamic dyn = balance;
                var map = dyn?.GetType()?.GetProperty(key)?.GetValue(dyn);
                if (map is IDictionary<string, object> valueMap && valueMap.TryGetValue(currency, out var value))
                {
                    return TryParseDecimal(value);
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static async Task<decimal?> EstimateRequiredMarginAsync(Exchange exchangeClient, string symbol, decimal qty, int leverage)
        {
            if (qty <= 0)
            {
                return null;
            }

            var price = await TryFetchTickerPriceAsync(exchangeClient, symbol).ConfigureAwait(false);
            if (!price.HasValue || price.Value <= 0)
            {
                return null;
            }

            var effectiveLeverage = Math.Max(1, leverage);
            return qty * price.Value / effectiveLeverage;
        }

        private static async Task<decimal?> TryFetchTickerPriceAsync(Exchange exchangeClient, string symbol)
        {
            try
            {
                var ticker = await exchangeClient.fetchTicker(symbol).ConfigureAwait(false);
                if (ticker is IDictionary<string, object> dict)
                {
                    return TryGetDecimal(dict, "last")
                        ?? TryGetDecimal(dict, "close")
                        ?? TryGetDecimal(dict, "bid")
                        ?? TryGetDecimal(dict, "ask");
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static decimal? TryParseDecimal(object? value)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                if (value is decimal dec) return dec;
                if (value is double d) return Convert.ToDecimal(d, CultureInfo.InvariantCulture);
                if (value is float f) return Convert.ToDecimal(f, CultureInfo.InvariantCulture);
                if (value is long l) return l;
                if (value is int i) return i;
                if (value is string s && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static decimal? TryGetDecimal(IDictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return TryParseDecimal(value);
            }

            return null;
        }

        private static string FormatNumber(decimal? value)
        {
            if (!value.HasValue)
            {
                return "-";
            }

            return value.Value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
