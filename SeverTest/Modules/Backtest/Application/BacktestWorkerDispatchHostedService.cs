using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.Backtest.Infrastructure;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 核心节点任务分发器：将队列中的回测任务下发给在线算力节点。
    /// </summary>
    public sealed class BacktestWorkerDispatchHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly BacktestWorkerRegistry _registry;
        private readonly BacktestWorkerOptions _options;
        private readonly ServerRoleRuntime _roleRuntime;
        private readonly ILogger<BacktestWorkerDispatchHostedService> _logger;

        public BacktestWorkerDispatchHostedService(
            IServiceProvider serviceProvider,
            BacktestWorkerRegistry registry,
            IOptions<BacktestWorkerOptions> options,
            ServerRoleRuntime roleRuntime,
            ILogger<BacktestWorkerDispatchHostedService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options?.Value ?? new BacktestWorkerOptions();
            _roleRuntime = roleRuntime ?? throw new ArgumentNullException(nameof(roleRuntime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_roleRuntime.IsCoreLike)
            {
                _logger.LogInformation("当前角色非核心节点，跳过回测算力分发服务: role={Role}", _roleRuntime.Role);
                return;
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();
                await repository.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);
            }

            _logger.LogInformation("回测算力分发服务启动");

            var delayMs = Math.Max(100, _options.DispatchPollingIntervalMs);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "回测算力分发循环异常");
                }

                try
                {
                    await Task.Delay(delayMs, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task DispatchOnceAsync(CancellationToken ct)
        {
            var availableWorkers = _registry.GetAvailableSessions();
            if (availableWorkers.Count == 0)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();

            foreach (var worker in availableWorkers)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var task = await repository.TryAcquireQueuedTaskAsync(worker.WorkerId, ct).ConfigureAwait(false);
                if (task == null)
                {
                    return;
                }

                var lease = new BacktestWorkerTaskLease
                {
                    TaskId = task.TaskId,
                    UserId = task.UserId,
                    ReqId = task.ReqId,
                    AssignedAtUtc = DateTime.UtcNow,
                };

                if (!_registry.TryAssignTask(worker.WorkerId, lease))
                {
                    await repository.RequeueAsync(task.TaskId, "算力节点已满，任务重新排队", ct).ConfigureAwait(false);
                    continue;
                }

                var request = ProtocolJson.Deserialize<BacktestRunRequest>(task.RequestJson);
                if (request == null)
                {
                    _registry.CompleteTask(worker.WorkerId, task.TaskId);
                    await repository.MarkFailedAsync(task.TaskId, "任务参数反序列化失败", ct).ConfigureAwait(false);
                    continue;
                }

                var payload = new BacktestWorkerExecuteRequest
                {
                    TaskId = task.TaskId,
                    UserId = task.UserId,
                    ReqId = task.ReqId,
                    Request = request
                };

                var envelope = ProtocolEnvelopeFactory.Ok<object>(
                    BacktestWorkerMessageTypes.Execute,
                    task.ReqId,
                    payload,
                    "dispatch");

                var sent = await _registry.SendAsync(worker.WorkerId, envelope, ct).ConfigureAwait(false);
                if (!sent)
                {
                    _registry.CompleteTask(worker.WorkerId, task.TaskId);
                    await repository.RequeueAsync(task.TaskId, $"下发失败，任务重新排队 worker={worker.WorkerId}", ct).ConfigureAwait(false);
                    continue;
                }

                await repository.UpdateProgressAsync(
                    task.TaskId,
                    task.Progress,
                    "dispatched",
                    "任务已下发",
                    $"已分发到算力节点 {worker.WorkerId}",
                    ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "回测任务已分发: taskId={TaskId} workerId={WorkerId} userId={UserId}",
                    task.TaskId,
                    worker.WorkerId,
                    task.UserId);
            }
        }
    }
}
