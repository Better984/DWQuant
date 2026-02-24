using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Text;

namespace ServerTest.Modules.StrategyEngine.Application
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
                StrategyVersionId = context.Strategy.VersionId,
                StrategyVersionNo = context.Strategy.Version,
                StrategyState = NormalizeStrategyState(context.Strategy.State),
                ExchangeApiKeyId = context.Strategy.ExchangeApiKeyId,
                Exchange = context.StrategyConfig?.Trade?.Exchange ?? string.Empty,
                Symbol = context.StrategyConfig?.Trade?.Symbol ?? string.Empty,
                TimeframeSec = context.StrategyConfig?.Trade?.TimeframeSec ?? 0,
                OrderQty = context.StrategyConfig?.Trade?.Sizing?.OrderQty ?? 0m,
                MaxPositionQty = context.StrategyConfig?.Trade?.Sizing?.MaxPositionQty ?? 0m,
                Leverage = context.StrategyConfig?.Trade?.Sizing?.Leverage ?? 1,
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

            // 使用 EnqueueAsync + WriteAsync，队列满时按 FullMode=Wait 等待，避免高压下丢单
            var enqueued = _queue.EnqueueAsync(task, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            if (!enqueued)
            {
                _logger.LogWarning("动作任务入队失败（已取消）: {Uid} 方法={Method}", context.Strategy.UidCode, method.Method);
                return BuildResult(method.Method ?? "Unknown", false, "Action task enqueue cancelled");
            }

            return BuildResult(method.Method ?? "Unknown", true, "Action task enqueued");
        }

        private static string NormalizeStrategyState(StrategyState state)
        {
            return state switch
            {
                StrategyState.Running => "running",
                StrategyState.Paused => "paused",
                StrategyState.PausedOpenPosition => "paused_open_position",
                StrategyState.PausedOpenFail => "paused_open_fail",
                StrategyState.Testing => "testing",
                StrategyState.Completed => "completed",
                StrategyState.Deleted => "deleted",
                _ => "draft"
            };
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
