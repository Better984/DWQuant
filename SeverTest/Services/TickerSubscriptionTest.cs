using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ccxt;
using ccxt.pro;

namespace ServerTest.Services
{
    /// <summary>
    /// Ticker WebSocket 订阅测试
    /// 使用 WebSocket 订阅多个交易所的 ticker 数据，每次收到消息打印出来
    /// </summary>
    public static class TickerSubscriptionTest
    {
        /// <summary>
        /// 入口方法：订阅多个交易所的 ticker 数据
        /// 注意：这是测试方法，不会自动在服务器启动时执行，需要你手动调用。
        /// 比如可以暂时在 Program.cs 顶部加一行：await TickerSubscriptionTest.RunAsync();
        /// </summary>
        public static async Task RunAsync()
        {
            Console.WriteLine("开始订阅 Binance / OKX / Bitget 合约 Ticker (WebSocket)...\n");

            var cancellationTokenSource = new CancellationTokenSource();
            var exchanges = new Dictionary<string, Exchange>();

            try
            {
                // 初始化交易所
                Console.WriteLine("[Init] 初始化交易所...");

                // 创建币安合约 WebSocket 连接
                var binanceFutures = new ccxt.pro.binanceusdm(new Dictionary<string, object>() { });
                exchanges["Binance"] = binanceFutures;

                // 创建 OKX WebSocket 连接
                var okx = new ccxt.pro.okx(new Dictionary<string, object>() {
                    { "options", new Dictionary<string, object>() {
                        { "defaultType", "swap" }  // OKX 需要指定合约类型
                    } }
                });
                exchanges["OKX"] = okx;

                // 创建 Bitget WebSocket 连接
                var bitget = new ccxt.pro.bitget(new Dictionary<string, object>() {
                    { "options", new Dictionary<string, object>() {
                        { "defaultType", "swap" }  // Bitget 需要指定合约类型
                    } }
                });
                exchanges["Bitget"] = bitget;

                // 加载市场数据
                Console.WriteLine("[Init] 加载市场数据...");
                await binanceFutures.LoadMarkets();
                await okx.LoadMarkets();
                await bitget.LoadMarkets();
                Console.WriteLine("[Init] 市场数据加载完成\n");

                // 启动订阅任务
                var tasks = new List<Task>();

                // 订阅币安合约 ticker
                //if (exchanges.ContainsKey("Binance") && exchanges["Binance"] is ccxt.pro.binanceusdm binanceEx)
                //{
                //    tasks.Add(WatchBinanceTicker(binanceEx, cancellationTokenSource.Token));
                //}

                //// 订阅 OKX 合约 ticker
                //if (exchanges.ContainsKey("OKX") && exchanges["OKX"] is ccxt.pro.okx okxEx)
                //{
                //    tasks.Add(WatchOKXTicker(okxEx, cancellationTokenSource.Token));
                //}

                // 订阅 Bitget 合约 ticker
                if (exchanges.ContainsKey("Bitget") && exchanges["Bitget"] is ccxt.pro.bitget bitgetEx)
                {
                    tasks.Add(WatchBitgetTicker(bitgetEx, cancellationTokenSource.Token));
                }

                // 等待所有任务（实际上会一直运行直到取消）
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 发生错误: {ex.Message}");
                Console.WriteLine($"[Error] 堆栈跟踪: {ex.StackTrace}");
            }
            finally
            {
                // 清理资源
                cancellationTokenSource.Cancel();
                foreach (var exchange in exchanges.Values)
                {
                    try
                    {
                        if (exchange is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Cleanup] 释放交易所资源时出错: {ex.Message}");
                    }
                }
                Console.WriteLine("\n[Cleanup] 测试结束，资源已清理");
            }
        }

