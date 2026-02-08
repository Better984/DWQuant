using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ServerTest.Infrastructure.Config;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.Backtest.Infrastructure;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测任务服务：提交任务、查询状态、获取结果
    /// 负责用户限流与队列容量控制（配置通过 ServerConfigStore 实时生效）
    /// </summary>
    public sealed class BacktestTaskService
    {
        private readonly BacktestTaskRepository _repository;
        private readonly ServerConfigStore _configStore;
        private readonly ILogger<BacktestTaskService> _logger;

        public BacktestTaskService(
            BacktestTaskRepository repository,
            ServerConfigStore configStore,
            ILogger<BacktestTaskService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 提交回测任务到队列。
        /// 返回 task_id 和初始状态。
        /// 若超出限制则抛出 InvalidOperationException。
        /// </summary>
        public async Task<BacktestTaskSummary> SubmitAsync(
            BacktestRunRequest request,
            long userId,
            string? reqId,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // 兜底确保表结构存在，避免服务刚启动时建表竞争导致提交失败。
            await _repository.EnsureSchemaAsync(ct).ConfigureAwait(false);

            // 用户并发限制
            var maxPerUser = _configStore.GetInt("Backtest:MaxTasksPerUser", 2);
            var userActive = await _repository.CountActiveByUserAsync(userId, ct).ConfigureAwait(false);
            if (userActive >= maxPerUser)
                throw new InvalidOperationException($"当前有 {userActive} 个回测任务进行中，单用户最多 {maxPerUser} 个");

            // 全局队列容量限制
            var maxQueue = _configStore.GetInt("Backtest:MaxQueueSize", 20);
            var globalActive = await _repository.CountActiveGlobalAsync(ct).ConfigureAwait(false);
            if (globalActive >= maxQueue)
                throw new InvalidOperationException($"回测队列已满（{globalActive}/{maxQueue}），请稍后重试");

            // bar 数量限制
            var maxBarCount = _configStore.GetInt("Backtest:MaxBarCount", 500000);
            if (request.BarCount.HasValue && request.BarCount.Value > maxBarCount)
                throw new InvalidOperationException($"回测 K 线数量不能超过 {maxBarCount}");

            var symbols = request.Symbols != null && request.Symbols.Count > 0
                ? string.Join(",", request.Symbols)
                : string.Empty;

            var task = new BacktestTask
            {
                UserId = userId,
                ReqId = reqId,
                Status = BacktestTaskStatus.Queued,
                RequestJson = JsonConvert.SerializeObject(request),
                Exchange = request.Exchange ?? string.Empty,
                Timeframe = request.Timeframe ?? string.Empty,
                Symbols = symbols,
                BarCount = request.BarCount ?? 0
            };

            var taskId = await _repository.InsertAsync(task, ct).ConfigureAwait(false);

            _logger.LogInformation("回测任务已提交: taskId={TaskId} userId={UserId} exchange={Exchange} symbols={Symbols}",
                taskId, userId, task.Exchange, task.Symbols);

            return new BacktestTaskSummary
            {
                TaskId = taskId,
                Status = BacktestTaskStatus.Queued,
                Progress = 0,
                Exchange = task.Exchange,
                Timeframe = task.Timeframe,
                Symbols = task.Symbols,
                BarCount = task.BarCount,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 获取任务详情（含完整结果）
        /// </summary>
        public async Task<BacktestTask?> GetTaskAsync(long taskId, long userId, CancellationToken ct = default)
        {
            var task = await _repository.GetByIdAsync(taskId, ct).ConfigureAwait(false);
            if (task == null || task.UserId != userId)
                return null;

            return task;
        }

        /// <summary>
        /// 获取任务简要状态（不含 result_json，用于轮询）
        /// </summary>
        public async Task<BacktestTaskSummary?> GetTaskStatusAsync(long taskId, long userId, CancellationToken ct = default)
        {
            var task = await _repository.GetByIdAsync(taskId, ct).ConfigureAwait(false);
            if (task == null || task.UserId != userId)
                return null;

            return new BacktestTaskSummary
            {
                TaskId = task.TaskId,
                Status = task.Status,
                Progress = task.Progress,
                Stage = task.Stage,
                StageName = task.StageName,
                Message = task.Message,
                ErrorMessage = task.ErrorMessage,
                Exchange = task.Exchange,
                Timeframe = task.Timeframe,
                Symbols = task.Symbols,
                BarCount = task.BarCount,
                TradeCount = task.TradeCount,
                DurationMs = task.DurationMs,
                CreatedAt = task.CreatedAt,
                StartedAt = task.StartedAt,
                CompletedAt = task.CompletedAt
            };
        }

        /// <summary>
        /// 列出用户的回测任务
        /// </summary>
        public Task<List<BacktestTaskSummary>> ListTasksAsync(long userId, int limit, CancellationToken ct = default)
        {
            return _repository.ListByUserAsync(userId, limit, ct);
        }

        /// <summary>
        /// 列出用户当前活跃任务（queued/running）
        /// </summary>
        public async Task<List<BacktestTaskSummary>> ListActiveTasksAsync(long userId, int limit, CancellationToken ct = default)
        {
            var tasks = await _repository.ListByUserAsync(userId, Math.Max(limit, 20), ct).ConfigureAwait(false);
            return tasks
                .Where(t => string.Equals(t.Status, BacktestTaskStatus.Queued, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Status, BacktestTaskStatus.Running, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(1, Math.Min(limit, 100)))
                .ToList();
        }

        /// <summary>
        /// 取消回测任务
        /// </summary>
        public async Task<bool> CancelTaskAsync(long taskId, long userId, CancellationToken ct = default)
        {
            var cancelled = await _repository.CancelAsync(taskId, userId, ct).ConfigureAwait(false);
            if (cancelled)
            {
                _logger.LogInformation("回测任务已取消: taskId={TaskId} userId={UserId}", taskId, userId);
            }

            return cancelled;
        }
    }
}
