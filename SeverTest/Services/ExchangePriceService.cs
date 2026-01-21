using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ccxt;
using ccxt.pro;
using Microsoft.Extensions.Logging;
using ServerTest.Models;

namespace ServerTest.Services
{
    public class ExchangePriceService : BaseService
    {
        private readonly ConcurrentDictionary<string, PriceData> _priceCache = new();
        private readonly Dictionary<string, Exchange> _exchanges = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public ExchangePriceService(ILogger<ExchangePriceService> logger) : base(logger)
        {
            // 初始化交易所
            _ = Task.Run(async () => await InitializeExchangesAsync());

            // 启动价格订阅
            _ = Task.Run(() => StartPriceSubscriptionsAsync(_cancellationTokenSource.Token));
        }

        private async Task InitializeExchangesAsync()
        {
            try
            {
                // 创建币安合约 WebSocket 连接
                var binanceFutures = new ccxt.pro.binanceusdm(new Dictionary<string, object>() { });
                _exchanges["Binance"] = binanceFutures;

                // 创建 OKX WebSocket 连接
                var okx = new ccxt.pro.okx(new Dictionary<string, object>() {
                    { "options", new Dictionary<string, object>() {
                        { "defaultType", "swap" }  // OKX 需要指定合约类型
                    } }
                });
                _exchanges["OKX"] = okx;

                // 创建 Bitget WebSocket 连接
                var bitget = new ccxt.pro.bitget(new Dictionary<string, object>() {
                    { "options", new Dictionary<string, object>() {
                        { "defaultType", "swap" }  // Bitget 需要指定合约类型
                    } }
                });
                _exchanges["Bitget"] = bitget;

                // 加载市场数据
                await binanceFutures.LoadMarkets();
                await okx.LoadMarkets();
                await bitget.LoadMarkets();

                Logger.LogInformation("交易所初始化完成，市场数据加载完成 (使用 WebSocket)");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "交易所初始化失败");
            }
        }

