using ccxt;
using ccxt.pro;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Services;
using System.Collections.Concurrent;

namespace ServerTest.Modules.MarketStreaming.Application
{
    public class ExchangePriceService : BaseService
    {
        private readonly ConcurrentDictionary<string, PriceData> _priceCache = new();
        private readonly Dictionary<string, Exchange> _exchanges = new();
        private readonly Dictionary<string, string> _exchangeInitErrors = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _initializationTask;
        private readonly Task _subscriptionTask;
        private readonly TaskCompletionSource<bool> _subscriptionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? _initializationError;

        public ExchangePriceService(ILogger<ExchangePriceService> logger) : base(logger)
        {
            // 初始化交易所与订阅任务（失败必须可感知）
            _initializationTask = InitializeExchangesAsync();
            _subscriptionTask = StartPriceSubscriptionsAsync(_cancellationTokenSource.Token);

            // 后台任务异常需显式记录，避免静默失败
            _ = ObserveBackgroundTaskAsync(_initializationTask, "交易所初始化任务");
            _ = ObserveBackgroundTaskAsync(_subscriptionTask, "价格订阅任务");
        }

        /// <summary>
        /// 等待交易所初始化完成（失败将抛出异常）
        /// </summary>
        public async Task WaitForInitializationAsync()
        {
            await _initializationTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 等待价格订阅任务启动完成（失败将抛出异常）
        /// </summary>
        public async Task WaitForSubscriptionAsync()
        {
            await _subscriptionTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 等待价格订阅完成启动前置检查（不等待订阅任务结束）。
        /// </summary>
        public Task WaitForSubscriptionReadyAsync()
        {
            return _subscriptionReadyTcs.Task;
        }

        /// <summary>
        /// 等待首个价格进入缓存，作为价格推送可用性的就绪信号。
        /// </summary>
        public async Task WaitForFirstPriceAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_priceCache.IsEmpty)
                {
                    return;
                }

                if (_subscriptionTask.IsFaulted)
                {
                    await _subscriptionTask.ConfigureAwait(false);
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 获取初始化失败异常（若失败）
        /// </summary>
        public Exception? GetInitializationError()
        {
            return _initializationError;
        }

        private Task ObserveBackgroundTaskAsync(Task task, string taskName)
        {
            return task.ContinueWith(t =>
            {
                if (!t.IsFaulted || t.Exception == null)
                {
                    return;
                }

                Logger.LogError(t.Exception, "{TaskName}失败", taskName);
            }, TaskScheduler.Default);
        }

        private async Task InitializeExchangesAsync()
        {
            try
            {
                await TryInitializeBinanceAsync().ConfigureAwait(false);
                await TryInitializeOkxAsync().ConfigureAwait(false);
                await TryInitializeBitgetAsync().ConfigureAwait(false);

                if (_exchanges.Count == 0)
                {
                    var failed = _exchangeInitErrors.Count == 0
                        ? "无详细错误"
                        : string.Join("; ", _exchangeInitErrors.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                    var message = $"交易所初始化全部失败，价格服务不可用: {failed}";
                    _initializationError = new InvalidOperationException(message);
                    Logger.LogError(message);
                    throw _initializationError;
                }

                if (_exchangeInitErrors.Count > 0)
                {
                    Logger.LogWarning(
                        "交易所初始化部分失败，已降级继续: ready={ReadyCount} failed={FailedCount} failedList={FailedList}",
                        _exchanges.Count,
                        _exchangeInitErrors.Count,
                        string.Join(", ", _exchangeInitErrors.Keys));
                }

                Logger.LogInformation(
                    "交易所初始化完成: ready={ReadyCount} readyList={ReadyList}",
                    _exchanges.Count,
                    string.Join(", ", _exchanges.Keys));
            }
            catch (Exception ex)
            {
                _initializationError ??= ex;
                Logger.LogError(ex, "交易所初始化失败");
                throw;
            }
        }

        private async Task StartPriceSubscriptionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 等待交易所初始化完成，失败则直接抛出
                await _initializationTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "交易所初始化失败，价格订阅不会启动");
                _subscriptionReadyTcs.TrySetException(ex);
                throw;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _subscriptionReadyTcs.TrySetCanceled(cancellationToken);
                return;
            }

            var tasks = new List<Task>();
            var enabledExchanges = new List<string>();
            var symbols = GetAllowedSymbols().ToArray();

            // 按“可用交易所”降级启动，避免单点初始化失败导致整体订阅不可用。
            if (_exchanges.TryGetValue("Binance", out var binance) && binance is ccxt.pro.binanceusdm binanceEx)
            {
                enabledExchanges.Add("Binance");
                foreach (var symbol in symbols)
                {
                    tasks.Add(WatchBinanceFutures(binanceEx, symbol, cancellationToken));
                }
            }
            if (_exchanges.TryGetValue("OKX", out var okx) && okx is ccxt.pro.okx okxEx)
            {
                enabledExchanges.Add("OKX");
                foreach (var symbol in symbols)
                {
                    tasks.Add(WatchOKX(okxEx, symbol, cancellationToken));
                }
            }
            if (_exchanges.TryGetValue("Bitget", out var bitget) && bitget is ccxt.pro.bitget bitgetEx)
            {
                enabledExchanges.Add("Bitget");
                foreach (var symbol in symbols)
                {
                    tasks.Add(WatchBitget(bitgetEx, symbol, cancellationToken));
                }
            }

            if (tasks.Count == 0)
            {
                var ready = _exchanges.Count == 0 ? "无" : string.Join(", ", _exchanges.Keys);
                var failed = _exchangeInitErrors.Count == 0
                    ? "无"
                    : string.Join("; ", _exchangeInitErrors.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                var message = $"价格订阅任务为空，无法启动价格推送: ready={ready}; failed={failed}";
                Logger.LogError(message);
                var ex = new InvalidOperationException(message);
                _subscriptionReadyTcs.TrySetException(ex);
                throw ex;
            }

            Logger.LogInformation(
                "价格订阅任务已启动: exchangeCount={ExchangeCount} taskCount={TaskCount} exchanges={Exchanges}",
                enabledExchanges.Count,
                tasks.Count,
                string.Join(", ", enabledExchanges));

            _subscriptionReadyTcs.TrySetResult(true);
            await Task.WhenAll(tasks);
        }

        private async Task TryInitializeBinanceAsync()
        {
            ccxt.pro.binanceusdm? exchange = null;
            try
            {
                exchange = new ccxt.pro.binanceusdm(new Dictionary<string, object>());
                await exchange.LoadMarkets().ConfigureAwait(false);
                _exchanges["Binance"] = exchange;
                Logger.LogInformation("交易所初始化成功: Binance");
            }
            catch (Exception ex)
            {
                RecordExchangeInitFailure("Binance", ex);
                DisposeExchangeSafely(exchange, "Binance");
            }
        }

        private async Task TryInitializeOkxAsync()
        {
            ccxt.pro.okx? exchange = null;
            try
            {
                exchange = new ccxt.pro.okx(new Dictionary<string, object>()
                {
                    { "options", new Dictionary<string, object>()
                        {
                            { "defaultType", "swap" }  // OKX 需要指定合约类型
                        } }
                });
                await exchange.LoadMarkets().ConfigureAwait(false);
                _exchanges["OKX"] = exchange;
                Logger.LogInformation("交易所初始化成功: OKX");
            }
            catch (Exception ex)
            {
                RecordExchangeInitFailure("OKX", ex);
                DisposeExchangeSafely(exchange, "OKX");
            }
        }

        private async Task TryInitializeBitgetAsync()
        {
            ccxt.pro.bitget? exchange = null;
            try
            {
                exchange = new ccxt.pro.bitget(new Dictionary<string, object>()
                {
                    { "options", new Dictionary<string, object>()
                        {
                            { "defaultType", "swap" }  // Bitget 需要指定合约类型
                        } }
                });
                await exchange.LoadMarkets().ConfigureAwait(false);
                _exchanges["Bitget"] = exchange;
                Logger.LogInformation("交易所初始化成功: Bitget");
            }
            catch (Exception ex)
            {
                RecordExchangeInitFailure("Bitget", ex);
                DisposeExchangeSafely(exchange, "Bitget");
            }
        }

        private void RecordExchangeInitFailure(string exchangeName, Exception ex)
        {
            var error = ex.Message;
            _exchangeInitErrors[exchangeName] = error;
            Logger.LogWarning(ex, "交易所初始化失败，已降级继续: exchange={Exchange} error={Error}", exchangeName, error);
        }

        private void DisposeExchangeSafely(Exchange? exchange, string exchangeName)
        {
            if (exchange is not IDisposable disposable)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "释放交易所资源失败: exchange={Exchange}", exchangeName);
            }
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
                        var market = markets[symbol];
                        if (market is Dictionary<string, object> marketDict)
                        {
                            // 检查是否是合约市场
                            var isSwap = marketDict.ContainsKey("swap") && marketDict["swap"] is bool swap && swap;
                            var isFuture = marketDict.ContainsKey("future") && marketDict["future"] is bool future && future;
                            var isContract = marketDict.ContainsKey("contract") && marketDict["contract"] is bool contract && contract;

                            if (isSwap || isFuture || isContract)
                            {
                                return symbol;
                            }
                        }
                    }
                }

                // 如果直接查找失败，遍历所有市场查找 BTC 相关的合约
                foreach (var marketEntry in markets)
                {
                    if (marketEntry.Value is Dictionary<string, object> marketDict)
                    {
                        marketDict.TryGetValue("baseId", out var baseIdObj);
                        string? baseId = baseIdObj as string;
                        
                        var isSwap = marketDict.ContainsKey("swap") && marketDict["swap"] is bool swap && swap;
                        var isFuture = marketDict.ContainsKey("future") && marketDict["future"] is bool future && future;
                        var isContract = marketDict.ContainsKey("contract") && marketDict["contract"] is bool contract && contract;
                        
                        marketDict.TryGetValue("quote", out var quoteObj);
                        string? quote = quoteObj as string;
                        
                        marketDict.TryGetValue("quoteId", out var quoteIdObj);
                        string? quoteId = quoteIdObj as string;

                        if (baseId == baseAsset && (isSwap || isFuture || isContract))
                        {
                            if (quote == "USDT" || quoteId == "USDT")
                            {
                                marketDict.TryGetValue("symbol", out var symbolObj);
                                return symbolObj as string ?? baseSymbol + ":USDT";
                            }
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

                    //Console.WriteLine(
                    //    $"[币安合约] {symbol} {timeframe} " +
                    //    $"时间: {ts:yyyy-MM-dd HH:mm:ss} " +
                    //    $"开: {last.open} 高: {last.high} 低: {last.low} 收: {last.close} 量: {last.volume}");

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

                    //Logger.LogDebug(
                    //    "[OKX合约] {Symbol} {Timeframe} 时间:{Timestamp:yyyy-MM-dd HH:mm:ss} 开:{Open} 高:{High} 低:{Low} 收:{Close} 量:{Volume}",
                    //    symbol,
                    //    timeframe,
                    //    ts,
                    //    last.open,
                    //    last.high,
                    //    last.low,
                    //    last.close,
                    //    last.volume);

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

                    //Logger.LogDebug(
                    //    "[Bitget合约] {Symbol} {Timeframe} 时间:{Timestamp:yyyy-MM-dd HH:mm:ss} 开:{Open} 高:{High} 低:{Low} 收:{Close} 量:{Volume}",
                    //    symbol,
                    //    timeframe,
                    //    ts,
                    //    last.open,
                    //    last.high,
                    //    last.low,
                    //    last.close,
                    //    last.volume);

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
