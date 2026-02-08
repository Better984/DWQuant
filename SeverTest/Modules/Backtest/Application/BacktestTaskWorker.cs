using System;
using System.Threading;
using System.Threading.Tasks;
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
    /// 回测任务后台消费者（HostedService）：
    /// - 轮询 backtest_task 表中 status=queued 的任务
    /// - 按 MaxConcurrentTasks 控制并发
    /// - 调用 BacktestService 执行回测
    /// - 更新任务状态和结果
    /// </summary>
    public sealed class BacktestTaskWorker : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

        private readonly IServiceProvider _serviceProvider;
        private readonly ServerConfigStore _configStore;
        private readonly ILogger<BacktestTaskWorker> _logger;
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
            _logger.LogInformation("回测任务Worker启动");

            // 等待系统启动完成
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
                _logger.LogError(ex, "回测任务Worker启动时建表失败，后续将继续重试执行");
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
                    _logger.LogError(ex, "回测任务Worker异常");
                    await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("回测任务Worker停止");
        }

        private async Task ProcessNextTaskAsync(CancellationToken ct)
        {
            var pollingInterval = _configStore.GetInt("Backtest:WorkerPollingIntervalMs", 500);
            var maxConcurrent = _configStore.GetInt("Backtest:MaxConcurrentTasks", 3);

            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<BacktestTaskRepository>();
            await TryCleanupAsync(repository, ct).ConfigureAwait(false);

            // 检查并发限制
            var running = await repository.CountRunningAsync(ct).ConfigureAwait(false);
            if (running >= maxConcurrent)
            {
                await Task.Delay(pollingInterval, ct).ConfigureAwait(false);
                return;
            }

            // 取出队首任务
            var task = await repository.DequeueAsync(ct).ConfigureAwait(false);
            if (task == null)
            {
                await Task.Delay(pollingInterval, ct).ConfigureAwait(false);
                return;
            }

            // 标记为执行中
            await repository.MarkRunningAsync(task.TaskId, ct).ConfigureAwait(false);
            await repository.UpdateProgressAsync(task.TaskId, 0m, "running", "正在执行", "任务已开始执行", ct).ConfigureAwait(false);

            _logger.LogInformation("回测任务开始执行: taskId={TaskId} userId={UserId} exchange={Exchange}",
                task.TaskId, task.UserId, task.Exchange);

            // 在新的 scope 中执行回测（BacktestRunner 是 Scoped）
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
                // 反序列化请求
                var request = JsonConvert.DeserializeObject<BacktestRunRequest>(task.RequestJson);
                if (request == null)
                {
                    await repository.MarkFailedAsync(task.TaskId, "请求参数反序列化失败").ConfigureAwait(false);
                    return;
                }

                // 执行回测
                var result = await backtestService
                    .RunAsync(request, task.ReqId, task.UserId, task.TaskId, cts.Token)
                    .ConfigureAwait(false);

                // 序列化结果并保存
                var resultJson = JsonConvert.SerializeObject(result);
                var tradeCount = result.TotalStats?.TradeCount ?? 0;

                await repository.MarkCompletedAsync(
                    task.TaskId,
                    resultJson,
                    result.TotalBars,
                    tradeCount,
                    result.DurationMs).ConfigureAwait(false);

                _logger.LogInformation(
                    "回测任务完成: taskId={TaskId} bars={Bars} trades={Trades} durationMs={Duration}",
                    task.TaskId, result.TotalBars, tradeCount, result.DurationMs);
            }
            catch (OperationCanceledException)
            {
                await repository.MarkFailedAsync(task.TaskId, "回测执行超时").ConfigureAwait(false);
                _logger.LogWarning("回测任务超时: taskId={TaskId}", task.TaskId);
            }
            catch (Exception ex)
            {
                await repository.MarkFailedAsync(task.TaskId, ex.Message).ConfigureAwait(false);
                _logger.LogError(ex, "回测任务失败: taskId={TaskId}", task.TaskId);
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
                _logger.LogInformation("回测任务过期清理完成: removed={Removed} retentionDays={RetentionDays}", removed, retentionDays);
            }
        }
    }
}
