using Microsoft.Extensions.Logging;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.Backtest.Infrastructure;
using ServerTest.Protocol;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 处理核心节点接收到的算力节点回传消息。
    /// </summary>
    public sealed class BacktestWorkerMessageService
    {
        private readonly BacktestTaskRepository _repository;
        private readonly BacktestProgressPushService _progressPushService;
        private readonly BacktestWorkerRegistry _registry;
        private readonly ILogger<BacktestWorkerMessageService> _logger;

        public BacktestWorkerMessageService(
            BacktestTaskRepository repository,
            BacktestProgressPushService progressPushService,
            BacktestWorkerRegistry registry,
            ILogger<BacktestWorkerMessageService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _progressPushService = progressPushService ?? throw new ArgumentNullException(nameof(progressPushService));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleProgressAsync(string workerId, BacktestWorkerProgressReport report, CancellationToken ct)
        {
            if (report == null || report.TaskId <= 0)
            {
                return;
            }

            try
            {
                await _repository.UpdateProgressAsync(
                    report.TaskId,
                    NormalizeProgress(report.Progress),
                    report.Stage,
                    report.StageName,
                    report.Message,
                    ct).ConfigureAwait(false);

                await _progressPushService.PublishMessageAsync(
                    new BacktestProgressContext
                    {
                        TaskId = report.TaskId,
                        UserId = report.UserId > 0 ? report.UserId : null,
                        ReqId = report.ReqId
                    },
                    new BacktestProgressMessage
                    {
                        EventKind = "stage",
                        Stage = report.Stage,
                        StageName = report.StageName,
                        Message = report.Message,
                        Progress = report.Progress,
                        ElapsedMs = report.ElapsedMs,
                        Completed = report.Completed
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "处理算力节点进度回传失败: workerId={WorkerId} taskId={TaskId}",
                    workerId,
                    report.TaskId);
            }
        }

        public async Task HandleResultAsync(string workerId, BacktestWorkerResultReport report, CancellationToken ct)
        {
            if (report == null || report.TaskId <= 0)
            {
                return;
            }

            try
            {
                if (report.Success)
                {
                    var resultJson = report.ResultJson;
                    if (string.IsNullOrWhiteSpace(resultJson) && report.Result != null)
                    {
                        resultJson = ProtocolJson.Serialize(report.Result);
                    }

                    if (string.IsNullOrWhiteSpace(resultJson))
                    {
                        await _repository.MarkFailedAsync(report.TaskId, "算力节点返回成功但结果为空", ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var totalBars = report.Result?.TotalBars ?? 0;
                        var tradeCount = report.Result?.TotalStats?.TradeCount ?? 0;
                        var durationMs = report.DurationMs > 0
                            ? report.DurationMs
                            : report.Result?.DurationMs ?? 0;

                        await _repository.MarkCompletedAsync(
                            report.TaskId,
                            resultJson,
                            totalBars,
                            tradeCount,
                            durationMs,
                            ct).ConfigureAwait(false);

                        await _progressPushService.PublishStageAsync(
                            new BacktestProgressContext
                            {
                                TaskId = report.TaskId,
                                UserId = report.UserId > 0 ? report.UserId : null,
                                ReqId = report.ReqId
                            },
                            "completed",
                            "回测完成",
                            "远端算力节点回测完成",
                            null,
                            null,
                            durationMs,
                            true,
                            ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    var error = string.IsNullOrWhiteSpace(report.ErrorMessage)
                        ? "远端算力节点执行失败"
                        : report.ErrorMessage!;
                    await _repository.MarkFailedAsync(report.TaskId, error, ct).ConfigureAwait(false);

                    await _progressPushService.PublishStageAsync(
                        new BacktestProgressContext
                        {
                            TaskId = report.TaskId,
                            UserId = report.UserId > 0 ? report.UserId : null,
                            ReqId = report.ReqId
                        },
                        "failed",
                        "回测失败",
                        error,
                        null,
                        null,
                        null,
                        true,
                        ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "处理算力节点结果回传失败: workerId={WorkerId} taskId={TaskId}",
                    workerId,
                    report.TaskId);
            }
            finally
            {
                _registry.CompleteTask(workerId, report.TaskId);
            }
        }

        public async Task RequeueLostTasksAsync(string workerId, IReadOnlyList<BacktestWorkerTaskLease> leases, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(workerId) || leases == null || leases.Count == 0)
            {
                return;
            }

            foreach (var lease in leases)
            {
                try
                {
                    await _repository.RequeueAsync(
                        lease.TaskId,
                        $"算力节点离线，任务回到队列: worker={workerId}",
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "算力节点离线回收任务失败: workerId={WorkerId} taskId={TaskId}",
                        workerId,
                        lease.TaskId);
                }
            }
        }

        private static decimal NormalizeProgress(decimal? progress)
        {
            if (!progress.HasValue)
            {
                return 0m;
            }

            if (progress.Value < 0m)
            {
                return 0m;
            }

            if (progress.Value > 1m)
            {
                return 1m;
            }

            return progress.Value;
        }
    }
}
