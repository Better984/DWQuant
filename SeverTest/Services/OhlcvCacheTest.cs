using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ccxt;

namespace ServerTest.Services
{
    /// <summary>
    /// 多交易所 / 多交易对 / 多周期 K 线缓存测试
    /// 参考 d:\UGit\ccxt\cs\MultiExchangeOhlcvCache.cs 改写，
    /// 用于一次性拉取并缓存 K 线，然后输出每个周期第一条和最后一条 K 线的时间和收盘价。
    /// </summary>
    public static class OhlcvCacheTest
    {
        // 需要的K线根数
        private const int TargetBars = 2000;

        // 想要缓存的周期
        private static readonly string[] Timeframes =
        {
            "1m",
            "5m",
            "15m",
            "1h",
            "4h",
            "1d",
        };

        // 周期 -> 毫秒 映射
        private static readonly Dictionary<string, long> TimeframeToMsMap = new()
        {
            ["1m"] = 60L * 1000,
            ["3m"] = 3L * 60 * 1000,
            ["5m"] = 5L * 60 * 1000,
            ["15m"] = 15L * 60 * 1000,
            ["30m"] = 30L * 60 * 1000,
            ["1h"] = 60L * 60 * 1000,
            ["2h"] = 2L * 60 * 60 * 1000,
            ["4h"] = 4L * 60 * 60 * 1000,
            ["6h"] = 6L * 60 * 60 * 1000,
            ["8h"] = 8L * 60 * 60 * 1000,
            ["12h"] = 12L * 60 * 60 * 1000,
            ["1d"] = 24L * 60 * 60 * 1000,
            ["3d"] = 3L * 24 * 60 * 60 * 1000,
            ["1w"] = 7L * 24 * 60 * 60 * 1000,
            ["1mo"] = 30L * 24 * 60 * 60 * 1000,
        };

        // 缓存结构：exchangeId -> symbol -> timeframe -> List<OHLCV>
        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, List<OHLCV>>>> Cache = new();
        private static readonly object CacheLock = new();

        /// <summary>
        /// 入口方法：拉取并缓存多个交易所 / 交易对 / 周期的 K 线，并在控制台打印首尾两根。
        /// 注意：这是测试方法，不会自动在服务器启动时执行，需要你手动调用。
        /// 比如可以暂时在 Program.cs 顶部加一行：await OhlcvCacheTest.RunAsync();
        /// </summary>
        public static async Task RunAsync()
        {
            Console.WriteLine("开始拉取并缓存 Binance / OKX / Bitget 多交易对多周期历史K线...\n");

            // 可以根据需要自行增减交易对
            var symbols = new[]
            {
                "BTC/USDT",
                "ETH/USDT",
            };

            // 这里默认用现货，如果你想改成合约，可以自己把下面的 new ccxt.binance()
            // 换成 binanceusdm / okx + defaultType=swap / bitget + defaultType=swap
            var exchanges = new Dictionary<string, Exchange>
            {
                ["binance"] = new ccxt.binance(new Dictionary<string, object>()),
                ["okx"] = new ccxt.okx(new Dictionary<string, object>()),
                ["bitget"] = new ccxt.bitget(new Dictionary<string, object>()),
            };

            // 加载 markets
            foreach (var kv in exchanges)
            {
                var id = kv.Key;
                var ex = kv.Value;
                try
                {
                    Console.WriteLine($"[Init] 加载 {id} markets...");
                    await ex.LoadMarkets();
                    Console.WriteLine($"[Init] {id} markets 加载完成");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Init] 加载 {id} 失败: {e.Message}");
                    return;
                }
            }

            // 为每个交易所 / 交易对 / 周期拉取并缓存K线（并行）
            var fetchTasks = new List<Task>();

