using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Shared.Application.Diagnostics;
using ServerTest.Modules.StrategyEngine.Domain;
using ServerTest.Options;
using ServerTest.Services;
using System.Text;
using System.Diagnostics;
using System.Text.Json;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class QueuedStrategyActionExecutor : IStrategyActionExecutor
    {
        private readonly StrategyActionTaskQueue _queue;
        private readonly ILogger<QueuedStrategyActionExecutor> _logger;
        private readonly StrategyDiagnosticsOptions _diagnostics;
        private readonly StrategyTaskTraceLogQueue? _taskTraceLogQueue;
        private readonly string _instanceId;

        public QueuedStrategyActionExecutor(
            StrategyActionTaskQueue queue,
            ILogger<QueuedStrategyActionExecutor> logger,
            StrategyTaskTraceLogQueue? taskTraceLogQueue = null,
            IOptions<StrategyDiagnosticsOptions>? diagnosticsOptions = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _taskTraceLogQueue = taskTraceLogQueue;
            _instanceId = ProcessInstanceIdProvider.InstanceId;
            _diagnostics = diagnosticsOptions?.Value ?? new StrategyDiagnosticsOptions();
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
                TraceId = Guid.NewGuid().ToString("N"),
                RootTraceId = MarketDataTask.NormalizeTraceId(context.Task.TraceId),
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
            var enqueueStopwatch = Stopwatch.StartNew();
            var enqueued = _queue.EnqueueAsync(task, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            enqueueStopwatch.Stop();
            var enqueueMs = (int)Math.Min(enqueueStopwatch.ElapsedMilliseconds, int.MaxValue);
            if (!enqueued)
            {
                _logger.LogWarning(
                    "动作任务入队失败（已取消）: uid={Uid} usId={UsId} 方法={Method} 入队耗时={Duration}毫秒",
                    context.Strategy.UidCode,
                    context.Strategy.Id,
                    method.Method,
                    enqueueMs);
                TraceActionEnqueue(task, method, enqueueMs, "fail", "Action task enqueue cancelled");
                return BuildResult(method.Method ?? "Unknown", false, "动作任务入队已取消");
            }

            LogActionEnqueueProfile(context, method, enqueueMs);
            TraceActionEnqueue(task, method, enqueueMs, "success", null);

            return BuildResult(method.Method ?? "Unknown", true, $"动作任务入队成功 taskTraceId={task.TraceId}");
        }

        private void LogActionEnqueueProfile(StrategyExecutionContext context, StrategyMethod method, int enqueueMs)
        {
            if (!_diagnostics.EnableActionEnqueueLog)
            {
                return;
            }

            var shouldLog = _diagnostics.LogEveryActionEnqueue ||
                            enqueueMs >= _diagnostics.SlowActionEnqueueThresholdMs;
            if (!shouldLog)
            {
                return;
            }

            if (enqueueMs >= _diagnostics.SlowActionEnqueueThresholdMs)
            {
                _logger.LogWarning(
                    "[动作入队画像] uid={Uid} usId={UsId} 方法={Method} 入队耗时={Duration}毫秒 阈值={Threshold}毫秒",
                    context.Strategy.UidCode,
                    context.Strategy.Id,
                    method.Method,
                    enqueueMs,
                    _diagnostics.SlowActionEnqueueThresholdMs);
                return;
            }

            _logger.LogInformation(
                "[动作入队画像] uid={Uid} usId={UsId} 方法={Method} 入队耗时={Duration}毫秒",
                context.Strategy.UidCode,
                context.Strategy.Id,
                method.Method,
                enqueueMs);
        }

        private void TraceActionEnqueue(
            StrategyActionTask task,
            StrategyMethod method,
            int enqueueMs,
            string status,
            string? errorMessage)
        {
            if (_taskTraceLogQueue == null || !_diagnostics.EnableTaskTracePersist)
            {
                return;
            }

            var shouldPersist = _diagnostics.TaskTraceLogEveryEvent
                || !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
                || enqueueMs >= _diagnostics.SlowTaskTraceThresholdMs;
            if (!shouldPersist)
            {
                return;
            }

            var payload = new
            {
                enqueueMs,
                thresholdMs = _diagnostics.SlowTaskTraceThresholdMs,
                triggerCount = task.TriggerResults?.Count ?? 0
            };

            _taskTraceLogQueue.TryEnqueue(new StrategyTaskTraceLog
            {
                TraceId = task.RootTraceId,
                ParentTraceId = task.TraceId,
                EventStage = "action.enqueue",
                EventStatus = status,
                ActorModule = nameof(QueuedStrategyActionExecutor),
                ActorInstance = _instanceId,
                Uid = task.Uid,
                UsId = task.UsId,
                StrategyUid = task.StrategyUid,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                Timeframe = task.TimeframeSec > 0 ? $"{task.TimeframeSec}s" : null,
                CandleTimestamp = task.MarketTask.CandleTimestamp,
                IsBarClose = task.MarketTask.IsBarClose,
                Method = method.Method,
                Flow = task.Param != null && task.Param.Length > 0 ? task.Param[0] : null,
                DurationMs = enqueueMs,
                MetricsJson = SerializeTracePayload(payload),
                ErrorMessage = errorMessage
            });
        }

        private static string? SerializeTracePayload(object payload)
        {
            try
            {
                return JsonSerializer.Serialize(payload);
            }
            catch
            {
                return null;
            }
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
