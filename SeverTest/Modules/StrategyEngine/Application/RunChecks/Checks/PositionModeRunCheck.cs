using ccxt;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Modules.StrategyEngine.Application.RunChecks;
using ServerTest.Models;
using ServerTest.Modules.TradingExecution.Infrastructure;

namespace ServerTest.Modules.StrategyEngine.Application.RunChecks.Checks
{
    public sealed class PositionModeRunCheck : IStrategyRunCheck
    {
        private readonly IOptionsMonitor<TradingOptions> _tradingOptions;

        public PositionModeRunCheck(IOptionsMonitor<TradingOptions> tradingOptions)
        {
            _tradingOptions = tradingOptions ?? throw new ArgumentNullException(nameof(tradingOptions));
        }

        public async Task<StrategyRunCheckItem> CheckAsync(StrategyRunCheckContext context, CancellationToken ct)
        {
            if (!context.RequirePositionModeCheck)
            {
                return new StrategyRunCheckItem
                {
                    Code = "position_mode",
                    Name = "仓位模式校验",
                    Passed = true,
                    Blocker = false,
                    Message = "当前状态无需校验仓位模式"
                };
            }

            Exchange? exchangeClient = null;
            Exception? lastError = null;
            try
            {
                exchangeClient = CcxtExchangeFactory.Create(context.Exchange, context.ApiKey);
                CcxtExchangeFactory.ConfigureSandbox(exchangeClient, context.Exchange, _tradingOptions.CurrentValue.EnableSandboxMode);

                var detected = await TryFetchMarginModeAsync(
                    exchangeClient,
                    context.Symbol,
                    ex => lastError = ex).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(detected))
                {
                    return new StrategyRunCheckItem
                    {
                        Code = "position_mode",
                        Name = "仓位模式校验",
                        Passed = false,
                        Blocker = true,
                        Message = "无法获取交易所仓位模式，请检查账户设置",
                        Detail = new Dictionary<string, object>
                        {
                            ["expected"] = NormalizeMode(context.PositionMode),
                            ["exchangeError"] = lastError?.Message ?? string.Empty
                        }
                    };
                }

                var expected = NormalizeMode(context.PositionMode);
                var normalizedDetected = NormalizeMode(detected);
                var passed = string.Equals(expected, normalizedDetected, StringComparison.OrdinalIgnoreCase);

                return new StrategyRunCheckItem
                {
                    Code = "position_mode",
                    Name = "仓位模式校验",
                    Passed = passed,
                    Blocker = true,
                    Message = passed
                        ? $"仓位模式一致：{normalizedDetected}"
                        : $"仓位模式不一致：当前{normalizedDetected}，期望{expected}",
                    Detail = new Dictionary<string, object>
                    {
                        ["expected"] = expected,
                        ["detected"] = normalizedDetected
                    }
                };
            }
            catch (Exception ex)
            {
                return new StrategyRunCheckItem
                {
                    Code = "position_mode",
                    Name = "仓位模式校验",
                    Passed = false,
                    Blocker = true,
                    Message = $"仓位模式校验失败：{ex.Message}",
                    Detail = new Dictionary<string, object>
                    {
                        ["exchangeError"] = ex.Message
                    }
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

        private static async Task<string?> TryFetchMarginModeAsync(
            Exchange exchangeClient,
            string symbol,
            Action<Exception>? onError)
        {
            var effectiveSymbol = await ResolveContractSymbolAsync(exchangeClient, symbol).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(effectiveSymbol))
            {
                try
                {
                    dynamic dyn = exchangeClient;
                    var result = await dyn.fetchMarginMode(effectiveSymbol, new Dictionary<string, object>
                    {
                        ["type"] = "swap"
                    });
                    var mode = TryResolveModeFromObject(result);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        return mode;
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                }
            }

            object? positions = null;
            try
            {
                dynamic dyn = exchangeClient;
                positions = await dyn.fetchPositions();
            }
            catch
            {
                // ignore
            }

            if (positions == null)
            {
                try
                {
                    dynamic dyn = exchangeClient;
                    var parameters = new Dictionary<string, object>
                    {
                        ["type"] = "swap"
                    };
                    positions = await dyn.fetchPositions(null, parameters);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                }
            }

            var positionMode = TryResolveModeFromPositions(positions);
            if (!string.IsNullOrWhiteSpace(positionMode))
            {
                return positionMode;
            }

            try
            {
                dynamic dyn = exchangeClient;
                if (string.IsNullOrWhiteSpace(effectiveSymbol))
                {
                    return null;
                }

                var modeResult = await dyn.fetchMarginMode(effectiveSymbol, new Dictionary<string, object>
                {
                    ["type"] = "swap"
                });
                positionMode = TryResolveModeFromObject(modeResult);
                if (!string.IsNullOrWhiteSpace(positionMode))
                {
                    return positionMode;
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }

            return null;
        }

        private static async Task<string?> ResolveContractSymbolAsync(Exchange exchangeClient, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
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

        private static string? TryResolveModeFromPositions(object? positions)
        {
            if (positions == null)
            {
                return null;
            }

            if (positions is IEnumerable<object> list)
            {
                foreach (var item in list)
                {
                    var mode = TryResolveModeFromObject(item);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        return mode;
                    }
                }
            }

            return TryResolveModeFromObject(positions);
        }

        private static string? TryResolveModeFromObject(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is IDictionary<string, object> dict)
            {
                var mode = GetString(dict, "marginMode")
                           ?? GetString(dict, "marginType")
                           ?? GetString(dict, "margin_mode");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }

                if (dict.TryGetValue("info", out var infoObj))
                {
                    return TryResolveModeFromObject(infoObj);
                }
            }

            try
            {
                dynamic dyn = value;
                var mode = dyn.marginMode ?? dyn.marginType;
                if (mode != null)
                {
                    return mode.ToString();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string? GetString(IDictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value) && value != null)
            {
                return value.ToString();
            }

            return null;
        }

        private static bool IsContractMarket(object? market)
        {
            if (market is not Dictionary<string, object> dict)
            {
                return false;
            }

            return GetBool(dict, "swap") || GetBool(dict, "future") || GetBool(dict, "contract");
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            try
            {
                if (value is bool b) return b;
                if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
                return Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeMode(string? raw)
        {
            var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
            if (value.Contains("cross"))
            {
                return "Cross";
            }

            if (value.Contains("isolated") || value.Contains("isolate"))
            {
                return "Isolated";
            }

            if (value.Contains("longshort") || value.Contains("hedge"))
            {
                return "Cross";
            }

            if (string.Equals(value, "全仓", StringComparison.OrdinalIgnoreCase))
            {
                return "Cross";
            }

            if (string.Equals(value, "逐仓", StringComparison.OrdinalIgnoreCase))
            {
                return "Isolated";
            }

            return raw?.Trim() ?? string.Empty;
        }
    }
}
