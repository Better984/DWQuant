using System;
using System.Collections.Generic;
using System.Text;
using ServerTest.Models.Strategy;

namespace ServerTest.Strategy
{
    public static class ActionMethodRegistry
    {
        private static readonly Dictionary<string, ExecuteAction> Methods = new(StringComparer.Ordinal)
        {
            { "MakeTrade", MakeTrade }
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
                return BuildResult(method.Method, false, "Unknown action method");
            }

            return action(context, method, triggerResults);
        }

        private static (bool Success, StringBuilder Message) MakeTrade(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (context.ActionExecutor == null)
            {
                return BuildResult(method.Method, false, "Action executor not configured");
            }

            return context.ActionExecutor.Execute(context, method, triggerResults);
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
