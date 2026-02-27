using ccxt;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.StrategyEngine.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测动作执行器（内存撮合，K线收盘价成交）
    /// </summary>
    internal sealed class BacktestActionExecutor : IStrategyActionExecutor
    {
        internal sealed class SymbolState
        {
            public string Symbol { get; set; } = string.Empty;
            public decimal OrderQty { get; set; }
            public int Leverage { get; set; } = 1;
            public decimal? StopLossPct { get; set; }
            public decimal? TakeProfitPct { get; set; }
            public decimal FeeRate { get; set; }
            public decimal FundingRate { get; set; }
            public int SlippageBps { get; set; }
            public bool AutoReverse { get; set; }
            public decimal ContractSize { get; set; } = 1m;
            public ContractDetails? Contract { get; set; }
            /// <summary>累计资金费用</summary>
            public decimal AccumulatedFunding { get; set; }

            public BacktestPosition? Position { get; set; }
            public decimal RealizedPnl { get; set; }
            public List<BacktestTrade> Trades { get; } = new();
            public List<BacktestEvent> Events { get; } = new();
        }

        internal sealed class BacktestPosition
        {
            public string Side { get; set; } = string.Empty; // Long/Short
            public long EntryTime { get; set; }
            public decimal EntryPrice { get; set; }
            public decimal Qty { get; set; }
            public decimal ContractSize { get; set; }
            public decimal EntryFee { get; set; }
            public decimal? StopLossPrice { get; set; }
            public decimal? TakeProfitPrice { get; set; }
        }

        private readonly Dictionary<string, SymbolState> _states;
        private readonly Dictionary<string, OHLCV> _currentBars;

        public BacktestActionExecutor(Dictionary<string, SymbolState> states)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _currentBars = new Dictionary<string, OHLCV>(Math.Max(4, _states.Count), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 按时间点批量刷新当前价格快照。
        /// 注意：该方法由主循环在进入并行 Symbol 处理前串行调用，
        /// 并行阶段仅执行只读访问，避免字典并发写导致的线程安全问题。
        /// </summary>
        public void UpdateCurrentBars(IReadOnlyDictionary<string, OHLCV> bars)
        {
            if (bars == null || bars.Count == 0)
            {
                _currentBars.Clear();
                return;
            }

            _currentBars.Clear();
            _currentBars.EnsureCapacity(bars.Count);
            foreach (var item in bars)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    continue;
                }

                _currentBars[item.Key] = item.Value;
            }
        }

        public SymbolState? GetState(string symbol)
        {
            return _states.TryGetValue(symbol, out var state) ? state : null;
        }

        /// <summary>
        /// 强制平仓（用于回测收尾处理）
        /// </summary>
        public bool ClosePosition(string symbol, decimal price, long timestamp, string reason)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            if (!_states.TryGetValue(symbol, out var state))
            {
                return false;
            }

            return TryClosePosition(state, price, timestamp, reason);
        }

        public (bool Success, StringBuilder Message) Execute(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (context == null || method == null)
            {
                return BuildResult(method?.Method ?? "Unknown", false, "执行上下文为空");
            }

            var symbol = context.StrategyConfig?.Trade?.Symbol ?? string.Empty;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BuildResult(method.Method ?? "Unknown", false, "缺少交易标的");
            }

            if (!_states.TryGetValue(symbol, out var state))
            {
                return BuildResult(method.Method ?? "Unknown", false, "未找到回测标的状态");
            }

            if (!_currentBars.TryGetValue(symbol, out var bar))
            {
                return BuildResult(method.Method ?? "Unknown", false, "当前K线缺失");
            }

            var action = method.Param != null && method.Param.Length > 0 ? method.Param[0] : string.Empty;
            if (!TryMapAction(action, out var positionSide, out var orderSide, out var isClose))
            {
                return BuildResult(method.Method ?? "Unknown", false, "不支持的动作类型");
            }

            var closePrice = bar.close ?? bar.open ?? 0d;
            if (closePrice <= 0)
            {
                return BuildResult(method.Method ?? "Unknown", false, "K线收盘价无效");
            }

            var execPrice = BacktestSlippageHelper.ApplySlippage(Convert.ToDecimal(closePrice), orderSide, state.SlippageBps);

            if (isClose)
            {
                if (!TryClosePosition(state, execPrice, context.Task.CandleTimestamp, reason: "Signal"))
                {
                    return BuildResult(method.Method ?? "Unknown", false, "无可平仓位");
                }

                return BuildResult(method.Method ?? "Unknown", true, "已平仓");
            }

            if (state.Position != null)
            {
                if (string.Equals(state.Position.Side, positionSide, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildResult(method.Method ?? "Unknown", false, "已有同向持仓，忽略开仓");
                }

                if (!state.AutoReverse)
                {
                    return BuildResult(method.Method ?? "Unknown", false, "已有反向持仓且未开启自动反向");
                }

                TryClosePosition(state, execPrice, context.Task.CandleTimestamp, reason: "Reverse");
            }

            var qty = NormalizeOrderQty(state.OrderQty, state.Contract);
            if (qty <= 0)
            {
                return BuildResult(method.Method ?? "Unknown", false, "下单数量无效");
            }

            var entryFee = CalculateFee(execPrice, qty, state.ContractSize, state.FeeRate);
            var position = new BacktestPosition
            {
                Side = positionSide,
                EntryTime = context.Task.CandleTimestamp,
                EntryPrice = execPrice,
                Qty = qty,
                ContractSize = state.ContractSize,
                EntryFee = entryFee,
                StopLossPrice = BuildStopLossPrice(execPrice, state.StopLossPct, state.Leverage, positionSide),
                TakeProfitPrice = BuildTakeProfitPrice(execPrice, state.TakeProfitPct, state.Leverage, positionSide)
            };

            state.Position = position;
            state.Events.Add(new BacktestEvent
            {
                Timestamp = context.Task.CandleTimestamp,
                Type = "Open",
                Message = $"开仓 {positionSide} 价格={execPrice} 数量={qty}"
            });

            return BuildResult(method.Method ?? "Unknown", true, "已开仓");
        }

        /// <summary>
        /// 结算资金费率（每根 K 线调用一次，模拟 8h 结算周期）
        /// </summary>
        public void ApplyFundingRate(SymbolState state, OHLCV bar, long timestamp, long timeframeMs)
        {
            if (state.Position == null || state.FundingRate == 0m)
                return;

            // 简化模型：按比例将资金费率分摊到每根 K 线
            // 实际交易所每 8 小时结算一次，这里按时间比例分摊
            const long FundingIntervalMs = 8 * 60 * 60 * 1000L; // 8小时
            var ratio = (decimal)timeframeMs / FundingIntervalMs;
            var close = Convert.ToDecimal(bar.close ?? bar.open ?? 0d);
            if (close <= 0)
                return;

            var notional = close * state.Position.Qty * state.Position.ContractSize;
            var funding = notional * state.FundingRate * ratio;

            // 多头支付正资金费率，空头收取正资金费率
            if (state.Position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase))
            {
                state.AccumulatedFunding += funding;
                state.RealizedPnl -= funding;
            }
            else
            {
                state.AccumulatedFunding -= funding;
                state.RealizedPnl += funding;
            }
        }

        public bool TryProcessRisk(SymbolState state, OHLCV bar, long timestamp)
        {
            if (state.Position == null)
            {
                return false;
            }

            var close = bar.close ?? bar.open ?? 0d;
            var high = bar.high ?? close;
            var low = bar.low ?? close;
            if (close <= 0 || high <= 0 || low <= 0)
            {
                return false;
            }

            var position = state.Position;
            var stopLossHit = CheckStopLoss(position, Convert.ToDecimal(high), Convert.ToDecimal(low));
            var takeProfitHit = CheckTakeProfit(position, Convert.ToDecimal(high), Convert.ToDecimal(low));

            if (!stopLossHit && !takeProfitHit)
            {
                return false;
            }

            var reason = takeProfitHit ? "TakeProfit" : "StopLoss";
            var orderSide = position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";
            var execPrice = BacktestSlippageHelper.ApplySlippage(Convert.ToDecimal(close), orderSide, state.SlippageBps);
            return TryClosePosition(state, execPrice, timestamp, reason);
        }

        private bool TryClosePosition(SymbolState state, decimal price, long timestamp, string reason)
        {
            if (state.Position == null)
            {
                return false;
            }

            var position = state.Position;
            var exitFee = CalculateFee(price, position.Qty, position.ContractSize, state.FeeRate);
            var pnl = CalculatePnl(position, price) - position.EntryFee - exitFee;

            state.RealizedPnl += pnl;
            state.Trades.Add(new BacktestTrade
            {
                Symbol = state.Symbol,
                Side = position.Side,
                EntryTime = position.EntryTime,
                ExitTime = timestamp,
                EntryPrice = position.EntryPrice,
                ExitPrice = price,
                StopLossPrice = position.StopLossPrice,
                TakeProfitPrice = position.TakeProfitPrice,
                Qty = position.Qty,
                ContractSize = position.ContractSize,
                Fee = position.EntryFee + exitFee,
                PnL = pnl,
                ExitReason = reason,
                SlippageBps = state.SlippageBps
            });

            state.Events.Add(new BacktestEvent
            {
                Timestamp = timestamp,
                Type = "Close",
                Message = $"平仓 {position.Side} 价格={price} 原因={reason} 盈亏={pnl:F4}"
            });

            state.Position = null;
            return true;
        }

        private static decimal CalculatePnl(BacktestPosition position, decimal exitPrice)
        {
            if (position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase))
            {
                return (exitPrice - position.EntryPrice) * position.Qty * position.ContractSize;
            }

            return (position.EntryPrice - exitPrice) * position.Qty * position.ContractSize;
        }

        private static decimal CalculateFee(decimal price, decimal qty, decimal contractSize, decimal feeRate)
        {
            if (feeRate <= 0)
            {
                return 0m;
            }

            var notional = price * qty * contractSize;
            return notional * feeRate;
        }

        private static decimal NormalizeOrderQty(decimal qty, ContractDetails? contract)
        {
            if (qty <= 0)
            {
                return 0m;
            }

            if (contract?.AmountPrecision != null)
            {
                var digits = Math.Max(0, contract.AmountPrecision.Value);
                var factor = (decimal)Math.Pow(10, digits);
                qty = Math.Floor(qty * factor) / factor;
            }

            if (contract?.MinOrderAmount != null && qty < contract.MinOrderAmount.Value)
            {
                return 0m;
            }

            if (contract?.MaxOrderAmount != null && qty > contract.MaxOrderAmount.Value)
            {
                qty = contract.MaxOrderAmount.Value;
            }

            return qty;
        }

        private static decimal? BuildStopLossPrice(decimal entryPrice, decimal? stopLossPct, int leverage, string side)
        {
            if (!stopLossPct.HasValue || stopLossPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            var effectiveLeverage = Math.Max(1, leverage);
            var priceMovePct = stopLossPct.Value / effectiveLeverage;

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 - priceMovePct)
                : entryPrice * (1 + priceMovePct);
        }

        private static decimal? BuildTakeProfitPrice(decimal entryPrice, decimal? takeProfitPct, int leverage, string side)
        {
            if (!takeProfitPct.HasValue || takeProfitPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            var effectiveLeverage = Math.Max(1, leverage);
            var priceMovePct = takeProfitPct.Value / effectiveLeverage;

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 + priceMovePct)
                : entryPrice * (1 - priceMovePct);
        }

        private static bool CheckStopLoss(BacktestPosition position, decimal high, decimal low)
        {
            if (!position.StopLossPrice.HasValue)
            {
                return false;
            }

            return position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? low <= position.StopLossPrice.Value
                : high >= position.StopLossPrice.Value;
        }

        private static bool CheckTakeProfit(BacktestPosition position, decimal high, decimal low)
        {
            if (!position.TakeProfitPrice.HasValue)
            {
                return false;
            }

            return position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? high >= position.TakeProfitPrice.Value
                : low <= position.TakeProfitPrice.Value;
        }

        private static bool TryMapAction(
            string action,
            out string positionSide,
            out string orderSide,
            out bool isClose)
        {
            positionSide = string.Empty;
            orderSide = string.Empty;
            isClose = false;

            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            switch (action.Trim().ToUpperInvariant())
            {
                case "LONG":
                    positionSide = "Long";
                    orderSide = "buy";
                    isClose = false;
                    return true;
                case "SHORT":
                    positionSide = "Short";
                    orderSide = "sell";
                    isClose = false;
                    return true;
                case "CLOSELONG":
                    positionSide = "Long";
                    orderSide = "sell";
                    isClose = true;
                    return true;
                case "CLOSESHORT":
                    positionSide = "Short";
                    orderSide = "buy";
                    isClose = true;
                    return true;
                default:
                    return false;
            }
        }

        private static (bool Success, StringBuilder Message) BuildResult(string method, bool success, string message)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(method))
            {
                builder.Append(method).Append(": ");
            }

            builder.Append(message);
            return (success, builder);
        }
    }
}
