using System.Text;
using ServerTest.Models.Strategy;
using ServerTest.Strategy;

namespace ServerTest.Services
{
    public sealed class NoopStrategyActionExecutor : IStrategyActionExecutor
    {
        public (bool Success, StringBuilder Message) Execute(
            StrategyExecutionContext context,
            StrategyMethod method,
            System.Collections.Generic.IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append("Action executor not configured");
            return (false, message);
        }
    }
}