        private async Task StartPriceSubscriptionsAsync(CancellationToken cancellationToken)
        {
            // 等待交易所初始化完成
            while (_exchanges.Count < 3 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            var tasks = new List<Task>();

            // 并行订阅 Binance 多币种（其他交易所暂不启用）
            if (_exchanges.ContainsKey("Binance") && _exchanges["Binance"] is ccxt.pro.binanceusdm binanceEx)
            {
                foreach (var symbol in GetAllowedSymbols())
                {
                    tasks.Add(WatchBinanceFutures(binanceEx, symbol, cancellationToken));
                }
            }
            //if (_exchanges.ContainsKey("OKX") && _exchanges["OKX"] is ccxt.pro.okx okxEx)
            //{
            //    tasks.Add(WatchOKX(okxEx, cancellationToken));
            //}
            //if (_exchanges.ContainsKey("Bitget") && _exchanges["Bitget"] is ccxt.pro.bitget bitgetEx)
            //{
            //    tasks.Add(WatchBitget(bitgetEx, cancellationToken));
            //}

            await Task.WhenAll(tasks);
        }

        // 查找合约符号
        private string FindFuturesSymbol(Exchange exchange, string baseSymbol)
        {
            try
            {
                // 使用动态类型访问 markets
                dynamic dynamicExchange = exchange;
                var markets = dynamicExchange.markets as Dictionary<string, object>;
                var baseAsset = baseSymbol.Split('/')[0];
                
                if (markets == null)
                {
                    Logger.LogWarning($"[{exchange.id}] markets 为空，使用默认符号");
                    return baseSymbol + ":USDT";
                }

                // 尝试不同的符号格式
                var possibleSymbols = new List<string>
                {
                    baseSymbol,                    // BTC/USDT
                    baseSymbol + ":USDT",          // BTC/USDT:USDT
                    baseSymbol.Replace("/", "")    // BTCUSDT
                };

                foreach (var symbol in possibleSymbols)
                {
                    if (markets.ContainsKey(symbol))
                    {
                        dynamic market = markets[symbol];
                        // 检查是否是合约市场
                        bool isSwap = market.swap == true;
                        bool isFuture = market.future == true;
                        bool isContract = market.contract == true;
                        
                        if (isSwap || isFuture || isContract)
                        {
                            return symbol;
                        }
                    }
                }

                // 如果直接查找失败，遍历所有市场查找 BTC 相关的合约
                foreach (var marketEntry in markets)
                {
                    dynamic market = marketEntry.Value;
                    string? baseId = market.baseId;
                    bool isSwap = market.swap == true;
                    bool isFuture = market.future == true;
                    bool isContract = market.contract == true;
                    string? quote = market.quote;
                    string? quoteId = market.quoteId;
                    
                    if (baseId == baseAsset && (isSwap || isFuture || isContract))
                    {
                        if (quote == "USDT" || quoteId == "USDT")
                        {
                            return market.symbol;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"[{exchange.id}] 查找合约符号时出错");
            }

            // 默认返回
            return baseSymbol + ":USDT";
        }

        // 订阅币安合约 1 分钟 K 线
        private async Task WatchBinanceFutures(ccxt.pro.binanceusdm exchange, string baseSymbol, CancellationToken cancellationToken)
        {
            var symbol = FindFuturesSymbol(exchange, baseSymbol);
            var normalizedSymbol = NormalizeSymbolKey(symbol);
            var timeframe = "1m";
            Logger.LogInformation($"[币安合约] 开始订阅 {symbol} {timeframe} K线...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // WebSocket 订阅 1m K 线
                    var ohlcvs = await exchange.WatchOHLCV(symbol, timeframe);
                    if (ohlcvs == null || ohlcvs.Count == 0)
                    {
                        continue;
                    }

                    var last = ohlcvs[^1]; // 最新一根
                    var ts = last.timestamp != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)last.timestamp).LocalDateTime
                        : DateTime.Now;

                    Console.WriteLine(
                        $"[币安合约] {symbol} {timeframe} " +
                        $"时间: {ts:yyyy-MM-dd HH:mm:ss} " +
                        $"开: {last.open} 高: {last.high} 低: {last.low} 收: {last.close} 量: {last.volume}");

                    var priceData = new PriceData
                    {
                        Exchange = "Binance",
                        Symbol = normalizedSymbol,
                        Price = last.close != null ? Convert.ToDecimal(last.close) : 0m,
                        Timestamp = ts,
                        Volume = last.volume != null ? Convert.ToDecimal(last.volume) : null,
                        High24h = null,
                        Low24h = null
                    };

                    _priceCache.AddOrUpdate(normalizedSymbol, priceData, (key, oldValue) => priceData);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[币安合约] 订阅 1m K线 错误");
                    await Task.Delay(2000, cancellationToken); // 等待2秒后重试
                }
            }
        }

        // 订阅 OKX 合约 1 分钟 K 线
        private async Task WatchOKX(ccxt.pro.okx exchange, string baseSymbol, CancellationToken cancellationToken)
        {
            var symbol = FindFuturesSymbol(exchange, baseSymbol);
            var normalizedSymbol = NormalizeSymbolKey(symbol);
            var timeframe = "1m";
            Logger.LogInformation($"[OKX合约] 开始订阅 {symbol} {timeframe} K线...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ohlcvs = await exchange.WatchOHLCV(symbol, timeframe);
                    if (ohlcvs == null || ohlcvs.Count == 0)
                    {
                        continue;
                    }

                    var last = ohlcvs[^1];
                    var ts = last.timestamp != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)last.timestamp).LocalDateTime
                        : DateTime.Now;

                    Console.WriteLine(
                        $"[OKX合约] {symbol} {timeframe} " +
                        $"时间: {ts:yyyy-MM-dd HH:mm:ss} " +
                        $"开: {last.open} 高: {last.high} 低: {last.low} 收: {last.close} 量: {last.volume}");

                    var priceData = new PriceData
                    {
                        Exchange = "OKX",
                        Symbol = normalizedSymbol,
                        Price = last.close != null ? Convert.ToDecimal(last.close) : 0m,
                        Timestamp = ts,
                        Volume = last.volume != null ? Convert.ToDecimal(last.volume) : null,
                        High24h = null,
                        Low24h = null
                    };

