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
                return BuildResult(method?.Method ?? "Unknown", false, "执行上下文无效");
            }

            var snapshots = triggerResults?
                .Select(result => new ConditionEvaluationSnapshot(result.Key, result.Success, result.Message))
                .ToList() ?? new List<ConditionEvaluationSnapshot>();

            if (!StrategyTradeTargetHelper.TryCreate(
                    context.Strategy.UidCode,
                    context.Strategy.CreatorUserId,
                    context.Strategy.Id,
                    context.Strategy.VersionId,
                    context.Strategy.Version,
                    NormalizeStrategyState(context.Strategy.State),
                    context.Strategy.ExchangeApiKeyId,
                    context.StrategyConfig?.Trade,
                    method,
                    context.Task,
                    snapshots,
                    context.CurrentTime,
                    out var target,
                    out var error))
            {
                return BuildResult(method.Method ?? "Unknown", false, error);
            }

            var task = new StrategyActionTask
            {
                StrategyUid = target.StrategyUid,
                Uid = target.Uid,
                UsId = target.UsId,
                StrategyVersionId = target.StrategyVersionId,
                StrategyVersionNo = target.StrategyVersionNo,
                StrategyState = target.StrategyState,
                ExchangeApiKeyId = target.ExchangeApiKeyId,
                Exchange = target.Exchange,
                Symbol = target.Symbol,
                TimeframeSec = target.TimeframeSec,
                OrderQty = target.RequestedQty,
                MaxPositionQty = target.MaxPositionQty,
                Leverage = target.Leverage,
                TakeProfitPct = target.TakeProfitPct,
                StopLossPct = target.StopLossPct,
                TrailingEnabled = target.TrailingEnabled,
                TrailingActivationPct = target.TrailingActivationPct,
                TrailingDrawdownPct = target.TrailingDrawdownPct,
                Stage = target.Stage,
                MarketTask = context.Task,
                Method = target.Method,
                Param = target.Param,
                Target = target,
                TriggerResults = snapshots
            };

            // 使用 EnqueueAsync + WriteAsync，队列满时按 FullMode=Wait 等待，避免高压下丢单
            var enqueued = _queue.EnqueueAsync(task, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            if (!enqueued)
            {
                _logger.LogWarning("动作任务入队失败（已取消）: {Uid} 方法={Method}", context.Strategy.UidCode, method.Method);
                return BuildResult(method.Method ?? "Unknown", false, "动作任务入队已取消");
            }

            return BuildResult(method.Method ?? "Unknown", true, "动作目标已入队");
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
