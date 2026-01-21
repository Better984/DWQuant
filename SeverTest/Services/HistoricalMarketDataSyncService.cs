using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Db;
using ServerTest.Models;
using ServerTest.Options;

namespace ServerTest.Services
{
    public class HistoricalMarketDataSyncService : BaseService
    {
        private readonly IDbManager _dbManager;
        private readonly HistoricalMarketDataCache _cache;
        private readonly HistoricalMarketDataOptions _options;

        public HistoricalMarketDataSyncService(
            ILogger<HistoricalMarketDataSyncService> logger,
            IDbManager dbManager,
            HistoricalMarketDataCache cache,
            IOptions<HistoricalMarketDataOptions> options) : base(logger)
        {
            _dbManager = dbManager;
            _cache = cache;
            _options = options?.Value ?? new HistoricalMarketDataOptions();
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
                var tasks = new List<Task>();

                foreach (var exchangeEnum in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
                {
                    var exchangeId = MarketDataConfig.ExchangeToString(exchangeEnum);
                    if (!exchanges.TryGetValue(exchangeId, out var exchange))
                    {
                        continue;
                    }

                    foreach (var symbolEnum in Enum.GetValues<MarketDataConfig.SymbolEnum>())
                    {
                        var symbol = MarketDataConfig.SymbolToString(symbolEnum);
                        foreach (var timeframeEnum in Enum.GetValues<MarketDataConfig.TimeframeEnum>())
                        {
                            var timeframe = MarketDataConfig.TimeframeToString(timeframeEnum);
                            var localExchange = exchange;
                            // 每个交易所/币对/周期独立任务，后台并发执行
                            tasks.Add(SyncSingleAsync(localExchange, exchangeEnum, symbolEnum, timeframeEnum, symbol, timeframe, throttler, ct));
                        }
                    }
                }

                await Task.WhenAll(tasks);
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

        private async Task SyncSingleAsync(
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
                if (!await TableExistsAsync(tableName, ct))
                {
                    Logger.LogWarning("跳过同步，表不存在: {Table}", tableName);
                    return;
                }

                // 从最后一根K线时间开始增量补齐
                var lastOpenTime = await GetLastOpenTimeAsync(tableName, ct);
                var timeframeMs = MarketDataConfig.TimeframeToMs(timeframe);
                var nowMs = exchange.milliseconds();
                var minGapMs = TimeSpan.FromMinutes(_options.SyncMinGapMinutes).TotalMilliseconds;

                long startMs;
                if (lastOpenTime.HasValue)
                {
                    if (nowMs - lastOpenTime.Value < minGapMs)
                    {
                        Logger.LogInformation(
                            "历史行情无需同步：{Exchange} {Symbol} {Timeframe} 距离最新不足 {GapMinutes} 分钟",
                            exchangeEnum,
                            symbolEnum,
                            timeframeEnum,
                            _options.SyncMinGapMinutes);
                        return;
                    }

                    startMs = lastOpenTime.Value + timeframeMs;
                }
                else
                {
                    startMs = ResolveDefaultStartMs();
                }

                if (startMs <= 0 || startMs >= nowMs)
                {
                    return;
                }

                var endMs = nowMs - timeframeMs;
                if (endMs <= startMs)
                {
                    return;
                }

                var futuresSymbol = FindFuturesSymbol(exchange, symbol);
                // 拉取并写入数据库
                Logger.LogInformation(
                    "历史行情开始同步：{Exchange} {Symbol} {Timeframe} 起始={Start} 结束={End}",
                    exchangeEnum,
                    symbolEnum,
                    timeframeEnum,
                    DateTimeOffset.FromUnixTimeMilliseconds(startMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    DateTimeOffset.FromUnixTimeMilliseconds(endMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                await FetchAndInsertAsync(exchange, futuresSymbol, timeframe, timeframeMs, tableName, startMs, endMs, ct);
                Logger.LogInformation(
                    "历史行情同步完成：{Exchange} {Symbol} {Timeframe}",
                    exchangeEnum,
                    symbolEnum,
                    timeframeEnum);

                _cache.InvalidateCache(exchangeEnum, timeframeEnum, symbolEnum);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "历史行情同步失败: {Exchange} {Symbol} {Timeframe}", exchangeEnum, symbolEnum, timeframeEnum);
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
            const int maxLimitPerRequest = 1000;
            var limit = Math.Min(_options.SyncBatchSize, maxLimitPerRequest);
            var since = startMs;

            while (!ct.IsCancellationRequested && since <= endMs)
            {
                // 分批拉取，避免单次过大
                var ohlcvs = await exchange.FetchOHLCV(symbol, timeframe, since, limit);
                if (ohlcvs == null || ohlcvs.Count == 0)
                {
                    break;
                }

                var rows = new List<KlineInsertRow>();
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

                    rows.Add(new KlineInsertRow
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
                await InsertRowsAsync(tableName, rows, ct);

                var lastTimestamp = rows[^1].OpenTime;
                if (lastTimestamp <= since)
                {
                    break;
                }

                since = lastTimestamp + timeframeMs;
            }
        }

        private async Task InsertRowsAsync(string tableName, List<KlineInsertRow> rows, CancellationToken ct)
        {
            const string sqlTemplate = @"INSERT IGNORE INTO `{0}` 
(`open_time`, `open`, `high`, `low`, `close`, `volume`, `close_time`)
VALUES (@OpenTime, @Open, @High, @Low, @Close, @Volume, @CloseTime);";

            var sql = string.Format(sqlTemplate, tableName);
            await _dbManager.ExecuteAsync(sql, rows, null, ct);
        }

        private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
        {
            const string sql = @"SELECT 1 FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = @Table
LIMIT 1;";
            var exists = await _dbManager.QuerySingleOrDefaultAsync<int?>(sql, new { Table = tableName }, null, ct);
            return exists.HasValue;
        }

        private async Task<long?> GetLastOpenTimeAsync(string tableName, CancellationToken ct)
        {
            var sql = $"SELECT MAX(open_time) FROM `{tableName}`;";
            return await _dbManager.ExecuteScalarAsync<long?>(sql, null, null, ct);
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
            foreach (var exchangeEnum in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                var exchangeId = MarketDataConfig.ExchangeToString(exchangeEnum);
                var options = MarketDataConfig.GetExchangeOptions(exchangeEnum);

                Exchange exchange = exchangeEnum switch
                {
                    MarketDataConfig.ExchangeEnum.Binance => new ccxt.binanceusdm(options),
                    MarketDataConfig.ExchangeEnum.OKX => new ccxt.okx(options),
                    MarketDataConfig.ExchangeEnum.Bitget => new ccxt.bitget(options),
                    _ => throw new NotSupportedException($"Unsupported exchange: {exchangeEnum}")
                };

                // 预加载交易所市场信息，保证后续查询可用
                await exchange.LoadMarkets();
                map[exchangeId] = exchange;
                ct.ThrowIfCancellationRequested();
            }

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

        private sealed class KlineInsertRow
        {
            public long OpenTime { get; set; }
            public decimal? Open { get; set; }
            public decimal? High { get; set; }
            public decimal? Low { get; set; }
            public decimal? Close { get; set; }
            public decimal? Volume { get; set; }
            public long CloseTime { get; set; }
        }
    }
}
