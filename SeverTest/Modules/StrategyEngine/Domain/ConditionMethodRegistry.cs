using ServerTest.Models.Strategy;
using System.Text;

namespace ServerTest.Modules.StrategyEngine.Domain
{
    public static class ConditionMethodRegistry
    {
        private static readonly Dictionary<string, ExecuteAction> Methods = new(StringComparer.Ordinal)
        {
            { "GreaterThanOrEqual", GreaterThanOrEqual },   // 大于等于：A >= B
            { "GreaterThan",        GreaterThan },          // 大于：A > B
            { "LessThan",           LessThan },             // 小于：A < B
            { "LessThanOrEqual",    LessThanOrEqual },      // 小于等于：A <= B
            { "Equal",              Equal },                // 等于：A == B
            { "CrossUp",            CrossUp },              // 上穿：A 从下向上穿越 B
            { "CrossOver",          CrossUp },              // 上穿别名（兼容旧配置）
            { "CrossDown",          CrossDown },            // 下穿：A 从上向下穿越 B
            { "CrossAny",           CrossAny }              // 任意穿越：上穿或下穿都算
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
                return BuildResult(method.Method, false, "未知的条件检测方法");
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

        private static (bool Success, StringBuilder Message) GreaterThan(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, ">", (left, right) => left > right);
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

        private static (bool Success, StringBuilder Message) CrossUp(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "未配置偏移取值解析器");
            }

            bool crossed = prevLeft <= prevRight && left > right;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" 上穿 ")
                .Append(prevRight.ToString("F4"))
                .Append("->")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(crossed);
            return (crossed, message);
        }

        private static (bool Success, StringBuilder Message) CrossDown(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "未配置偏移取值解析器");
            }

            bool crossed = prevLeft >= prevRight && left < right;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" 下穿 ")
                .Append(prevRight.ToString("F4"))
                .Append("->")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(crossed);
            return (crossed, message);
        }

        private static (bool Success, StringBuilder Message) CrossAny(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "未配置偏移取值解析器");
            }

            bool crossed =
                (prevLeft <= prevRight && left > right) ||
                (prevLeft >= prevRight && left < right);
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" 任意穿越 ")
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
                return BuildResult(method.Method, false, "未配置取值解析器");
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
