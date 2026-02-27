using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ServerTest.Infrastructure.Config;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.Backtest.Infrastructure;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 本地回测任务处理器（HostedService）：
    /// - 轮询 backtest_task 中 queued 任务；
    /// - 受 Backtest:MaxConcurrentTasks 控制；
    /// - 本地执行回测并写回结果。
    /// </summary>
    public sealed class BacktestTaskWorker : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

        private readonly IServiceProvider _serviceProvider;
        private readonly ServerConfigStore _configStore;
        private readonly ILogger<BacktestTaskWorker> _logger;
        private readonly string _localWorkerId = $"local:{Environment.MachineName}:{Environment.ProcessId}";
        private DateTime _nextCleanupAtUtc = DateTime.MinValue;

        public BacktestTaskWorker(
            IServiceProvider serviceProvider,
            ServerConfigStore configStore,
            ILogger<BacktestTaskWorker> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("本地回测任务 Worker 启动");

            // 等待其他启动流程基本完成
            await Task.Delay(3000, stoppingToken).ConfigureAwait(false);
            try
            {
                await InitializeSchemaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "本地回测 Worker 初始化建表失败，后续将继续重试执行");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessNextTaskAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "本地回测 Worker 循环异常");
                    await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("本地回测任务 Worker 停止");
        }

        private async Task ProcessNextTaskAsync(CancellationToken ct)
        {
            var pollingInterval = _configStore.GetInt("Backtest:WorkerPollingIntervalMs", 500);
            var maxConcurrent = _configStore.GetInt("Backtest:MaxConcurrentTasks", 3);

            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();
            await TryCleanupAsync(repository, ct).ConfigureAwait(false);

            var running = await repository.CountRunningAsync(ct).ConfigureAwait(false);
            if (running >= maxConcurrent)
            {
                await Task.Delay(pollingInterval, ct).ConfigureAwait(false);
                return;
            }

            // 原子抢占队列任务，避免多实例重复消费
            var task = await repository.TryAcquireQueuedTaskAsync(_localWorkerId, ct).ConfigureAwait(false);
            if (task == null)
            {
                await Task.Delay(pollingInterval, ct).ConfigureAwait(false);
                return;
            }

            await repository.UpdateProgressAsync(task.TaskId, 0m, "running", "正在执行", "任务已由本地 Worker 接管", ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "本地回测任务开始执行: taskId={TaskId} userId={UserId} exchange={Exchange}",
                task.TaskId,
                task.UserId,
                task.Exchange);

            _ = Task.Run(async () =>
            {
                using var executeScope = _serviceProvider.CreateScope();
                await ExecuteTaskAsync(executeScope, task).ConfigureAwait(false);
            }, ct);
        }

        private async Task ExecuteTaskAsync(IServiceScope scope, BacktestTask task)
        {
            var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();
            var backtestService = scope.ServiceProvider.GetRequiredService<BacktestService>();
            var timeoutMinutes = _configStore.GetInt("Backtest:TaskTimeoutMinutes", 10);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));

            try
            {
                var request = JsonConvert.DeserializeObject<BacktestRunRequest>(task.RequestJson);
                if (request == null)
                {
                    await repository.MarkFailedAsync(task.TaskId, "请求参数反序列化失败").ConfigureAwait(false);
                    return;
                }

                var result = await backtestService
                    .RunAsync(request, task.ReqId, task.UserId, task.TaskId, cts.Token)
                    .ConfigureAwait(false);

                var resultJson = JsonConvert.SerializeObject(result);
                var tradeCount = result.TotalStats?.TradeCount ?? 0;

                await repository.MarkCompletedAsync(
                    task.TaskId,
                    resultJson,
                    result.TotalBars,
                    tradeCount,
                    result.DurationMs).ConfigureAwait(false);

                _logger.LogInformation(
                    "本地回测任务完成: taskId={TaskId} bars={Bars} trades={Trades} durationMs={Duration}",
                    task.TaskId,
                    result.TotalBars,
                    tradeCount,
                    result.DurationMs);
            }
            catch (OperationCanceledException)
            {
                await repository.MarkFailedAsync(task.TaskId, "回测执行超时").ConfigureAwait(false);
                _logger.LogWarning("本地回测任务超时: taskId={TaskId}", task.TaskId);
            }
            catch (Exception ex)
            {
                await repository.MarkFailedAsync(task.TaskId, ex.Message).ConfigureAwait(false);
                _logger.LogError(ex, "本地回测任务失败: taskId={TaskId}", task.TaskId);
            }
        }

        private async Task InitializeSchemaAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();
            await repository.EnsureSchemaAsync(ct).ConfigureAwait(false);
        }

        private async Task TryCleanupAsync(BacktestTaskRepository repository, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (now < _nextCleanupAtUtc)
            {
                return;
            }

            _nextCleanupAtUtc = now.Add(CleanupInterval);
            var retentionDays = _configStore.GetInt("Backtest:ResultRetentionDays", 30);
            if (retentionDays <= 0)
            {
                return;
            }

            var removed = await repository.CleanupExpiredAsync(retentionDays, ct).ConfigureAwait(false);
            if (removed > 0)
            {
                _logger.LogInformation("回测历史清理完成: removed={Removed} retentionDays={RetentionDays}", removed, retentionDays);
            }
        }
    }
}