                    _priceCache.AddOrUpdate(normalizedSymbol, priceData, (key, oldValue) => priceData);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[OKX合约] 订阅 1m K线 错误");
                    await Task.Delay(2000, cancellationToken); // 等待2秒后重试
                }
            }
        }

        // 订阅 Bitget 合约 1 分钟 K 线
        private async Task WatchBitget(ccxt.pro.bitget exchange, string baseSymbol, CancellationToken cancellationToken)
        {
            var symbol = FindFuturesSymbol(exchange, baseSymbol);
            var normalizedSymbol = NormalizeSymbolKey(symbol);
            var timeframe = "1m";
            Logger.LogInformation($"[Bitget合约] 开始订阅 {symbol} {timeframe} K线...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ohlcvs = await exchange.WatchOHLCV(symbol, timeframe);
                    if (ohlcvs == null || ohlcvs.Count == 0)
                    {
                        continue;
                    }

                    var last = ohlcvs[^1];
                    var ts = last.timestamp != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)last.timestamp).LocalDateTime
                        : DateTime.Now;

                    Console.WriteLine(
                        $"[Bitget合约] {symbol} {timeframe} " +
                        $"时间: {ts:yyyy-MM-dd HH:mm:ss} " +
                        $"开: {last.open} 高: {last.high} 低: {last.low} 收: {last.close} 量: {last.volume}");

                    var priceData = new PriceData
                    {
                        Exchange = "Bitget",
                        Symbol = normalizedSymbol,
                        Price = last.close != null ? Convert.ToDecimal(last.close) : 0m,
                        Timestamp = ts,
                        Volume = last.volume != null ? Convert.ToDecimal(last.volume) : null,
                        High24h = null,
                        Low24h = null
                    };

                    _priceCache.AddOrUpdate(normalizedSymbol, priceData, (key, oldValue) => priceData);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[Bitget合约] 订阅 1m K线 错误");
                    await Task.Delay(2000, cancellationToken); // 等待2秒后重试
                }
            }
        }

        private static IEnumerable<string> GetAllowedSymbols()
        {
            foreach (var symbolEnum in Enum.GetValues<MarketDataConfig.SymbolEnum>())
            {
                yield return MarketDataConfig.SymbolToString(symbolEnum);
            }
        }

        private static string NormalizeSymbolKey(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return string.Empty;
            }

            var trimmed = symbol.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                trimmed = trimmed.Substring(0, colonIndex);
            }

            return trimmed;
        }

        private void OutputPrices(object? state)
        {
            Console.Clear();
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine($"BTC/USDT 合约实时价格 (WebSocket) - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();

            var exchanges = new[] { "Binance", "OKX", "Bitget" };
            foreach (var exchangeName in exchanges)
            {
                if (_priceCache.TryGetValue(exchangeName, out var priceData))
                {
                    Console.WriteLine($"{exchangeName,-10} | 价格: {priceData.Price,15:F2} USDT | " +
                                    $"24h高: {priceData.High24h?.ToString("F2") ?? "N/A",10} | " +
                                    $"24h低: {priceData.Low24h?.ToString("F2") ?? "N/A",10} | " +
                                    $"更新时间: {priceData.Timestamp:HH:mm:ss}");
                }
                else
                {
                    Console.WriteLine($"{exchangeName,-10} | 价格: {"连接中...",15}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=".PadRight(80, '='));
        }

        public Dictionary<string, PriceData> GetAllPrices()
        {
            return _priceCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public PriceData? GetPrice(string exchangeName)
        {
            if (_priceCache.TryGetValue(exchangeName, out var priceData))
            {
                return priceData;
            }

            return _priceCache.Values.FirstOrDefault(p =>
                string.Equals(p.Exchange, exchangeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Symbol, exchangeName, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            
            foreach (var exchange in _exchanges.Values)
            {
                try
                {
                    // CCXT Exchange 可能实现了 IDisposable
                    if (exchange is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "释放交易所资源时出错");
                }
            }
        }
    }
}
