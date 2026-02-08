using ServerTest.Models;
using ServerTest.Models.Strategy;
using System.Text;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Modules.StrategyEngine.Domain
{
    public readonly record struct ConditionEvaluationResult(
        string Key,
        bool Success,
        string Message);

    public delegate (bool Success, StringBuilder Message) ExecuteAction(
        StrategyExecutionContext context,
        StrategyMethod method,
        IReadOnlyList<ConditionEvaluationResult> triggerResults);

    public interface IStrategyValueResolver
    {
        bool TryResolvePair(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            out double left,
            out double right);

        bool TryResolveValue(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int offset,
            out double value);

        bool TryResolveValues(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            int requiredCount,
            out double[] values);
    }

    public sealed class NoopStrategyValueResolver : IStrategyValueResolver
    {
        public static NoopStrategyValueResolver Instance { get; } = new();

        private NoopStrategyValueResolver()
        {
        }

        public bool TryResolvePair(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            out double left,
            out double right)
        {
            left = double.NaN;
            right = double.NaN;
            return false;
        }

        public bool TryResolveValue(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int offset,
            out double value)
        {
            value = double.NaN;
            return false;
        }

        public bool TryResolveValues(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            int requiredCount,
            out double[] values)
        {
            values = Array.Empty<double>();
            return false;
        }
    }

    public interface IStrategyActionExecutor
    {
        (bool Success, StringBuilder Message) Execute(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults);
    }

    public sealed class StrategyExecutionContext
    {
        private static readonly StrategyModel EmptyStrategy = new();

        internal StrategyExecutionContext()
        {
            Strategy = EmptyStrategy;
            Task = default;
            ValueResolver = NoopStrategyValueResolver.Instance;
            CurrentTime = DateTimeOffset.UnixEpoch;
        }

        public StrategyExecutionContext(
            StrategyModel strategy,
            MarketDataTask task,
            IStrategyValueResolver? valueResolver = null,
            IStrategyActionExecutor? actionExecutor = null,
            DateTimeOffset? currentTime = null)
        {
            Reset(strategy, task, valueResolver, actionExecutor, currentTime);
        }

        public StrategyModel Strategy { get; private set; } = EmptyStrategy;
        public StrategyConfig StrategyConfig => Strategy.StrategyConfig;
        public MarketDataTask Task { get; private set; } = default;
        public IStrategyValueResolver ValueResolver { get; private set; } = NoopStrategyValueResolver.Instance;
        public IStrategyActionExecutor? ActionExecutor { get; private set; }
        public DateTimeOffset CurrentTime { get; private set; } = DateTimeOffset.UnixEpoch;

        /// <summary>
        /// 重置执行上下文，供回测对象池复用。
        /// </summary>
        internal void Reset(
            StrategyModel strategy,
            MarketDataTask task,
            IStrategyValueResolver? valueResolver = null,
            IStrategyActionExecutor? actionExecutor = null,
            DateTimeOffset? currentTime = null)
        {
            Strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            Task = task;
            ValueResolver = valueResolver ?? NoopStrategyValueResolver.Instance;
            ActionExecutor = actionExecutor;
            CurrentTime = currentTime ?? DateTimeOffset.FromUnixTimeMilliseconds(task.CandleTimestamp);
        }

        /// <summary>
        /// 清理上下文引用，避免对象池中残留大对象。
        /// </summary>
        internal void Clear()
        {
            Strategy = EmptyStrategy;
            Task = default;
            ValueResolver = NoopStrategyValueResolver.Instance;
            ActionExecutor = null;
            CurrentTime = DateTimeOffset.UnixEpoch;
        }
    }
}
