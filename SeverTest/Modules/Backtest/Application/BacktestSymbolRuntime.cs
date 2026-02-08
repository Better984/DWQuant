using System;
using System.Collections.Generic;
using ServerTest.Models.Indicator;
using ServerTest.Modules.StrategyEngine.Domain;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 单标的回测运行时上下文。
    /// </summary>
    internal sealed class BacktestSymbolRuntime
    {
        public BacktestSymbolRuntime(
            string symbol,
            StrategyModel strategy,
            StrategyRuntimeSchedule runtimeSchedule,
            BacktestActionExecutor.SymbolState state,
            List<IndicatorRequest> indicatorRequests,
            Domain.BacktestSymbolResult result,
            bool useRuntimeGate)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            Strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            RuntimeSchedule = runtimeSchedule ?? throw new ArgumentNullException(nameof(runtimeSchedule));
            State = state ?? throw new ArgumentNullException(nameof(state));
            IndicatorRequests = indicatorRequests ?? throw new ArgumentNullException(nameof(indicatorRequests));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            UseRuntimeGate = useRuntimeGate;
        }

        public string Symbol { get; }
        public StrategyModel Strategy { get; }
        public StrategyRuntimeSchedule RuntimeSchedule { get; }
        public BacktestActionExecutor.SymbolState State { get; }
        public List<IndicatorRequest> IndicatorRequests { get; }
        public Domain.BacktestSymbolResult Result { get; }
        public bool UseRuntimeGate { get; }
    }
}
