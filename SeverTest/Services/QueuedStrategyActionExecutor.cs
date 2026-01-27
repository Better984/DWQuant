using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;
using ServerTest.Strategy;
using System.Text;

namespace ServerTest.Services
{
    public sealed class QueuedStrategyActionExecutor : IStrategyActionExecutor
    {
        private readonly StrategyActionTaskQueue _queue;
        private readonly ILogger<QueuedStrategyActionExecutor> _logger;

        public QueuedStrategyActionExecutor(
            StrategyActionTaskQueue queue,
            ILogger<QueuedStrategyActionExecutor> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public (bool Success, StringBuilder Message) Execute(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (context == null || method == null)
            {
                return BuildResult(method?.Method ?? "Unknown", false, "Invalid execution context");
            }

            var task = new StrategyActionTask
            {
                StrategyUid = context.Strategy.UidCode,
                Uid = context.Strategy.CreatorUserId,
                UsId = context.Strategy.Id,
                Exchange = context.StrategyConfig?.Trade?.Exchange ?? string.Empty,
                Symbol = context.StrategyConfig?.Trade?.Symbol ?? string.Empty,
                TimeframeSec = context.StrategyConfig?.Trade?.TimeframeSec ?? 0,
                OrderQty = context.StrategyConfig?.Trade?.Sizing?.OrderQty ?? 0m,
                TakeProfitPct = context.StrategyConfig?.Trade?.Risk?.TakeProfitPct,
                StopLossPct = context.StrategyConfig?.Trade?.Risk?.StopLossPct,
                TrailingEnabled = context.StrategyConfig?.Trade?.Risk?.Trailing?.Enabled ?? false,
                TrailingActivationPct = context.StrategyConfig?.Trade?.Risk?.Trailing?.ActivationProfitPct,
                TrailingDrawdownPct = context.StrategyConfig?.Trade?.Risk?.Trailing?.CloseOnDrawdownPct,
                Stage = method.Method ?? string.Empty,
                MarketTask = context.Task,
                Method = method.Method ?? string.Empty,
                Param = method.Param ?? Array.Empty<string>(),
                TriggerResults = triggerResults
                    .Select(result => new ConditionEvaluationSnapshot(result.Key, result.Success, result.Message))
                    .ToList()
            };

            if (!_queue.TryEnqueue(task))
            {
                _logger.LogWarning("Action task enqueue failed: {Uid} Method={Method}", context.Strategy.UidCode, method.Method);
                return BuildResult(method.Method ?? "Unknown", false, "Action task enqueue failed");
            }

            return BuildResult(method.Method ?? "Unknown", true, "Action task enqueued");
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