            foreach (var (id, exchange) in exchanges)
            {
                // 先创建第一层，避免并发时 KeyNotFound
                Cache[id] = new Dictionary<string, Dictionary<string, List<OHLCV>>>();

                foreach (var symbol in symbols)
                {
                    foreach (var timeframe in Timeframes)
                    {
                        var localId = id;
                        var localSymbol = symbol;
                        var localTimeframe = timeframe;
                        var localExchange = exchange;

                        fetchTasks.Add(Task.Run(async () =>
                        {
                            Console.WriteLine($"\n[{localId}] 开始拉取 {localSymbol} {localTimeframe} K线 (目标 {TargetBars} 根)...");
                            try
                            {
                                var candles = await FetchOhlcvWithLimit(localExchange, localSymbol, localTimeframe, TargetBars);

                                lock (CacheLock)
                                {
                                    if (!Cache[localId].TryGetValue(localSymbol, out var tfMap))
                                    {
                                        tfMap = new Dictionary<string, List<OHLCV>>();
                                        Cache[localId][localSymbol] = tfMap;
                                    }

                                    tfMap[localTimeframe] = candles;
                                }

                                Console.WriteLine($"[{localId}] {localSymbol} {localTimeframe} 实际缓存数量: {candles.Count} 根");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"[{localId}] 拉取 {localSymbol} {localTimeframe} 失败: {e.Message}");
                            }
                        }));
                    }
                }
            }

            // 等待全部任务完成
            await Task.WhenAll(fetchTasks);

            Console.WriteLine("\n全部完成，输出每个周期第一条和最新一条K线示例：\n");

            foreach (var (id, symbolMap) in Cache)
            {
                foreach (var (symbol, tfMap) in symbolMap)
                {
                    foreach (var (timeframe, candles) in tfMap)
                    {
                        if (candles.Count == 0)
                        {
                            continue;
                        }

                        var first = candles[0];
                        var last = candles[^1];

                        var firstTime = first.timestamp != null
                            ? UnixMsToDateTime((long)first.timestamp).ToString("yyyy-MM-dd HH:mm:ss")
                            : "null";
                        var lastTime = last.timestamp != null
                            ? UnixMsToDateTime((long)last.timestamp).ToString("yyyy-MM-dd HH:mm:ss")
                            : "null";

                        Console.WriteLine(
                            $"[{id}] {symbol} {timeframe} " +
                            $"第一根时间: {firstTime} close={first.close} " +
                            $"| 最新一根时间: {lastTime} close={last.close}");
                    }
                }
            }

            Console.WriteLine("\n示例结束。你可以在此基础上把 Cache 交给策略或持久化到数据库。\n");
        }

        /// <summary>
        /// 拉取指定交易所 / 交易对 / 周期 的历史K线，尽量补够 targetBars 根。
        /// 内部会自动分批请求，直到拿够或没有更多数据。
        /// </summary>
        private static async Task<List<OHLCV>> FetchOhlcvWithLimit(
            Exchange exchange,
            string symbol,
            string timeframe,
            int targetBars)
        {
            if (!TimeframeToMsMap.TryGetValue(timeframe, out var tfMs))
            {
                throw new ArgumentException($"不支持的周期: {timeframe}");
            }

            var result = new List<OHLCV>();
            var now = exchange.milliseconds();
            // 从现在往前推 targetBars 根的时间
            var since = now - tfMs * targetBars;

            // 每次请求的最大条数，绝大多数交易所 1000 没问题
            const int maxLimitPerRequest = 1000;

            while (result.Count < targetBars)
            {
                var remaining = targetBars - result.Count;
                var limit = Math.Min(remaining, maxLimitPerRequest);

                // 注意：FetchOHLCV 的签名来自官方示例
                var ohlcvs = await exchange.FetchOHLCV(symbol, timeframe, since, limit);

                if (ohlcvs == null || ohlcvs.Count == 0)
                {
                    // 没有更多数据了
                    break;
                }

                result.AddRange(ohlcvs);

                // 使用最后一根K线的时间推进 since，避免死循环
                var last = ohlcvs[^1];
                if (last.timestamp == null)
                {
                    break;
                }

                since = (long)last.timestamp + tfMs;
            }

            // 只保留最新 targetBars 根（如果多了的话）
            if (result.Count > targetBars)
            {
                result = result.GetRange(result.Count - targetBars, targetBars);
            }

            return result;
        }

        private static DateTime UnixMsToDateTime(long ms)
        {
            var epoch = DateTime.UnixEpoch;
            return epoch.AddMilliseconds(ms).ToLocalTime();
        }
    }
}

