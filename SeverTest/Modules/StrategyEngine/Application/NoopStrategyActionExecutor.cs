using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Text;

namespace ServerTest.Modules.StrategyEngine.Application
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
