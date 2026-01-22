using ServerTest.Models.Strategy;
using System.Text;

namespace ServerTest.Strategy
{
    public static class ConditionMethodRegistry
    {
        private static readonly Dictionary<string, ExecuteAction> Methods = new(StringComparer.Ordinal)
        {
            { "GreaterThanOrEqual", GreaterThanOrEqual },   // 大于等于：A >= B
            { "LessThan",           LessThan },             // 小于：A < B
            { "LessThanOrEqual",    LessThanOrEqual },      // 小于等于：A <= B
            { "Equal",              Equal },                // 等于：A == B
            { "CrossOver",          CrossOver },            // 交叉：A 与 B 发生任意方向交叉
            { "CrossUp",            CrossOver }             // 上穿：A 从下向上穿越 B（当前实现与 CrossOver 共用）
        };

        public static ExecuteAction? Get(string methodId)
        {
            return !string.IsNullOrWhiteSpace(methodId) && Methods.TryGetValue(methodId, out var action)
                ? action
                : null;
        }

        public static (bool Success, StringBuilder Message) Run(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            var action = Get(method.Method);
            if (action == null)
            {
                return BuildResult(method.Method, false, "Unknown condition method");
            }

            return action(context, method, triggerResults);
        }

        private static (bool Success, StringBuilder Message) GreaterThanOrEqual(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, ">=", (left, right) => left >= right);
        }

        private static (bool Success, StringBuilder Message) LessThan(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "<", (left, right) => left < right);
        }

        private static (bool Success, StringBuilder Message) LessThanOrEqual(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "<=", (left, right) => left <= right);
        }

        private static (bool Success, StringBuilder Message) Equal(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "==", (left, right) => Math.Abs(left - right) < 1e-10);
        }

        private static (bool Success, StringBuilder Message) CrossOver(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "Value resolver not configured");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "Value resolver not configured for offset");
            }

            bool crossed = prevLeft <= prevRight && left > right;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" crossed above ")
                .Append(prevRight.ToString("F4"))
                .Append("->")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(crossed);
            return (crossed, message);
        }

        private static (bool Success, StringBuilder Message) CompareValues(
            StrategyExecutionContext context,
            StrategyMethod method,
            string op,
            Func<double, double, bool> comparison)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "Value resolver not configured");
            }

            bool success = comparison(left, right);
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(left.ToString("F4"))
                .Append(" ")
                .Append(op)
                .Append(" ")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(success);
            return (success, message);
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