        // 查找合约符号
        private static string FindFuturesSymbol(Exchange exchange, string baseSymbol)
        {
            try
            {
                // 使用动态类型访问 markets
                dynamic dynamicExchange = exchange;
                var markets = dynamicExchange.markets as Dictionary<string, object>;
                
                if (markets == null)
                {
                    Console.WriteLine($"[{exchange.id}] markets 为空，使用默认符号");
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
                    
                    if (baseId == "BTC" && (isSwap || isFuture || isContract))
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
                Console.WriteLine($"[{exchange.id}] 查找合约符号时出错: {ex.Message}");
            }

            // 默认返回
            return baseSymbol + ":USDT";
        }

        // 订阅币安合约 ticker
        private static async Task WatchBinanceTicker(ccxt.pro.binanceusdm exchange, CancellationToken cancellationToken)
        {
            var symbol = FindFuturesSymbol(exchange, "BTC/USDT");
            Console.WriteLine($"[币安合约] 开始订阅 {symbol} Ticker...\n");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // WebSocket 订阅 ticker
                    var ticker = await exchange.WatchTicker(symbol);

                    var timestamp = ticker.timestamp != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)ticker.timestamp).LocalDateTime
                        : DateTime.Now;

                    Console.WriteLine(
                        $"[币安合约] {symbol} Ticker | " +
                        $"时间: {timestamp:yyyy-MM-dd HH:mm:ss} | " +
                        $"最新价: {ticker.last} | " +
                        $"买一价: {ticker.bid} | " +
                        $"卖一价: {ticker.ask} | " +
                        $"24h高: {ticker.high} | " +
                        $"24h低: {ticker.low} | " +
                        $"24h涨跌: {ticker.change} | " +
                        $"24h涨跌幅: {ticker.percentage}% | " +
                        $"24h成交量: {ticker.baseVolume}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[币安合约] 订阅 Ticker 错误: {ex.Message}");
                    await Task.Delay(2000, cancellationToken); // 等待2秒后重试
                }
            }
        }

        // 订阅 OKX 合约 ticker
        private static async Task WatchOKXTicker(ccxt.pro.okx exchange, CancellationToken cancellationToken)
        {
            var symbol = FindFuturesSymbol(exchange, "BTC/USDT");
            Console.WriteLine($"[OKX合约] 开始订阅 {symbol} Ticker...\n");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ticker = await exchange.WatchTicker(symbol);
                    
                    var timestamp = ticker.timestamp != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)ticker.timestamp).LocalDateTime
                        : DateTime.Now;

                    Console.WriteLine(
                        $"[OKX合约] {symbol} Ticker | " +
                        $"时间: {timestamp:yyyy-MM-dd HH:mm:ss} | " +
                        $"最新价: {ticker.last} | " +
                        $"买一价: {ticker.bid} | " +
                        $"卖一价: {ticker.ask} | " +
                        $"24h高: {ticker.high} | " +
                        $"24h低: {ticker.low} | " +
                        $"24h涨跌: {ticker.change} | " +
                        $"24h涨跌幅: {ticker.percentage}% | " +
                        $"24h成交量: {ticker.baseVolume}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OKX合约] 订阅 Ticker 错误: {ex.Message}");
                    await Task.Delay(2000, cancellationToken); // 等待2秒后重试
                }
            }
        }

        // 订阅 Bitget 合约 ticker
        private static async Task WatchBitgetTicker(ccxt.pro.bitget exchange, CancellationToken cancellationToken)
        {
            var symbol = FindFuturesSymbol(exchange, "XRP/USDT");
            Console.WriteLine($"[Bitget合约] 开始订阅 {symbol} Ticker...\n");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ticker = await exchange.WatchTicker(symbol);

                    var timestamp = ticker.timestamp != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)ticker.timestamp).LocalDateTime
                        : DateTime.Now;

                    Console.WriteLine(
                        $"[Bitget合约] {symbol} Ticker | " +
                        $"时间: {timestamp:yyyy-MM-dd HH:mm:ss} | " +
                        $"最新价: {ticker.last} | " +
                        $"买一价: {ticker.bid} | " +
                        $"卖一价: {ticker.ask} | " +
                        $"24h高: {ticker.high} | " +
                        $"24h低: {ticker.low} | " +
                        $"24h涨跌: {ticker.change} | " +
                        $"24h涨跌幅: {ticker.percentage}% | " +
                        $"24h成交量: {ticker.baseVolume}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bitget合约] 订阅 Ticker 错误: {ex.Message}");
                    await Task.Delay(2000, cancellationToken); // 等待2秒后重试
                }
            }
        }
    }
}
