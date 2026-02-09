using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测统计指标构建器（从 BacktestRunner 拆分）
    /// 包含基础指标和高级指标（夏普比率、Sortino、年化收益等）
    /// </summary>
    internal static class BacktestStatisticsBuilder
    {
        /// <summary>
        /// 构建完整的统计指标
        /// </summary>
        public static BacktestStats BuildStats(
            IReadOnlyList<BacktestTrade> trades,
            IReadOnlyList<BacktestEquityPoint>? equityCurve,
            decimal initialCapital)
        {
            var tradeCount = trades.Count;
            var totalProfit = 0m;
            var winCount = 0;
            var lossCount = 0;
            var winSum = 0m;
            var lossSum = 0m;
            foreach (var trade in trades)
            {
                totalProfit += trade.PnL;
                if (trade.PnL > 0)
                {
                    winCount++;
                    winSum += trade.PnL;
                }
                else if (trade.PnL < 0)
                {
                    lossCount++;
                    lossSum += trade.PnL;
                }
            }

            var winRate = tradeCount > 0 ? winCount / (decimal)tradeCount : 0m;
            var avgProfit = tradeCount > 0 ? totalProfit / tradeCount : 0m;
            var avgWin = winCount > 0 ? winSum / winCount : 0m;
            var avgLoss = lossCount > 0 ? lossSum / lossCount : 0m;
            var profitFactor = lossSum < 0 ? winSum / Math.Abs(lossSum) : 0m;
            var totalReturn = initialCapital > 0 ? totalProfit / initialCapital : 0m;

            var maxDrawdown = equityCurve == null || equityCurve.Count == 0
                ? 0m
                : ComputeMaxDrawdown(equityCurve);

            // 高级指标
            var maxConsecutiveLosses = ComputeMaxConsecutiveLosses(trades);
            var maxConsecutiveWins = ComputeMaxConsecutiveWins(trades);
            var avgHoldingMs = ComputeAvgHoldingMs(trades);
            var maxDrawdownDurationMs = equityCurve == null || equityCurve.Count == 0
                ? 0L
                : ComputeMaxDrawdownDurationMs(equityCurve);

            // 时间跨度（用于年化计算）
            var durationMs = ComputeTradingDurationMs(trades);
            var annualizedReturn = ComputeAnnualizedReturn(totalReturn, durationMs);
            var returnStatistics = ComputeReturnStatistics(trades, equityCurve, initialCapital);
            var sharpeRatio = ComputeSharpeRatio(trades, returnStatistics);
            var sortinoRatio = ComputeSortinoRatio(trades, returnStatistics);
            var calmarRatio = maxDrawdown > 0 ? annualizedReturn / maxDrawdown : 0m;

            return new BacktestStats
            {
                TotalProfit = totalProfit,
                TotalReturn = totalReturn,
                MaxDrawdown = maxDrawdown,
                WinRate = winRate,
                TradeCount = tradeCount,
                AvgProfit = avgProfit,
                ProfitFactor = profitFactor,
                AvgWin = avgWin,
                AvgLoss = avgLoss,
                // 高级指标
                SharpeRatio = sharpeRatio,
                SortinoRatio = sortinoRatio,
                AnnualizedReturn = annualizedReturn,
                MaxConsecutiveLosses = maxConsecutiveLosses,
                MaxConsecutiveWins = maxConsecutiveWins,
                AvgHoldingMs = avgHoldingMs,
                MaxDrawdownDurationMs = maxDrawdownDurationMs,
                CalmarRatio = calmarRatio
            };
        }

        /// <summary>
        /// 最大回撤（峰值回撤比例）
        /// </summary>
        public static decimal ComputeMaxDrawdown(IReadOnlyList<BacktestEquityPoint> curve)
        {
            var peak = 0m;
            var maxDrawdown = 0m;

            foreach (var point in curve)
            {
                if (point.Equity > peak)
                    peak = point.Equity;

                if (peak <= 0)
                    continue;

                var drawdown = (peak - point.Equity) / peak;
                if (drawdown > maxDrawdown)
                    maxDrawdown = drawdown;
            }

            return maxDrawdown;
        }

        /// <summary>
        /// 最大连续亏损次数
        /// </summary>
        public static int ComputeMaxConsecutiveLosses(IReadOnlyList<BacktestTrade> trades)
        {
            var maxStreak = 0;
            var currentStreak = 0;

            foreach (var trade in trades)
            {
                if (trade.PnL < 0)
                {
                    currentStreak++;
                    if (currentStreak > maxStreak)
                        maxStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }

            return maxStreak;
        }

        /// <summary>
        /// 最大连续盈利次数
        /// </summary>
        public static int ComputeMaxConsecutiveWins(IReadOnlyList<BacktestTrade> trades)
        {
            var maxStreak = 0;
            var currentStreak = 0;

            foreach (var trade in trades)
            {
                if (trade.PnL > 0)
                {
                    currentStreak++;
                    if (currentStreak > maxStreak)
                        maxStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }

            return maxStreak;
        }

        /// <summary>
        /// 平均持仓时间（毫秒）
        /// </summary>
        public static long ComputeAvgHoldingMs(IReadOnlyList<BacktestTrade> trades)
        {
            if (trades.Count == 0)
                return 0;

            var totalMs = 0L;
            foreach (var trade in trades)
            {
                totalMs += Math.Max(0, trade.ExitTime - trade.EntryTime);
            }

            return totalMs / trades.Count;
        }

        /// <summary>
        /// 最大回撤持续时间（从峰值到恢复的毫秒数）
        /// </summary>
        public static long ComputeMaxDrawdownDurationMs(IReadOnlyList<BacktestEquityPoint> curve)
        {
            if (curve.Count == 0)
                return 0;

            var peak = 0m;
            var peakTimestamp = curve[0].Timestamp;
            var maxDuration = 0L;

            foreach (var point in curve)
            {
                if (point.Equity >= peak)
                {
                    // 新高或恢复
                    if (peak > 0 && point.Equity >= peak)
                    {
                        var duration = point.Timestamp - peakTimestamp;
                        if (duration > maxDuration)
                            maxDuration = duration;
                    }

                    peak = point.Equity;
                    peakTimestamp = point.Timestamp;
                }
            }

            // 如果最后还在回撤中，计算到最后一个点
            if (curve.Count > 0)
            {
                var lastPoint = curve[^1];
                if (lastPoint.Equity < peak)
                {
                    var duration = lastPoint.Timestamp - peakTimestamp;
                    if (duration > maxDuration)
                        maxDuration = duration;
                }
            }

            return maxDuration;
        }

        /// <summary>
        /// 交易时间跨度（毫秒），用于年化计算
        /// </summary>
        public static long ComputeTradingDurationMs(IReadOnlyList<BacktestTrade> trades)
        {
            if (trades.Count == 0)
                return 0;

            var firstEntry = long.MaxValue;
            var lastExit = 0L;

            foreach (var trade in trades)
            {
                if (trade.EntryTime > 0 && trade.EntryTime < firstEntry)
                    firstEntry = trade.EntryTime;
                if (trade.ExitTime > lastExit)
                    lastExit = trade.ExitTime;
            }

            return firstEntry == long.MaxValue ? 0 : Math.Max(0, lastExit - firstEntry);
        }

        /// <summary>
        /// 年化收益率
        /// </summary>
        public static decimal ComputeAnnualizedReturn(decimal totalReturn, long durationMs)
        {
            if (durationMs <= 0 || totalReturn == 0)
                return 0m;

            const long MillisPerYear = 365L * 24 * 60 * 60 * 1000;
            var years = (double)durationMs / MillisPerYear;
            if (years < 0.001)
                return 0m;

            // 年化收益率 = (1 + 总收益率)^(1/years) - 1
            var annualized = Math.Pow(1.0 + (double)totalReturn, 1.0 / years) - 1.0;
            return (decimal)annualized;
        }

        private readonly struct ReturnStatistics
        {
            public static readonly ReturnStatistics Empty = new(0, 0d, 0d, 0d);

            public ReturnStatistics(int count, double mean, double sampleVariance, double downsideVariance)
            {
                Count = count;
                Mean = mean;
                SampleVariance = sampleVariance;
                DownsideVariance = downsideVariance;
            }

            public int Count { get; }
            public double Mean { get; }
            public double SampleVariance { get; }
            public double DownsideVariance { get; }
        }

        /// <summary>
        /// 夏普比率（基于每笔交易收益率，无风险利率假定为 0）
        /// 如果有资金曲线则按区间收益率计算，否则按交易收益率计算
        /// </summary>
        public static decimal ComputeSharpeRatio(
            IReadOnlyList<BacktestTrade> trades,
            IReadOnlyList<BacktestEquityPoint>? equityCurve,
            decimal initialCapital)
        {
            var returnStatistics = ComputeReturnStatistics(trades, equityCurve, initialCapital);
            return ComputeSharpeRatio(trades, returnStatistics);
        }

        /// <summary>
        /// Sortino 比率（仅考虑下行波动率）
        /// </summary>
        public static decimal ComputeSortinoRatio(
            IReadOnlyList<BacktestTrade> trades,
            IReadOnlyList<BacktestEquityPoint>? equityCurve,
            decimal initialCapital)
        {
            var returnStatistics = ComputeReturnStatistics(trades, equityCurve, initialCapital);
            return ComputeSortinoRatio(trades, returnStatistics);
        }

        private static decimal ComputeSharpeRatio(
            IReadOnlyList<BacktestTrade> trades,
            ReturnStatistics returnStatistics)
        {
            if (returnStatistics.Count < 2)
            {
                return 0m;
            }

            var stdDev = Math.Sqrt(returnStatistics.SampleVariance);
            if (stdDev < 1e-12)
            {
                return 0m;
            }

            var sharpe = returnStatistics.Mean / stdDev;
            sharpe = AnnualizeRiskAdjustedRatio(sharpe, trades, returnStatistics.Count);
            return (decimal)sharpe;
        }

        private static decimal ComputeSortinoRatio(
            IReadOnlyList<BacktestTrade> trades,
            ReturnStatistics returnStatistics)
        {
            if (returnStatistics.Count < 2)
            {
                return 0m;
            }

            var downsideStdDev = Math.Sqrt(returnStatistics.DownsideVariance);
            if (downsideStdDev < 1e-12)
            {
                return 0m;
            }

            var sortino = returnStatistics.Mean / downsideStdDev;
            sortino = AnnualizeRiskAdjustedRatio(sortino, trades, returnStatistics.Count);
            return (decimal)sortino;
        }

        private static ReturnStatistics ComputeReturnStatistics(
            IReadOnlyList<BacktestTrade> trades,
            IReadOnlyList<BacktestEquityPoint>? equityCurve,
            decimal initialCapital)
        {
            var returns = ExtractPeriodReturns(trades, equityCurve, initialCapital);
            if (returns.Count == 0)
            {
                return ReturnStatistics.Empty;
            }

            var sum = 0d;
            var sumSquares = 0d;
            var downsideSquares = 0d;
            foreach (var item in returns)
            {
                sum += item;
                sumSquares += item * item;
                if (item < 0d)
                {
                    downsideSquares += item * item;
                }
            }

            var mean = sum / returns.Count;
            var varianceNumerator = sumSquares - returns.Count * mean * mean;
            if (varianceNumerator < 0d && varianceNumerator > -1e-12)
            {
                varianceNumerator = 0d;
            }

            var sampleVariance = returns.Count > 1 ? varianceNumerator / (returns.Count - 1) : 0d;
            if (sampleVariance < 0d)
            {
                sampleVariance = 0d;
            }

            var downsideVariance = downsideSquares / returns.Count;
            return new ReturnStatistics(returns.Count, mean, sampleVariance, downsideVariance);
        }

        private static double AnnualizeRiskAdjustedRatio(
            double ratio,
            IReadOnlyList<BacktestTrade> trades,
            int periods)
        {
            if (periods <= 0)
            {
                return ratio;
            }

            var durationMs = ComputeTradingDurationMs(trades);
            if (durationMs <= 0)
            {
                return ratio;
            }

            const long MillisPerYear = 365L * 24 * 60 * 60 * 1000;
            var periodsPerYear = (double)MillisPerYear / ((double)durationMs / periods);
            return periodsPerYear > 0d ? ratio * Math.Sqrt(periodsPerYear) : ratio;
        }

        /// <summary>
        /// 提取收益率序列（每笔交易的收益率，或资金曲线区间收益率）
        /// </summary>
        private static List<double> ExtractPeriodReturns(
            IReadOnlyList<BacktestTrade> trades,
            IReadOnlyList<BacktestEquityPoint>? equityCurve,
            decimal initialCapital)
        {
            // 优先使用资金曲线的区间收益率（更准确）
            if (equityCurve != null && equityCurve.Count >= 2 && initialCapital > 0)
            {
                var returns = new List<double>(equityCurve.Count - 1);
                for (var i = 1; i < equityCurve.Count; i++)
                {
                    var prevEquity = equityCurve[i - 1].Equity;
                    if (prevEquity > 0)
                    {
                        returns.Add((double)(equityCurve[i].Equity - prevEquity) / (double)prevEquity);
                    }
                }
                return returns;
            }

            // 回退到每笔交易的收益率
            if (trades.Count == 0 || initialCapital <= 0)
                return new List<double>();

            var result = new List<double>(trades.Count);
            foreach (var trade in trades)
            {
                result.Add((double)(trade.PnL / initialCapital));
            }

            return result;
        }

        /// <summary>
        /// 构建交易明细汇总
        /// </summary>
        public static BacktestTradeSummary BuildTradeSummary(IReadOnlyList<BacktestTrade> trades)
        {
            var summary = new BacktestTradeSummary { TotalCount = trades.Count };
            if (trades.Count == 0)
                return summary;

            var winCount = 0;
            var lossCount = 0;
            var maxProfit = 0m;
            var maxLoss = 0m;
            var totalFee = 0m;
            var firstEntry = long.MaxValue;
            var lastExit = 0L;

            foreach (var trade in trades)
            {
                if (trade.PnL > 0m) winCount++;
                else if (trade.PnL < 0m) lossCount++;
                if (trade.PnL > maxProfit) maxProfit = trade.PnL;
                if (trade.PnL < maxLoss) maxLoss = trade.PnL;
                totalFee += trade.Fee;
                if (trade.EntryTime > 0 && trade.EntryTime < firstEntry) firstEntry = trade.EntryTime;
                if (trade.ExitTime > lastExit) lastExit = trade.ExitTime;
            }

            summary.WinCount = winCount;
            summary.LossCount = lossCount;
            summary.MaxProfit = maxProfit;
            summary.MaxLoss = maxLoss;
            summary.TotalFee = totalFee;
            summary.FirstEntryTime = firstEntry == long.MaxValue ? 0L : firstEntry;
            summary.LastExitTime = lastExit;
            return summary;
        }

        /// <summary>
        /// 构建资金曲线汇总
        /// </summary>
        public static BacktestEquitySummary BuildEquitySummary(IReadOnlyList<BacktestEquityPoint> curve)
        {
            var summary = new BacktestEquitySummary { PointCount = curve.Count };
            if (curve.Count == 0)
                return summary;

            var first = curve[0];
            var maxEquity = first.Equity;
            var minEquity = first.Equity;
            var maxEquityAt = first.Timestamp;
            var minEquityAt = first.Timestamp;
            var maxPeriodProfit = 0m;
            var maxPeriodLoss = 0m;
            var maxPeriodProfitAt = 0L;
            var maxPeriodLossAt = 0L;

            foreach (var point in curve)
            {
                if (point.Equity > maxEquity) { maxEquity = point.Equity; maxEquityAt = point.Timestamp; }
                if (point.Equity < minEquity) { minEquity = point.Equity; minEquityAt = point.Timestamp; }
                var periodPnl = point.PeriodRealizedPnl + point.PeriodUnrealizedPnl;
                if (periodPnl > maxPeriodProfit) { maxPeriodProfit = periodPnl; maxPeriodProfitAt = point.Timestamp; }
                if (periodPnl < maxPeriodLoss) { maxPeriodLoss = periodPnl; maxPeriodLossAt = point.Timestamp; }
            }

            summary.MaxEquity = maxEquity;
            summary.MinEquity = minEquity;
            summary.MaxEquityAt = maxEquityAt;
            summary.MinEquityAt = minEquityAt;
            summary.MaxPeriodProfit = maxPeriodProfit;
            summary.MaxPeriodProfitAt = maxPeriodProfitAt;
            summary.MaxPeriodLoss = maxPeriodLoss;
            summary.MaxPeriodLossAt = maxPeriodLossAt;
            return summary;
        }

        /// <summary>
        /// 构建事件日志汇总
        /// </summary>
        public static BacktestEventSummary BuildEventSummary(IReadOnlyList<BacktestEvent> events)
        {
            var summary = new BacktestEventSummary { TotalCount = events.Count };
            if (events.Count == 0)
                return summary;

            var first = long.MaxValue;
            var last = 0L;
            var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                if (evt.Timestamp > 0 && evt.Timestamp < first) first = evt.Timestamp;
                if (evt.Timestamp > last) last = evt.Timestamp;
                var type = string.IsNullOrWhiteSpace(evt.Type) ? "unknown" : evt.Type;
                typeCounts[type] = typeCounts.TryGetValue(type, out var c) ? c + 1 : 1;
            }

            summary.FirstTimestamp = first == long.MaxValue ? 0L : first;
            summary.LastTimestamp = last;
            summary.TypeCounts = typeCounts;
            return summary;
        }

        /// <summary>
        /// 合并多标的资金曲线
        /// </summary>
        public static List<BacktestEquityPoint> BuildTotalEquityCurve(List<List<BacktestEquityPoint>> curves)
        {
            if (curves.Count == 0)
                return new List<BacktestEquityPoint>();

            var minCount = curves.Min(c => c.Count);
            if (minCount <= 0)
                return new List<BacktestEquityPoint>();

            var result = new List<BacktestEquityPoint>(minCount);
            for (var i = 0; i < minCount; i++)
            {
                var timestamp = curves[0][i].Timestamp;
                var equity = 0m;
                var realized = 0m;
                var unrealized = 0m;
                var periodRealized = 0m;
                var periodUnrealized = 0m;

                foreach (var curve in curves)
                {
                    equity += curve[i].Equity;
                    realized += curve[i].RealizedPnl;
                    unrealized += curve[i].UnrealizedPnl;
                    periodRealized += curve[i].PeriodRealizedPnl;
                    periodUnrealized += curve[i].PeriodUnrealizedPnl;
                }

                result.Add(new BacktestEquityPoint
                {
                    Timestamp = timestamp,
                    Equity = equity,
                    RealizedPnl = realized,
                    UnrealizedPnl = unrealized,
                    PeriodRealizedPnl = periodRealized,
                    PeriodUnrealizedPnl = periodUnrealized
                });
            }

            return result;
        }

        /// <summary>
        /// 序列化列表为 JSON 字符串数组（前端按需解析）
        /// </summary>
        public static List<string> SerializeToRawList<T>(IReadOnlyList<T> items)
        {
            if (items.Count == 0)
                return new List<string>();

            var result = new List<string>(items.Count);
            foreach (var item in items)
            {
                result.Add(ProtocolJson.Serialize(item));
            }

            return result;
        }
    }
}
