using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Models.Market;
using ServerTest.Options;
using ServerTest.Services;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ServerTest.Modules.MarketStreaming.Application
{
    /// <summary>
    /// 每个交易对的缓存，包含独立锁和所有周期的数据
    /// </summary>
    internal class SymbolCache
    {
        public readonly object Lock = new object();
        public readonly Dictionary<string, List<OHLCV>> Timeframes = new();

        // 用于增量聚合：记录每个周期当前正在构建的 K 线
        public readonly Dictionary<string, OHLCV?> CurrentBuckets = new();
    }

    /// <summary>
    /// 实时行情引擎：使用 WebSocket 订阅 1 分钟 K 线，实时维护历史行情缓存
    /// </summary>
    public class MarketDataEngine : BaseService
    {
        // 缓存结构：Exchange -> Symbol -> SymbolCache（包含独立锁和所有周期数据）
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SymbolCache>> _cache = new();

        // 交易所实例：ExchangeId -> Exchange
        private readonly Dictionary<string, Exchange> _exchanges = new();

        // WebSocket 交易所实例：ExchangeId -> Exchange (ccxt.pro)
        private readonly Dictionary<string, Exchange> _wsExchanges = new();

        // WebSocket 监听任务管理（跟踪所有后台任务）
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, Task> _watchTasks = new();

        // 实时行情任务通道（生产者：行情引擎；消费者：策略引擎）
        private readonly Channel<MarketDataTask> _marketTaskChannel;
        private readonly QueuePressureMonitor _queueMonitor;
        private readonly RuntimeQueueOptions _queueOptions;
        private readonly ConcurrentDictionary<string, MarketDataTaskSubscription> _subscriptions = new();
        private readonly MarketDataQueryOptions _queryOptions;

        // 限流信号量：限制并发请求数，避免触发交易所API限流
        private readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(5, 5); // 最多5个并发请求

        // 缓存其他周期列表（避免每次 Enum.GetValues + LINQ）
        private readonly string[] _otherTimeframes;

        // 缓存合约符号查找结果（markets 是静态数据）
        private readonly ConcurrentDictionary<string, string> _futuresSymbolCache = new();

        // 初始化完成标志
        private Task? _initializationTask;

        public MarketDataEngine(
            ILogger<MarketDataEngine> logger,
            IOptions<RuntimeQueueOptions> queueOptions,
            IOptions<MarketDataQueryOptions> queryOptions) : base(logger)
        {
            // 初始化队列，启用有界通道与背压策略
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            _queueOptions = options;
            _marketTaskChannel = ChannelFactory.Create<MarketDataTask>(
                options.MarketData,
                "MarketDataEngine",
                logger,
                singleReader: false,
                singleWriter: false);
            _queueMonitor = new QueuePressureMonitor("MarketDataEngine", options.MarketData, logger);
            _queryOptions = queryOptions?.Value ?? new MarketDataQueryOptions();

            // 初始化时生成周期列表
            _otherTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>()
                .Where(tf => tf != MarketDataConfig.TimeframeEnum.m1)
                .Select(tf => MarketDataConfig.TimeframeToString(tf))
                .ToArray();

            // 启动异步初始化，但不等待
            _initializationTask = InitializeExchangesAsync();
        }

        /// <summary>
        /// 获取其他周期列表（除1m外）
        /// </summary>
        private string[] GetOtherTimeframes() => _otherTimeframes;

        public MarketDataNextTickInfo GetNextTickInfo()
        {
            long? latest1mTimestamp = null;

            foreach (var exchangeEntry in _cache)
            {
                foreach (var symbolEntry in exchangeEntry.Value)
                {
                    var symbolCache = symbolEntry.Value;
                    if (!symbolCache.Timeframes.TryGetValue("1m", out var candles))
                    {
                        continue;
                    }

                    lock (symbolCache.Lock)
                    {
                        if (candles.Count == 0 || candles[^1].timestamp == null)
                        {
                            continue;
                        }

                        var ts = (long)candles[^1].timestamp;
                        if (!latest1mTimestamp.HasValue || ts > latest1mTimestamp.Value)
                        {
                            latest1mTimestamp = ts;
                        }
                    }
                }
            }

            DateTimeOffset? nextCloseAt = null;
            int? nextCloseInSeconds = null;
            var closingTimeframes = new List<string>();

            if (latest1mTimestamp.HasValue)
            {
                var next1mTimestamp = latest1mTimestamp.Value + 60_000;
                nextCloseAt = DateTimeOffset.FromUnixTimeMilliseconds(next1mTimestamp);
                var deltaSeconds = (int)Math.Round((nextCloseAt.Value - DateTimeOffset.UtcNow).TotalSeconds);
                nextCloseInSeconds = Math.Max(0, deltaSeconds);

                foreach (var timeframe in _otherTimeframes)
                {
                    var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                    if (tfMs <= 0)
                    {
                        continue;
                    }

                    if (next1mTimestamp % tfMs == 0)
                    {
                        closingTimeframes.Add(timeframe);
                    }
                }
            }

            return new MarketDataNextTickInfo
            {
                Next1mCloseAt = nextCloseAt,
                Next1mCloseInSeconds = nextCloseInSeconds,
                UpdateTimeframes = _otherTimeframes,
                ClosingTimeframes = closingTimeframes
            };
        }

        /// <summary>
        /// 等待初始化完成
        /// </summary>
        public async Task WaitForInitializationAsync()
        {
            if (_initializationTask != null)
            {
                await _initializationTask;
            }
        }

        /// <summary>
        /// 订阅行情任务（为每个订阅者提供独立通道）
        /// </summary>
        public MarketDataTaskSubscription SubscribeMarketTasks(string subscriberName, bool onlyBarClose = false)
        {
            if (string.IsNullOrWhiteSpace(subscriberName))
            {
                throw new ArgumentException("订阅者名称不能为空", nameof(subscriberName));
            }

            var name = subscriberName.Trim();
            if (_subscriptions.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var queueName = $"MarketDataEngine:{name}";
            var channel = ChannelFactory.Create<MarketDataTask>(
                _queueOptions.MarketData,
                queueName,
                Logger,
                singleReader: false,
                singleWriter: false);
            var monitor = new QueuePressureMonitor(queueName, _queueOptions.MarketData, Logger);
            var subscription = new MarketDataTaskSubscription(name, channel, monitor, onlyBarClose);
            _subscriptions[name] = subscription;
            Logger.LogInformation("创建行情任务订阅: {Subscriber} onlyBarClose={OnlyBarClose}", name, onlyBarClose);
            return subscription;
        }

        /// <summary>
        /// 交易所行情是否就绪（至少有一根 1m K 线）
        /// </summary>
        public bool IsExchangeReady(string exchangeKey)
        {
            if (string.IsNullOrWhiteSpace(exchangeKey))
            {
                return false;
            }

            if (!_cache.TryGetValue(exchangeKey, out var symbolCaches))
            {
                return false;
            }

            foreach (var symbolEntry in symbolCaches.Values)
            {
                if (!symbolEntry.Timeframes.TryGetValue("1m", out var candles))
                {
                    continue;
                }

                lock (symbolEntry.Lock)
                {
                    if (candles.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 尝试读取一条最新行情任务（非阻塞）
        /// </summary>
        public bool TryDequeueMarketTask(out MarketDataTask task)
        {
            if (_marketTaskChannel.Reader.TryRead(out task))
            {
                _queueMonitor.OnDequeue();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 阻塞读取一条行情任务（用于实时策略引擎）
        /// </summary>
        public ValueTask<MarketDataTask> ReadMarketTaskAsync(CancellationToken cancellationToken)
        {
            return ReadMarketTaskInternalAsync(cancellationToken);
        }

        /// <summary>
        /// 持续读取行情任务流（用于后台消费）
        /// </summary>
        public IAsyncEnumerable<MarketDataTask> ReadAllMarketTasksAsync(CancellationToken cancellationToken)
        {
            return ReadAllMarketTasksInternalAsync(cancellationToken);
        }


        private async ValueTask<MarketDataTask> ReadMarketTaskInternalAsync(CancellationToken cancellationToken)
        {
            var task = await _marketTaskChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _queueMonitor.OnDequeue();
            return task;
        }

        private async IAsyncEnumerable<MarketDataTask> ReadAllMarketTasksInternalAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var task in _marketTaskChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _queueMonitor.OnDequeue();
                yield return task;
            }
        }

        /// <summary>
        /// 初始化交易所（REST API 和 WebSocket）
        /// </summary>
        private async Task InitializeExchangesAsync()
        {
            try
            {
                List<MarketDataConfig.ExchangeEnum> initTasks = new List<MarketDataConfig.ExchangeEnum>();
                initTasks.Add(MarketDataConfig.ExchangeEnum.Bitget);

                foreach (var exchangeEnum in initTasks)//Enum.GetValues<MarketDataConfig.ExchangeEnum>())
                {
                    var exchangeId = MarketDataConfig.ExchangeToString(exchangeEnum);
                    var options = MarketDataConfig.GetExchangeOptions(exchangeEnum);

                    // 创建 REST API 交易所实例
                    Exchange restExchange = exchangeEnum switch
                    {
                        MarketDataConfig.ExchangeEnum.Binance => new ccxt.binanceusdm(options),
                        MarketDataConfig.ExchangeEnum.OKX => new ccxt.okx(options),
                        MarketDataConfig.ExchangeEnum.Bitget => new ccxt.bitget(options),
                        _ => throw new NotSupportedException($"不支持的交易所: {exchangeEnum}")
                    };

                    // 创建 WebSocket 交易所实例
                    Exchange wsExchange = exchangeEnum switch
                    {
                        MarketDataConfig.ExchangeEnum.Binance => new ccxt.pro.binanceusdm(options),
                        MarketDataConfig.ExchangeEnum.OKX => new ccxt.pro.okx(options),
                        MarketDataConfig.ExchangeEnum.Bitget => new ccxt.pro.bitget(options),
                        _ => throw new NotSupportedException($"不支持的交易所: {exchangeEnum}")
                    };

                    await restExchange.LoadMarkets();
                    await wsExchange.LoadMarkets();

                    _exchanges[exchangeId] = restExchange;
                    _wsExchanges[exchangeId] = wsExchange;

                    Logger.LogInformation($"[{exchangeId}] 交易所初始化完成");
                }

                // 初始化缓存结构
                InitializeCache();

                // 先启动 WebSocket 订阅
                await StartWebSocketSubscriptionsAsync();

                // WebSocket 订阅成功后再加载历史数据
                await LoadHistoryDataAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "交易所初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 初始化缓存结构
        /// </summary>
        private void InitializeCache()
        {
            foreach (var exchangeId in _exchanges.Keys)
            {
                _cache[exchangeId] = new ConcurrentDictionary<string, SymbolCache>();

                foreach (var symbolEnum in Enum.GetValues<MarketDataConfig.SymbolEnum>())
                {
                    var symbol = MarketDataConfig.SymbolToString(symbolEnum);
                    var symbolCache = new SymbolCache();

                    foreach (var timeframeEnum in Enum.GetValues<MarketDataConfig.TimeframeEnum>())
                    {
                        var timeframe = MarketDataConfig.TimeframeToString(timeframeEnum);
                        symbolCache.Timeframes[timeframe] = new List<OHLCV>();
                        symbolCache.CurrentBuckets[timeframe] = null;
                    }

                    _cache[exchangeId][symbol] = symbolCache;
                }
            }
        }

        /// <summary>
        /// 启动 WebSocket 订阅（只订阅 1 分钟 K 线）
        /// </summary>
        private async Task StartWebSocketSubscriptionsAsync()
        {
            var subscriptionTasks = new List<Task>();

            foreach (var exchangeId in _wsExchanges.Keys)
            {
                if (!_wsExchanges.TryGetValue(exchangeId, out var wsExchange))
                    continue;

                if (!_exchanges.TryGetValue(exchangeId, out var restExchange))
                    continue;

                foreach (var symbolEnum in Enum.GetValues<MarketDataConfig.SymbolEnum>())
                {
                    var symbol = MarketDataConfig.SymbolToString(symbolEnum);

                    // 检查交易对是否存在
                    if (!IsSymbolAvailable(restExchange, symbol))
                    {
                        Logger.LogWarning($"[{exchangeId}] {symbol} 交易对不存在，跳过 WebSocket 订阅");
                        continue;
                    }

                    var taskKey = $"{exchangeId}_{symbol}";
                    // 直接启动监听任务，不再有重复订阅
                    var watchTask = Watch1mKlineAsync(exchangeId, symbol, _cancellationTokenSource.Token);
                    _watchTasks[taskKey] = watchTask;
                    subscriptionTasks.Add(watchTask);
                }
            }

            Logger.LogInformation($"启动 {subscriptionTasks.Count} 个 WebSocket 订阅任务");
            // 不等待所有任务完成，让它们后台运行
            _ = Task.WhenAll(subscriptionTasks).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Logger.LogError(t.Exception, "部分 WebSocket 订阅任务失败");
                }
                else
                {
                    Logger.LogInformation("所有 WebSocket 订阅任务已启动");
                }
            });
        }

        /// <summary>
        /// 查找合约符号（带缓存）
        /// </summary>
        private string FindFuturesSymbol(Exchange exchange, string baseSymbol)
        {
            var cacheKey = $"{exchange.id}_{baseSymbol}";

            // 先查缓存
            if (_futuresSymbolCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            string result;
            try
            {
                // 使用动态类型访问 markets
                dynamic dynamicExchange = exchange;
                var markets = dynamicExchange.markets as Dictionary<string, object>;

                if (markets == null)
                {
                    Logger.LogWarning($"[{exchange.id}] markets 为空，使用默认符号");
                    result = baseSymbol + ":USDT";
                    _futuresSymbolCache.TryAdd(cacheKey, result);
                    return result;
                }

                // 尝试不同的符号格式
                var possibleSymbols = new List<string>
                {
                    baseSymbol,                    // BTC/USDT
                    baseSymbol + ":USDT",          // BTC/USDT:USDT
                    baseSymbol.Replace("/", "")     // BTCUSDT
                };

                foreach (var symbol in possibleSymbols)
                {
                    if (markets.ContainsKey(symbol))
                    {
                        var market = markets[symbol];
                        if (market is Dictionary<string, object> marketDict)
                        {
                            var isSwap = marketDict.ContainsKey("swap") && marketDict["swap"] is bool swap && swap;
                            var isFuture = marketDict.ContainsKey("future") && marketDict["future"] is bool future && future;
                            var isContract = marketDict.ContainsKey("contract") && marketDict["contract"] is bool contract && contract;

                            if (isSwap || isFuture || isContract)
                            {
                                // 缓存结果
                                _futuresSymbolCache.TryAdd(cacheKey, symbol);
                                return symbol;
                            }
                        }
                    }
                }

                // 如果直接查找失败，遍历所有市场查找相关的合约
                var baseCoin = baseSymbol.Split('/')[0];
                foreach (var marketEntry in markets)
                {
                    var market = marketEntry.Value;
                    if (market is Dictionary<string, object> marketDict)
                    {
                        var baseId = marketDict.ContainsKey("baseId") ? marketDict["baseId"]?.ToString() : null;
                        var isSwap = marketDict.ContainsKey("swap") && marketDict["swap"] is bool swap && swap;
                        var isFuture = marketDict.ContainsKey("future") && marketDict["future"] is bool future && future;
                        var isContract = marketDict.ContainsKey("contract") && marketDict["contract"] is bool contract && contract;
                        var quote = marketDict.ContainsKey("quote") ? marketDict["quote"]?.ToString() : null;
                        var quoteId = marketDict.ContainsKey("quoteId") ? marketDict["quoteId"]?.ToString() : null;
                        var marketSymbol = marketDict.ContainsKey("symbol") ? marketDict["symbol"]?.ToString() : null;

                        if (baseId == baseCoin && (isSwap || isFuture || isContract))
                        {
                            if (quote == "USDT" || quoteId == "USDT")
                            {
                                result = marketSymbol ?? baseSymbol;
                                // 缓存结果
                                _futuresSymbolCache.TryAdd(cacheKey, result);
                                return result;
                            }
                        }
                    }
                }

                // 如果都没找到，使用默认值
                result = baseSymbol + ":USDT";
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"[{exchange.id}] 查找合约符号时出错");
                result = baseSymbol + ":USDT";
            }

            // 缓存默认结果
            _futuresSymbolCache.TryAdd(cacheKey, result);
            return result;
        }

        /// <summary>
        /// 检查交易对是否在交易所可用
        /// </summary>
        private bool IsSymbolAvailable(Exchange exchange, string symbol)
        {
            try
            {
                // 先尝试查找合约符号
                var futuresSymbol = FindFuturesSymbol(exchange, symbol);

                // 如果找到的符号和原始符号不同，或者能找到，说明可用
                if (futuresSymbol != symbol + ":USDT" || futuresSymbol.Contains(":"))
                {
                    return true;
                }

                // 尝试访问 markets
                dynamic dynamicExchange = exchange;
                var markets = dynamicExchange.markets as Dictionary<string, object>;

                if (markets == null)
                    return false;

                // 尝试不同的符号格式
                var possibleSymbols = new List<string>
                {
                    symbol,                    // BTC/USDT
                    symbol + ":USDT",          // BTC/USDT:USDT
                    symbol.Replace("/", "")     // BTCUSDT
                };

                foreach (var sym in possibleSymbols)
                {
                    if (markets.ContainsKey(sym))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 订阅 1 分钟 K 线并实时更新缓存（单一消费循环，无重复订阅）
        /// </summary>
        private async Task Watch1mKlineAsync(string exchangeId, string symbol, CancellationToken cancellationToken)
        {
            if (!_wsExchanges.TryGetValue(exchangeId, out var exchange))
            {
                Logger.LogError($"[{exchangeId}] WebSocket 交易所实例不存在");
                return;
            }

            var futuresSymbol = FindFuturesSymbol(exchange, symbol);
            Logger.LogInformation($"[{exchangeId}] 开始订阅 {symbol} (实际符号: {futuresSymbol}) 1m K线...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 单一订阅点，持续消费
                    var ohlcvs = await exchange.WatchOHLCV(futuresSymbol, "1m");
                    if (ohlcvs == null || ohlcvs.Count == 0)
                    {
                        continue;
                    }

                    var latest = ohlcvs[^1];
                    var timestamp = latest.timestamp != null ? (long)latest.timestamp : 0;

                    if (!_cache.TryGetValue(exchangeId, out var symbolCacheDict))
                        continue;

                    if (!symbolCacheDict.TryGetValue(symbol, out var symbolCache))
                        continue;

                    lock (symbolCache.Lock)
                    {
                        var cache1m = symbolCache.Timeframes["1m"];

                        // 检查是否是新K线（收线）
                        var lastTimestamp = cache1m.Count > 0 && cache1m[^1].timestamp != null
                            ? (long?)cache1m[^1].timestamp
                            : null;
                        bool isNewCandle = cache1m.Count == 0 ||
                                          (lastTimestamp.HasValue && lastTimestamp.Value < timestamp);

                        if (isNewCandle)
                        {
                            // 新K线，添加到缓存
                            cache1m.Add(latest);

                            // 保持缓存长度
                            if (cache1m.Count > _queryOptions.CacheHistoryLength)
                            {
                                cache1m.RemoveAt(0);
                            }

                            // Logger.LogInformation($"[{exchangeId}] {symbol} 1m 新K线: time={FormatTimestamp(timestamp)} close={latest.close}");

                            // 输出最新的5根K线
                            // Logger.LogInformation($"[{exchangeId}] {symbol} 1m 最新的5根K线: \n" +
                            //     $"{string.Join("\n", cache1m.TakeLast(5).Select(c => $"time={FormatTimestamp(c.timestamp ?? 0)}, close={c.close}"))}\n");

                            // 1分钟收线，更新其他周期（增量聚合）
                            UpdateOtherTimeframesIncremental(exchangeId, symbol, latest, symbolCache);

                            // 1m 收线生成实时行情任务
                            EnqueueMarketTask(exchangeId, symbol, "1m", timestamp, isBarClose: true);

                            // 1m 收线驱动其他周期 OnBarUpdate 任务
                            EnqueueOnBarUpdateTasks(exchangeId, symbol, symbolCache, timestamp);
                        }
                        else
                        {
                            // 更新当前K线
                            if (cache1m.Count > 0)
                            {
                                cache1m[^1] = latest;

                                // 同时更新其他周期的当前 bucket
                                UpdateOtherTimeframesCurrentBucket(exchangeId, symbol, latest, symbolCache);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"[{exchangeId}] 订阅 {symbol} 1m K线错误");
                    await Task.Delay(2000, cancellationToken);
                }
            }
        }

        /// <summary>
        /// 增量更新其他周期（新K线收线时）
        /// </summary>
        private void UpdateOtherTimeframesIncremental(string exchangeId, string symbol, OHLCV new1mCandle, SymbolCache symbolCache)
        {
            var timestamp1m = new1mCandle.timestamp != null ? (long)new1mCandle.timestamp : 0;
            if (timestamp1m == 0) return;

            // 获取所有周期（除了1m）- 使用缓存的列表
            var timeframes = GetOtherTimeframes();

            foreach (var timeframe in timeframes)
            {
                try
                {
                    var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                    var cacheTf = symbolCache.Timeframes[timeframe];
                    var currentBucket = symbolCache.CurrentBuckets[timeframe];

                    // 计算新1m K线属于哪个周期
                    var tfStart = (timestamp1m / tfMs) * tfMs;

                    if (currentBucket == null)
                    {
                        // 没有当前 bucket，创建新的
                        var newBucket = new OHLCV
                        {
                            timestamp = tfStart,
                            open = new1mCandle.open ?? 0,
                            high = new1mCandle.high ?? 0,
                            low = new1mCandle.low ?? 0,
                            close = new1mCandle.close ?? 0,
                            volume = new1mCandle.volume ?? 0
                        };
                        symbolCache.CurrentBuckets[timeframe] = newBucket;
                    }
                    else
                    {
                        var currentTfStart = currentBucket.Value.timestamp != null ? (long)currentBucket.Value.timestamp : 0;

                        if (tfStart == currentTfStart)
                        {
                            // 同一周期，更新当前 bucket
                            var updatedBucket = currentBucket.Value;
                            updatedBucket.high = Math.Max(updatedBucket.high ?? 0, new1mCandle.high ?? 0);
                            updatedBucket.low = Math.Min(updatedBucket.low ?? double.MaxValue, new1mCandle.low ?? double.MaxValue);
                            if (updatedBucket.low == double.MaxValue) updatedBucket.low = new1mCandle.low ?? 0;
                            updatedBucket.close = new1mCandle.close ?? 0;
                            updatedBucket.volume = (updatedBucket.volume ?? 0) + (new1mCandle.volume ?? 0);
                            symbolCache.CurrentBuckets[timeframe] = updatedBucket;
                        }
                        else
                        {
                            // 新周期开始，finalize 上一个 bucket
                            var finalizedBucket = currentBucket.Value;
                            if (finalizedBucket.low == double.MaxValue) finalizedBucket.low = 0;
                            cacheTf.Add(finalizedBucket);

                            // 保持缓存长度
                            if (cacheTf.Count > _queryOptions.CacheHistoryLength)
                            {
                                cacheTf.RemoveAt(0);
                            }

                            var finalizedTimestamp = finalizedBucket.timestamp != null ? (long)finalizedBucket.timestamp : 0;
                            if (finalizedTimestamp > 0)
                            {
                                EnqueueMarketTask(exchangeId, symbol, timeframe, finalizedTimestamp);
                            }

                            // 创建新的 bucket
                            var newBucket = new OHLCV
                            {
                                timestamp = tfStart,
                                open = new1mCandle.open ?? 0,
                                high = new1mCandle.high ?? 0,
                                low = new1mCandle.low ?? 0,
                                close = new1mCandle.close ?? 0,
                                volume = new1mCandle.volume ?? 0
                            };
                            symbolCache.CurrentBuckets[timeframe] = newBucket;
                            // 输出最新的5根K线
                            // Logger.LogInformation($"[{exchangeId}] {symbol} {timeframe} 最新的5根K线: \n" +
                            //     $"{string.Join(", ", cacheTf.TakeLast(5).Select(c => $"time={FormatTimestamp(c.timestamp ?? 0)}, close={c.close}"))}\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"[{exchangeId}] 更新 {symbol} {timeframe} 失败");
                }
            }
        }

        /// <summary>
        /// 更新其他周期的当前 bucket（1m K线更新时，未收线）
        /// </summary>
        private void UpdateOtherTimeframesCurrentBucket(string exchangeId, string symbol, OHLCV updated1mCandle, SymbolCache symbolCache)
        {
            var timestamp1m = updated1mCandle.timestamp != null ? (long)updated1mCandle.timestamp : 0;
            if (timestamp1m == 0) return;

            // 使用缓存的周期列表
            var timeframes = GetOtherTimeframes();

            foreach (var timeframe in timeframes)
            {
                try
                {
                    var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                    var tfStart = (timestamp1m / tfMs) * tfMs;
                    var currentBucket = symbolCache.CurrentBuckets[timeframe];

                    if (currentBucket != null)
                    {
                        var currentTfStart = currentBucket.Value.timestamp != null ? (long)currentBucket.Value.timestamp : 0;

                        if (tfStart == currentTfStart)
                        {
                            // 更新当前 bucket（未收线时，只更新 high/low/close，不累加 volume）
                            // volume 只在 UpdateOtherTimeframesIncremental（新1m收线）时累加
                            var updatedBucket = currentBucket.Value;
                            updatedBucket.high = Math.Max(updatedBucket.high ?? 0, updated1mCandle.high ?? 0);
                            updatedBucket.low = Math.Min(updatedBucket.low ?? double.MaxValue, updated1mCandle.low ?? double.MaxValue);
                            if (updatedBucket.low == double.MaxValue) updatedBucket.low = updated1mCandle.low ?? 0;
                            updatedBucket.close = updated1mCandle.close ?? 0;
                            // ❌ 不累加 volume，避免同一根1m K线多次更新时重复累加
                            symbolCache.CurrentBuckets[timeframe] = updatedBucket;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"[{exchangeId}] 更新 {symbol} {timeframe} 当前 bucket 失败");
                }
            }
        }

        /// <summary>
        /// 加载历史数据（各个周期都拉取2000根）
        /// </summary>
        private async Task LoadHistoryDataAsync()
        {
            Logger.LogInformation("========== 开始加载历史数据 ==========");

            var fetchTasks = new List<Task>();

            List<MarketDataConfig.TimeframeEnum> timeframeEnums = new List<MarketDataConfig.TimeframeEnum>();
            timeframeEnums.Add(MarketDataConfig.TimeframeEnum.m1);
            timeframeEnums.Add(MarketDataConfig.TimeframeEnum.m3);


            foreach (var exchangeId in _exchanges.Keys)
            {
                if (!_exchanges.TryGetValue(exchangeId, out var exchange))
                    continue;

                foreach (var symbolEnum in Enum.GetValues<MarketDataConfig.SymbolEnum>())
                {
                    var symbol = MarketDataConfig.SymbolToString(symbolEnum);

                    // 检查交易对是否存在
                    if (!IsSymbolAvailable(exchange, symbol))
                    {
                        Logger.LogWarning($"[{exchangeId}] {symbol} 交易对不存在，跳过");
                        continue;
                    }

                    var futuresSymbol = FindFuturesSymbol(exchange, symbol);

                    // 为每个周期拉取历史数据
                    foreach (var timeframeEnum in timeframeEnums)//Enum.GetValues<MarketDataConfig.TimeframeEnum>())
                    {
                        var timeframe = MarketDataConfig.TimeframeToString(timeframeEnum);
                        var localExchangeId = exchangeId;
                        var localSymbol = symbol;
                        var localFuturesSymbol = futuresSymbol;
                        var localTimeframe = timeframe;
                        var localExchange = exchange;

                        // 移除 Task.Run，直接使用异步方法
                        fetchTasks.Add(FetchHistoryDataAsync(localExchangeId, localSymbol, localFuturesSymbol, localTimeframe, localExchange));
                    }
                }
            }

            await Task.WhenAll(fetchTasks);
            Logger.LogInformation("历史数据加载完成");
        }

        /// <summary>
        /// 拉取单个交易对单个周期的历史数据
        /// </summary>
        private async Task FetchHistoryDataAsync(string exchangeId, string symbol, string futuresSymbol, string timeframe, Exchange exchange)
        {
            // 等待获取信号量，限制并发请求数
            await _rateLimiter.WaitAsync();
            try
            {
                // 在请求前延迟，真正限制请求频次
                await Task.Delay(TimeSpan.FromMilliseconds(200));

                // 计算并输出拉取时间范围
                var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                var now = exchange.milliseconds();
                var since = now - tfMs * _queryOptions.CacheHistoryLength;
                var sinceDateTime = DateTimeOffset.FromUnixTimeMilliseconds(since).ToOffset(TimeSpan.FromHours(8));
                var nowDateTime = DateTimeOffset.FromUnixTimeMilliseconds(now).ToOffset(TimeSpan.FromHours(8));
                Logger.LogInformation($"开始拉取 [{exchangeId}] {symbol} {timeframe} 拉取时间范围: {sinceDateTime:yyyy-MM-dd HH:mm:ss} ~ {nowDateTime:yyyy-MM-dd HH:mm:ss}");

                var candles = await FetchOhlcvWithLimit(exchange, futuresSymbol, timeframe, _queryOptions.CacheHistoryLength);

                if (!_cache.TryGetValue(exchangeId, out var symbolCacheDict))
                {
                    Logger.LogWarning($"[{exchangeId}] 缓存结构不存在");
                    return;
                }

                if (!symbolCacheDict.TryGetValue(symbol, out var symbolCache))
                {
                    Logger.LogWarning($"[{exchangeId}] {symbol} 缓存不存在");
                    return;
                }

                lock (symbolCache.Lock)
                {
                    symbolCache.Timeframes[timeframe] = candles;

                    // 如果是1m数据，初始化其他周期的当前 bucket
                    if (timeframe == "1m" && candles.Count > 0)
                    {
                        InitializeCurrentBucketsFromHistory(symbolCache, candles);
                    }
                }

                Logger.LogInformation($"[{exchangeId}] {symbol} {timeframe} 实际缓存数量: {candles.Count} 根");

                // 输出数据长度、第一天和最后一天的北京时间
                if (candles.Count > 0)
                {
                    var firstTimestamp = candles[0].timestamp ?? 0;
                    var lastTimestamp = candles[^1].timestamp ?? 0;

                    var firstDateTime = DateTimeOffset.FromUnixTimeMilliseconds(firstTimestamp).ToOffset(TimeSpan.FromHours(8));
                    var lastDateTime = DateTimeOffset.FromUnixTimeMilliseconds(lastTimestamp).ToOffset(TimeSpan.FromHours(8));

                    Logger.LogInformation($"[{exchangeId}] {symbol} {timeframe} 数据长度: {candles.Count} 根, 第一天北京时间: {firstDateTime:yyyy-MM-dd HH:mm:ss}, 最后一天北京时间: {lastDateTime:yyyy-MM-dd HH:mm:ss}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"[{exchangeId}] 拉取 {symbol} {timeframe} 失败");
            }
            finally
            {
                // 释放信号量，允许下一个请求开始
                _rateLimiter.Release();
            }
        }

        /// <summary>
        /// 从1m历史数据初始化其他周期的当前 bucket
        /// </summary>
        private void InitializeCurrentBucketsFromHistory(SymbolCache symbolCache, List<OHLCV> candles1m)
        {
            if (candles1m.Count == 0) return;

            var latest1m = candles1m[^1];
            var timestamp1m = latest1m.timestamp != null ? (long)latest1m.timestamp : 0;
            if (timestamp1m == 0) return;

            var timeframes = GetOtherTimeframes();

            foreach (var timeframe in timeframes)
            {
                try
                {
                    var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                    var tfStart = (timestamp1m / tfMs) * tfMs;
                    var cacheTf = symbolCache.Timeframes[timeframe];

                    // 🔧 关键修复：确保 list 里不包含当前周期
                    // 如果最后一根K线的 timestamp == tfStart，说明当前周期已收线，应该移除
                    // 因为当前 bucket 会替代它
                    if (cacheTf.Count > 0 && cacheTf[^1].timestamp != null)
                    {
                        var lastTfTimestamp = (long)cacheTf[^1].timestamp;
                        if (lastTfTimestamp == tfStart)
                        {
                            // 当前周期已收线，移除最后一根（它会被 bucket 替代）
                            cacheTf.RemoveAt(cacheTf.Count - 1);
                        }
                    }

                    // 找到该周期内所有1m K线
                    var relevant1m = candles1m.Where(c =>
                        c.timestamp != null &&
                        (long)c.timestamp >= tfStart &&
                        (long)c.timestamp < tfStart + tfMs).ToList();

                    if (relevant1m.Count > 0)
                    {
                        // 聚合为当前 bucket
                        var aggregated = AggregateCandles(relevant1m, tfStart);
                        symbolCache.CurrentBuckets[timeframe] = aggregated;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"初始化 {timeframe} 当前 bucket 失败");
                }
            }
        }

        /// <summary>
        /// 拉取指定数量的K线
        /// </summary>
        private async Task<List<OHLCV>> FetchOhlcvWithLimit(Exchange exchange, string symbol, string timeframe, int targetBars)
        {
            var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
            var result = new List<OHLCV>();
            var now = exchange.milliseconds();
            var since = long.Max(now - tfMs * targetBars, 1577808000);
            var maxLimitPerRequest = _queryOptions.MaxLimitPerRequest;

            while (result.Count < targetBars)
            {
                var remaining = targetBars - result.Count;
                var limit = Math.Min(remaining, maxLimitPerRequest);

                var ohlcvs = await exchange.FetchOHLCV(symbol, timeframe, since, limit);
                if (ohlcvs == null || ohlcvs.Count == 0)
                {
                    break;
                }

                result.AddRange(ohlcvs);

                var last = ohlcvs[^1];
                if (last.timestamp == null)
                {
                    break;
                }

                since = (long)last.timestamp + tfMs;
            }

            if (result.Count > targetBars)
            {
                result = result.GetRange(result.Count - targetBars, targetBars);
            }

            return result;
        }

        /// <summary>
        /// 聚合多根1分钟K线为一根周期K线
        /// </summary>
        private OHLCV AggregateCandles(List<OHLCV> candles, long timestamp)
        {
            if (candles.Count == 0)
            {
                throw new ArgumentException("蜡烛图列表不能为空");
            }

            var open = candles[0].open ?? 0;
            var close = candles[^1].close ?? 0;
            var high = candles.Max(c => c.high ?? 0);
            var low = candles.Min(c => c.low ?? double.MaxValue);
            var volume = candles.Sum(c => c.volume ?? 0);

            return new OHLCV
            {
                timestamp = timestamp,
                open = open,
                high = high,
                low = low == double.MaxValue ? 0 : low,
                close = close,
                volume = volume
            };
        }

        /// <summary>
        /// 获取实时数据：返回最新的1根K线
        /// </summary>
        public OHLCV? GetLatestKline(
            MarketDataConfig.ExchangeEnum exchange,
            MarketDataConfig.TimeframeEnum timeframe,
            MarketDataConfig.SymbolEnum symbol)
        {
            var exchangeId = MarketDataConfig.ExchangeToString(exchange);
            var symbolStr = MarketDataConfig.SymbolToString(symbol);
            var timeframeStr = MarketDataConfig.TimeframeToString(timeframe);

            if (!_cache.TryGetValue(exchangeId, out var symbolCacheDict))
                return null;

            if (!symbolCacheDict.TryGetValue(symbolStr, out var symbolCache))
                return null;

            if (!symbolCache.Timeframes.TryGetValue(timeframeStr, out var candles))
                return null;

            lock (symbolCache.Lock)
            {
                if (candles.Count == 0)
                    return null;

                // 如果是当前周期且存在当前 bucket，返回 bucket（未收线）
                if (timeframeStr != "1m")
                {
                    var currentBucket = symbolCache.CurrentBuckets[timeframeStr];
                    if (currentBucket != null)
                    {
                        return currentBucket.Value;
                    }
                }

                return candles[^1];
            }
        }

        public OHLCV? GetLatestKline(string exchangeId, string timeframe, string symbol)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);

            if (!_cache.TryGetValue(exchangeKey, out var symbolCacheDict))
                return null;

            if (!symbolCacheDict.TryGetValue(symbolKey, out var symbolCache))
                return null;

            if (!symbolCache.Timeframes.TryGetValue(timeframeKey, out var candles))
                return null;

            lock (symbolCache.Lock)
            {
                if (candles.Count == 0)
                    return null;

                if (timeframeKey != "1m")
                {
                    var currentBucket = symbolCache.CurrentBuckets[timeframeKey];
                    if (currentBucket != null)
                    {
                        return currentBucket.Value;
                    }
                }

                return candles[^1];
            }
        }

        /// <summary>
        /// 获取历史数据：返回最新的n根K线数据
        /// </summary>
        public List<OHLCV> GetHistoryKlines(
            MarketDataConfig.ExchangeEnum exchange,
            MarketDataConfig.TimeframeEnum timeframe,
            MarketDataConfig.SymbolEnum symbol,
            DateTime? endTime,
            int count)
        {
            var exchangeId = MarketDataConfig.ExchangeToString(exchange);
            var symbolStr = MarketDataConfig.SymbolToString(symbol);
            var timeframeStr = MarketDataConfig.TimeframeToString(timeframe);

            if (!_cache.TryGetValue(exchangeId, out var symbolCacheDict))
                return new List<OHLCV>();

            if (!symbolCacheDict.TryGetValue(symbolStr, out var symbolCache))
                return new List<OHLCV>();

            if (!symbolCache.Timeframes.TryGetValue(timeframeStr, out var candles))
                return new List<OHLCV>();

            lock (symbolCache.Lock)
            {
                var result = new List<OHLCV>(candles);

                // 如果是非1m周期，添加当前 bucket（如果存在）
                if (timeframeStr != "1m")
                {
                    var currentBucket = symbolCache.CurrentBuckets[timeframeStr];
                    if (currentBucket != null)
                    {
                        result.Add(currentBucket.Value);
                    }
                }

                if (result.Count == 0)
                    return new List<OHLCV>();

                // 如果指定了结束时间，过滤数据
                if (endTime.HasValue)
                {
                    var endTimestamp = ((DateTimeOffset)endTime.Value).ToUnixTimeMilliseconds();
                    var filtered = result.Where(c =>
                        c.timestamp != null &&
                        (long)c.timestamp <= endTimestamp).ToList();

                    // 返回最新的 count 根
                    var startIndex = Math.Max(0, filtered.Count - count);
                    return filtered.Skip(startIndex).Take(count).ToList();
                }
                else
                {
                    // 返回最新的 count 根
                    var startIndex = Math.Max(0, result.Count - count);
                    return result.Skip(startIndex).Take(count).ToList();
                }
            }
        }

        public List<OHLCV> GetHistoryKlines(
            string exchangeId,
            string timeframe,
            string symbol,
            long? endTimestamp,
            int count)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);

            if (!_cache.TryGetValue(exchangeKey, out var symbolCacheDict))
                return new List<OHLCV>();

            if (!symbolCacheDict.TryGetValue(symbolKey, out var symbolCache))
                return new List<OHLCV>();

            if (!symbolCache.Timeframes.TryGetValue(timeframeKey, out var candles))
                return new List<OHLCV>();

            lock (symbolCache.Lock)
            {
                var result = new List<OHLCV>(candles);

                if (timeframeKey != "1m")
                {
                    var currentBucket = symbolCache.CurrentBuckets[timeframeKey];
                    if (currentBucket != null)
                    {
                        result.Add(currentBucket.Value);
                    }
                }

                if (result.Count == 0)
                    return new List<OHLCV>();

                if (endTimestamp.HasValue)
                {
                    var filtered = result.Where(c =>
                        c.timestamp != null &&
                        (long)c.timestamp <= endTimestamp.Value).ToList();

                    var startIndex = Math.Max(0, filtered.Count - count);
                    return filtered.Skip(startIndex).Take(count).ToList();
                }

                var start = Math.Max(0, result.Count - count);
                return result.Skip(start).Take(count).ToList();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Logger.LogInformation("开始释放 MarketDataEngine 资源...");

            // 取消所有任务
            _cancellationTokenSource.Cancel();

            // 不等待 WebSocket 任务，直接释放资源
            // 行情引擎退出时，"停止消费"比"优雅等待"更重要
            // WatchOHLCV 是网络阻塞 await，cancel token 不一定立即打断，可能卡住
            var taskCount = _watchTasks.Count;
            Logger.LogInformation($"已取消 {taskCount} 个 WebSocket 任务，不等待其完成");

            // 释放交易所资源
            foreach (var exchange in _exchanges.Values)
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
                    Logger.LogWarning(ex, "释放 REST API 交易所资源时出错");
                }
            }

            foreach (var exchange in _wsExchanges.Values)
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
                    Logger.LogWarning(ex, "释放 WebSocket 交易所资源时出错");
                }
            }

            _rateLimiter.Dispose();
            _cancellationTokenSource.Dispose();
            _marketTaskChannel.Writer.TryComplete();
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Complete();
            }

            Logger.LogInformation("MarketDataEngine 资源已释放");
        }

        private void EnqueueMarketTask(
            string exchangeId,
            string symbol,
            string timeframe,
            long candleTimestamp,
            bool isBarClose = true)
        {
            if (candleTimestamp <= 0)
            {
                return;
            }

            var task = new MarketDataTask(exchangeId, symbol, timeframe, candleTimestamp, isBarClose);
            if (!_marketTaskChannel.Writer.TryWrite(task))
            {
                Logger.LogWarning($"[{exchangeId}] {symbol} {timeframe} 任务入队失败");
                _queueMonitor.OnEnqueueFailed();
                return;
            }

            _queueMonitor.OnEnqueueSuccess();

            foreach (var subscription in _subscriptions.Values)
            {
                if (subscription.OnlyBarClose && !isBarClose)
                {
                    continue;
                }

                if (!subscription.TryWrite(task))
                {
                    Logger.LogWarning(
                        "行情任务写入订阅队列失败: {Subscriber} {Exchange} {Symbol} {Timeframe}",
                        subscription.Name,
                        exchangeId,
                        symbol,
                        timeframe);
                }
            }

            // var modeText = isBarClose ? "收线" : "更新";
            // Logger.LogInformation(
            //     $"[{exchangeId}] {symbol} {timeframe} 实时行情任务入队({modeText}): time={FormatTimestamp(candleTimestamp)}");
        }

        private void EnqueueOnBarUpdateTasks(
            string exchangeId,
            string symbol,
            SymbolCache symbolCache,
            long currentTimestamp)
        {
            var timeframes = GetOtherTimeframes();
            foreach (var timeframe in timeframes)
            {
                if (!symbolCache.Timeframes.TryGetValue(timeframe, out var cacheTf))
                {
                    continue;
                }

                if (cacheTf.Count == 0)
                {
                    continue;
                }

                EnqueueMarketTask(exchangeId, symbol, timeframe, currentTimestamp, isBarClose: false);
            }
        }

        public static string FormatTimestamp(long timestamp)
        {
            if (timestamp <= 0)
            {
                return "N/A";
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
