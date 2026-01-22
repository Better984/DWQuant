using ServerTest.Models;
using ServerTest.Models.Strategy;
using System.Text;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Strategy
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
        public StrategyExecutionContext(
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

        public StrategyModel Strategy { get; }
        public StrategyConfig StrategyConfig => Strategy.StrategyConfig;
        public MarketDataTask Task { get; }
        public IStrategyValueResolver ValueResolver { get; }
        public IStrategyActionExecutor? ActionExecutor { get; }
        public DateTimeOffset CurrentTime { get; }
    }
}
