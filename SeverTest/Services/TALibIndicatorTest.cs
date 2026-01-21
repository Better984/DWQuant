using ccxt;
using TALib;

namespace ServerTest.Services
{
    /// <summary>
    /// TALib 技术指标测试脚本
    /// 使用 CCXT 获取币安比特币 USDT 交易对 5 分钟周期的 1000 根 K 线
    /// 然后使用 TALib.NETCore 计算 MACD、MA、布林带、StochRSI 指标
    /// </summary>
    public class TALibIndicatorTest
    {
        /// <summary>
        /// 运行测试
        /// </summary>
        public static async Task RunAsync()
        {
            Console.WriteLine("========== TALib 技术指标测试 ==========\n");

            try
            {
                // 1. 初始化币安合约交易所
                Console.WriteLine("[1/5] 初始化币安合约交易所...");
                var exchange = new ccxt.binanceusdm(new Dictionary<string, object>());
                await exchange.LoadMarkets();
                Console.WriteLine("✓ 交易所初始化完成\n");

                // 2. 获取 BTC/USDT 5分钟K线数据（1000根）
                Console.WriteLine("[2/5] 获取 BTC/USDT 5分钟K线数据（1000根）...");
                var symbol = "BTC/USDT:USDT"; // 币安合约符号格式
                var timeframe = "5m";
                var targetBars = 1000;

                var candles = await FetchOhlcvWithLimit(exchange, symbol, timeframe, targetBars);
                
                if (candles.Count == 0)
                {
                    Console.WriteLine("❌ 未能获取到K线数据");
                    return;
                }

                Console.WriteLine($"✓ 成功获取 {candles.Count} 根K线数据");
                Console.WriteLine($"  第一根时间: {DateTimeOffset.FromUnixTimeMilliseconds((long)(candles[0].timestamp ?? 0)):yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  最后一根时间: {DateTimeOffset.FromUnixTimeMilliseconds((long)(candles[^1].timestamp ?? 0)):yyyy-MM-dd HH:mm:ss}\n");

                // 3. 提取价格数组（TALib 需要 double[] 数组）
                Console.WriteLine("[3/5] 提取价格数据...");
                var closePrices = candles.Select(c => c.close ?? 0).ToArray();
                Console.WriteLine($"✓ 价格数据提取完成，共 {closePrices.Length} 个数据点\n");

                // 4. 计算技术指标
                Console.WriteLine("[4/5] 计算技术指标...\n");

                // 4.1 计算移动平均线 MA (20周期)
                var ma20Output = new double[closePrices.Length];
                var ma20RetCode = Functions.Ma<double>(
                    closePrices,
                    new Range(0, closePrices.Length - 1),
                    ma20Output,
                    out var ma20OutRange,
                    20,
                    Core.MAType.Sma
                );
                
                double[] ma20Full = new double[closePrices.Length];
                int ma20BegIdx = 0, ma20NbElement = 0;
                if (ma20RetCode == Core.RetCode.Success)
                {
                    ProcessTaLibResult(ma20Full, ma20Output, ma20OutRange);
                    ma20BegIdx = ma20OutRange.Start.Value;
                    ma20NbElement = ma20OutRange.End.Value - ma20OutRange.Start.Value;
                    Console.WriteLine($"✓ MA(20) 计算成功，有效数据: {ma20NbElement} 个");
                }
                else
                {
                    Console.WriteLine($"❌ MA(20) 计算失败，错误码: {ma20RetCode}");
                }

                // 4.2 计算 MACD (12, 26, 9)
                var macdOutput = new double[closePrices.Length];
                var macdSignalOutput = new double[closePrices.Length];
                var macdHistOutput = new double[closePrices.Length];
                var macdRetCode = Functions.Macd<double>(
                    closePrices,
                    new Range(0, closePrices.Length - 1),
                    macdOutput,
                    macdSignalOutput,
                    macdHistOutput,
                    out var macdOutRange,
                    12,
                    26,
                    9
                );
                
                double[] macdFull = new double[closePrices.Length];
                double[] macdSignalFull = new double[closePrices.Length];
                double[] macdHistFull = new double[closePrices.Length];
                int macdBegIdx = 0, macdNbElement = 0;
                if (macdRetCode == Core.RetCode.Success)
                {
                    ProcessTaLibResult(macdFull, macdOutput, macdOutRange);
                    ProcessTaLibResult(macdSignalFull, macdSignalOutput, macdOutRange);
                    ProcessTaLibResult(macdHistFull, macdHistOutput, macdOutRange);
                    macdBegIdx = macdOutRange.Start.Value;
                    macdNbElement = macdOutRange.End.Value - macdOutRange.Start.Value;
                    Console.WriteLine($"✓ MACD(12,26,9) 计算成功，有效数据: {macdNbElement} 个");
                }
                else
                {
                    Console.WriteLine($"❌ MACD 计算失败，错误码: {macdRetCode}");
                }

                // 4.3 计算布林带 BBANDS (20周期, 2倍标准差)
                var bbUpperOutput = new double[closePrices.Length];
                var bbMiddleOutput = new double[closePrices.Length];
                var bbLowerOutput = new double[closePrices.Length];
                var bbRetCode = Functions.Bbands<double>(
                    closePrices,
                    new Range(0, closePrices.Length - 1),
                    bbUpperOutput,
                    bbMiddleOutput,
                    bbLowerOutput,
                    out var bbOutRange,
                    20,
                    2.0,
                    2.0,
                    Core.MAType.Sma
                );
                
                double[] bbUpperFull = new double[closePrices.Length];
                double[] bbMiddleFull = new double[closePrices.Length];
                double[] bbLowerFull = new double[closePrices.Length];
                int bbBegIdx = 0, bbNbElement = 0;
                if (bbRetCode == Core.RetCode.Success)
                {
                    ProcessTaLibResult(bbUpperFull, bbUpperOutput, bbOutRange);
                    ProcessTaLibResult(bbMiddleFull, bbMiddleOutput, bbOutRange);
                    ProcessTaLibResult(bbLowerFull, bbLowerOutput, bbOutRange);
                    bbBegIdx = bbOutRange.Start.Value;
                    bbNbElement = bbOutRange.End.Value - bbOutRange.Start.Value;
                    Console.WriteLine($"✓ 布林带(20,2) 计算成功，有效数据: {bbNbElement} 个");
                }
                else
                {
                    Console.WriteLine($"❌ 布林带 计算失败，错误码: {bbRetCode}");
                }

                // 4.4 计算 StochRSI (14周期, 14周期, 3周期)
                var stochRsiKOutput = new double[closePrices.Length];
                var stochRsiDOutput = new double[closePrices.Length];
                var stochRsiRetCode = Functions.StochRsi<double>(
                    closePrices,
                    new Range(0, closePrices.Length - 1),
                    stochRsiKOutput,
                    stochRsiDOutput,
                    out var stochRsiOutRange,
                    14,
                    14,
                    3
                );
                
                double[] stochRsiKFull = new double[closePrices.Length];
                double[] stochRsiDFull = new double[closePrices.Length];
                int stochRsiBegIdx = 0, stochRsiNbElement = 0;
                if (stochRsiRetCode == Core.RetCode.Success)
                {
                    ProcessTaLibResult(stochRsiKFull, stochRsiKOutput, stochRsiOutRange);
                    ProcessTaLibResult(stochRsiDFull, stochRsiDOutput, stochRsiOutRange);
                    stochRsiBegIdx = stochRsiOutRange.Start.Value;
                    stochRsiNbElement = stochRsiOutRange.End.Value - stochRsiOutRange.Start.Value;
                    Console.WriteLine($"✓ StochRSI(14,14,3) 计算成功，有效数据: {stochRsiNbElement} 个");
                }
                else
                {
                    Console.WriteLine($"❌ StochRSI 计算失败，错误码: {stochRsiRetCode}");
                }

                Console.WriteLine();

                // 5. 输出结果（显示最后10根K线的指标值）
                Console.WriteLine("[5/5] 输出计算结果（最后10根K线）...\n");
                Console.WriteLine("=".PadRight(150, '='));
                Console.WriteLine($"{"时间",-20} {"收盘价",-12} {"MA(20)",-12} {"MACD",-12} {"MACD信号",-12} {"MACD柱",-12} {"BB上轨",-12} {"BB中轨",-12} {"BB下轨",-12} {"StochRSI_K",-12} {"StochRSI_D",-12}");
                Console.WriteLine("=".PadRight(150, '='));

                int displayCount = Math.Min(10, candles.Count);
                int startIdx = candles.Count - displayCount;

                for (int i = startIdx; i < candles.Count; i++)
                {
                    var candle = candles[i];
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(candle.timestamp ?? 0));
                    var timeStr = timestamp.ToString("yyyy-MM-dd HH:mm");

                    // 获取指标值
                    var ma20Val = ma20Full[i];
                    var macdVal = macdFull[i];
                    var macdSigVal = macdSignalFull[i];
                    var macdHistVal = macdHistFull[i];
                    var bbUpperVal = bbUpperFull[i];
                    var bbMiddleVal = bbMiddleFull[i];
                    var bbLowerVal = bbLowerFull[i];
                    var stochRsiKVal = stochRsiKFull[i];
                    var stochRsiDVal = stochRsiDFull[i];

                    Console.WriteLine($"{timeStr,-20} " +
                        $"{candle.close?.ToString("F2"),-12} " +
                        $"{(double.IsNaN(ma20Val) ? "N/A" : ma20Val.ToString("F2")),-12} " +
                        $"{(double.IsNaN(macdVal) ? "N/A" : macdVal.ToString("F4")),-12} " +
                        $"{(double.IsNaN(macdSigVal) ? "N/A" : macdSigVal.ToString("F4")),-12} " +
                        $"{(double.IsNaN(macdHistVal) ? "N/A" : macdHistVal.ToString("F4")),-12} " +
                        $"{(double.IsNaN(bbUpperVal) ? "N/A" : bbUpperVal.ToString("F2")),-12} " +
                        $"{(double.IsNaN(bbMiddleVal) ? "N/A" : bbMiddleVal.ToString("F2")),-12} " +
                        $"{(double.IsNaN(bbLowerVal) ? "N/A" : bbLowerVal.ToString("F2")),-12} " +
                        $"{(double.IsNaN(stochRsiKVal) ? "N/A" : stochRsiKVal.ToString("F2")),-12} " +
                        $"{(double.IsNaN(stochRsiDVal) ? "N/A" : stochRsiDVal.ToString("F2")),-12}");
                }

                Console.WriteLine("=".PadRight(150, '='));
                Console.WriteLine("\n✓ 测试完成！");

                // 输出统计信息
                Console.WriteLine("\n========== 统计信息 ==========");
                Console.WriteLine($"总K线数: {candles.Count}");
                Console.WriteLine($"MA(20) 有效数据: {ma20NbElement} 个 (起始索引: {ma20BegIdx})");
                Console.WriteLine($"MACD 有效数据: {macdNbElement} 个 (起始索引: {macdBegIdx})");
                Console.WriteLine($"布林带 有效数据: {bbNbElement} 个 (起始索引: {bbBegIdx})");
                Console.WriteLine($"StochRSI 有效数据: {stochRsiNbElement} 个 (起始索引: {stochRsiBegIdx})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 测试失败: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 处理 TALib 计算结果，将有效数据填充到完整数组中（前面填充 NaN）
        /// </summary>
        private static void ProcessTaLibResult(double[] fullArray, double[] outputArray, Range outRange)
        {
            // 填充 NaN 到有效数据开始之前
            int startIndex = outRange.Start.Value;
            int validLength = outRange.End.Value - outRange.Start.Value;
            
            for (int i = 0; i < startIndex; i++)
            {
                fullArray[i] = double.NaN;
            }
            
            // 复制有效数据
            for (int i = 0; i < validLength && i < outputArray.Length; i++)
            {
                fullArray[startIndex + i] = outputArray[i];
            }
            
            // 填充剩余位置为 NaN（如果有）
            for (int i = startIndex + validLength; i < fullArray.Length; i++)
            {
                fullArray[i] = double.NaN;
            }
        }

        /// <summary>
        /// 拉取指定数量的K线（参考 MarketDataEngine 中的方法）
        /// </summary>
        private static async Task<List<OHLCV>> FetchOhlcvWithLimit(Exchange exchange, string symbol, string timeframe, int targetBars)
        {
            // 计算时间周期（毫秒）
            var tfMs = TimeframeToMs(timeframe);
            var result = new List<OHLCV>();
            var now = exchange.milliseconds();
            var since = Math.Max(now - tfMs * targetBars, 1577808000); // 2020-01-01 作为最早时间
            const int maxLimitPerRequest = 1000;

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
        /// 将时间周期字符串转换为毫秒数
        /// </summary>
        private static long TimeframeToMs(string timeframe)
        {
            return timeframe switch
            {
                "1m" => 60 * 1000,
                "5m" => 5 * 60 * 1000,
                "15m" => 15 * 60 * 1000,
                "30m" => 30 * 60 * 1000,
                "1h" => 60 * 60 * 1000,
                "4h" => 4 * 60 * 60 * 1000,
                "1d" => 24 * 60 * 60 * 1000,
                _ => throw new ArgumentException($"不支持的时间周期: {timeframe}")
            };
        }
    }
}
