using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Services;

namespace ServerTest.Modules.MarketData.Application
{
    public class HistoricalMarketDataSyncService : BaseService
    {
        private readonly HistoricalMarketDataRepository _repository;
        private readonly HistoricalMarketDataCache _cache;
        private readonly HistoricalMarketDataOptions _options;
        private readonly MarketDataQueryOptions _queryOptions;

        public HistoricalMarketDataSyncService(
            ILogger<HistoricalMarketDataSyncService> logger,
            HistoricalMarketDataRepository repository,
            HistoricalMarketDataCache cache,
            IOptions<HistoricalMarketDataOptions> options,
            IOptions<MarketDataQueryOptions> queryOptions) : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _cache = cache;
            _options = options?.Value ?? new HistoricalMarketDataOptions();
            _queryOptions = queryOptions?.Value ?? new MarketDataQueryOptions();
        }

        public Task PreloadCacheAsync(DateTime startDate, CancellationToken ct)
        {
            // 预热缓存直接委托给缓存服务
            return _cache.WarmUpCacheAsync(startDate, ct);
        }

        public async Task SyncIfNeededAsync(CancellationToken ct)
        {
            // 初始化交易所连接，准备拉取历史数据
            var exchanges = await CreateExchangesAsync(ct);
            try
            {
                var throttler = new SemaphoreSlim(_options.SyncMaxParallel, _options.SyncMaxParallel);
                var tasks = new List<Task<SyncResult>>();

                // 只处理币安BTC的数据
                var exchangeEnum = MarketDataConfig.ExchangeEnum.Binance;
                var exchangeId = MarketDataConfig.ExchangeToString(exchangeEnum);
                if (exchanges.TryGetValue(exchangeId, out var exchange))
                {
                    var symbolEnum = MarketDataConfig.SymbolEnum.BTC_USDT;
                    var symbol = MarketDataConfig.SymbolToString(symbolEnum);
                    foreach (var timeframeEnum in Enum.GetValues<MarketDataConfig.TimeframeEnum>())
                    {
                        var timeframe = MarketDataConfig.TimeframeToString(timeframeEnum);
                        var localExchange = exchange;
                        // 每个周期独立任务，后台并发执行
                        tasks.Add(SyncSingleAsync(localExchange, exchangeEnum, symbolEnum, timeframeEnum, symbol, timeframe, throttler, ct));
                    }
                }

                var results = await Task.WhenAll(tasks);
                
                // 统计并输出所有结果
                var successCount = results.Count(r => r.Status == SyncStatus.Success);
                var skippedCount = results.Count(r => r.Status == SyncStatus.Skipped);
                var failedCount = results.Count(r => r.Status == SyncStatus.Failed);
                
                Logger.LogInformation(
                    "历史行情同步完成：成功={Success} 跳过={Skipped} 失败={Failed} 总计={Total}",
                    successCount, skippedCount, failedCount, results.Length);
                
                // 输出失败详情
                var failedResults = results.Where(r => r.Status == SyncStatus.Failed).ToList();
                if (failedResults.Any())
                {
                    foreach (var result in failedResults)
                    {
                        Logger.LogWarning(
                            "历史行情同步失败: {Exchange} {Symbol} {Timeframe} - {Error}",
                            result.Exchange, result.Symbol, result.Timeframe, result.ErrorMessage);
                    }
                }
            }
            finally
            {
                foreach (var exchange in exchanges.Values)
                {
                    if (exchange is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }

        private async Task<SyncResult> SyncSingleAsync(
            Exchange exchange,
            MarketDataConfig.ExchangeEnum exchangeEnum,
            MarketDataConfig.SymbolEnum symbolEnum,
            MarketDataConfig.TimeframeEnum timeframeEnum,
            string symbol,
            string timeframe,
            SemaphoreSlim throttler,
            CancellationToken ct)
        {
            await throttler.WaitAsync(ct);
            try
            {
                var tableName = BuildTableName(exchangeEnum, symbolEnum, timeframeEnum);
                if (!await _repository.TableExistsAsync(tableName, ct))
                {
                    return new SyncResult
                    {
                        Exchange = exchangeEnum.ToString(),
                        Symbol = symbolEnum.ToString(),
                        Timeframe = timeframeEnum.ToString(),
                        Status = SyncStatus.Skipped,
                        ErrorMessage = $"表不存在: {tableName}"
                    };
                }

                // 从最后一根K线时间开始增量补齐
                var lastOpenTime = await _repository.GetLastOpenTimeAsync(tableName, ct);
                var timeframeMs = MarketDataConfig.TimeframeToMs(timeframe);
                var nowMs = exchange.milliseconds();
                var minGapMs = TimeSpan.FromMinutes(_options.SyncMinGapMinutes).TotalMilliseconds;

                long startMs;
                if (lastOpenTime.HasValue)
                {
                    if (nowMs - lastOpenTime.Value < minGapMs)
                    {
                        return new SyncResult
                        {
                            Exchange = exchangeEnum.ToString(),
                            Symbol = symbolEnum.ToString(),
                            Timeframe = timeframeEnum.ToString(),
                            Status = SyncStatus.Skipped,
                            ErrorMessage = $"距离最新不足 {_options.SyncMinGapMinutes} 分钟"
                        };
                    }

                    startMs = lastOpenTime.Value + timeframeMs;
                }
                else
                {
                    startMs = ResolveDefaultStartMs();
                }

                if (startMs <= 0 || startMs >= nowMs)
                {
                    return new SyncResult
                    {
                        Exchange = exchangeEnum.ToString(),
                        Symbol = symbolEnum.ToString(),
                        Timeframe = timeframeEnum.ToString(),
                        Status = SyncStatus.Skipped,
                        ErrorMessage = "时间范围无效"
                    };
                }

                var endMs = nowMs - timeframeMs;
                if (endMs <= startMs)
                {
                    return new SyncResult
                    {
                        Exchange = exchangeEnum.ToString(),
                        Symbol = symbolEnum.ToString(),
                        Timeframe = timeframeEnum.ToString(),
                        Status = SyncStatus.Skipped,
                        ErrorMessage = "结束时间小于等于开始时间"
                    };
                }

                var futuresSymbol = FindFuturesSymbol(exchange, symbol);
                // 拉取并写入数据库
                await FetchAndInsertAsync(exchange, futuresSymbol, timeframe, timeframeMs, tableName, startMs, endMs, ct);

                _cache.InvalidateCache(exchangeEnum, timeframeEnum, symbolEnum);

                return new SyncResult
                {
                    Exchange = exchangeEnum.ToString(),
                    Symbol = symbolEnum.ToString(),
                    Timeframe = timeframeEnum.ToString(),
                    Status = SyncStatus.Success
                };
            }
            catch (Exception ex)
            {
                return new SyncResult
                {
                    Exchange = exchangeEnum.ToString(),
                    Symbol = symbolEnum.ToString(),
                    Timeframe = timeframeEnum.ToString(),
                    Status = SyncStatus.Failed,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                throttler.Release();
            }
        }

        private async Task FetchAndInsertAsync(
            Exchange exchange,
            string symbol,
            string timeframe,
            long timeframeMs,
            string tableName,
            long startMs,
            long endMs,
            CancellationToken ct)
        {
            var limit = Math.Min(_options.SyncBatchSize, _queryOptions.MaxLimitPerRequest);
            var since = startMs;

            while (!ct.IsCancellationRequested && since <= endMs)
            {
                // 分批拉取，避免单次过大
                var ohlcvs = await exchange.FetchOHLCV(symbol, timeframe, since, limit);
                if (ohlcvs == null || ohlcvs.Count == 0)
                {
                    break;
                }

                var rows = new List<HistoricalMarketDataInsertRow>();
                foreach (var candle in ohlcvs)
                {
                    if (candle.timestamp == null)
                    {
                        continue;
                    }

                    var ts = (long)candle.timestamp;
                    if (ts < startMs || ts > endMs)
                    {
                        continue;
                    }

                    rows.Add(new HistoricalMarketDataInsertRow
                    {
                        OpenTime = ts,
                        Open = ToDecimal(candle.open),
                        High = ToDecimal(candle.high),
                        Low = ToDecimal(candle.low),
                        Close = ToDecimal(candle.close),
                        Volume = ToDecimal(candle.volume),
                        CloseTime = ts + timeframeMs - 1
                    });
                }

                if (rows.Count == 0)
                {
                    break;
                }

                // 批量入库，使用 INSERT IGNORE 去重
                await _repository.InsertRowsAsync(tableName, rows, ct);

                var lastTimestamp = rows[^1].OpenTime;
                if (lastTimestamp <= since)
                {
                    break;
                }

                since = lastTimestamp + timeframeMs;
            }
        }

        private static decimal? ToDecimal(double? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return Convert.ToDecimal(value.Value);
        }

        private long ResolveDefaultStartMs()
        {
            if (DateTime.TryParse(_options.DefaultStartDate, out var parsed))
            {
                return new DateTimeOffset(parsed).ToUnixTimeMilliseconds();
            }

            return DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        }

        private static string BuildTableName(
            MarketDataConfig.ExchangeEnum exchange,
            MarketDataConfig.SymbolEnum symbol,
            MarketDataConfig.TimeframeEnum timeframe)
        {
            var exchangeId = MarketDataConfig.ExchangeToString(exchange);
            var symbolStr = MarketDataConfig.SymbolToString(symbol);
            var timeframeStr = MarketDataConfig.TimeframeToString(timeframe);

            var symbolPart = symbolStr.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return $"{exchangeId}_futures_{symbolPart}_{timeframeStr}";
        }

        private async Task<Dictionary<string, Exchange>> CreateExchangesAsync(CancellationToken ct)
        {
            var map = new Dictionary<string, Exchange>();
            // 只创建币安交易所连接
            var exchangeEnum = MarketDataConfig.ExchangeEnum.Binance;
            var exchangeId = MarketDataConfig.ExchangeToString(exchangeEnum);
            var options = MarketDataConfig.GetExchangeOptions(exchangeEnum);

            Exchange exchange = new ccxt.binanceusdm(options);

            // 预加载交易所市场信息，保证后续查询可用
            await exchange.LoadMarkets();
            map[exchangeId] = exchange;
            ct.ThrowIfCancellationRequested();

            return map;
        }

        private string FindFuturesSymbol(Exchange exchange, string baseSymbol)
        {
            try
            {
                dynamic dynamicExchange = exchange;
                var markets = dynamicExchange.markets as Dictionary<string, object>;
                if (markets == null)
                {
                    return baseSymbol + ":USDT";
                }

                var possibleSymbols = new List<string>
                {
                    baseSymbol,
                    baseSymbol + ":USDT",
                    baseSymbol.Replace("/", string.Empty, StringComparison.Ordinal)
                };

                // 优先匹配合约市场
                foreach (var symbol in possibleSymbols)
                {
                    if (markets.ContainsKey(symbol))
                    {
                        if (markets[symbol] is Dictionary<string, object> marketDict)
                        {
                            var isSwap = marketDict.TryGetValue("swap", out var swap) && swap is bool swapBool && swapBool;
                            var isFuture = marketDict.TryGetValue("future", out var future) && future is bool futureBool && futureBool;
                            var isContract = marketDict.TryGetValue("contract", out var contract) && contract is bool contractBool && contractBool;
                            if (isSwap || isFuture || isContract)
                            {
                                return symbol;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[{Exchange}] 合约符号解析失败", exchange.id);
            }

            return baseSymbol + ":USDT";
        }

        private enum SyncStatus
        {
            Success,
            Skipped,
            Failed
        }

        private sealed class SyncResult
        {
            public string Exchange { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string Timeframe { get; set; } = string.Empty;
            public SyncStatus Status { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}
