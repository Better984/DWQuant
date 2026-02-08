using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Services;

namespace ServerTest.Modules.Backtest.Infrastructure
{
    /// <summary>
    /// 回测任务数据访问层（backtest_task 表）
    /// </summary>
    public sealed class BacktestTaskRepository
    {
        private readonly DatabaseService _db;
        private readonly ILogger<BacktestTaskRepository> _logger;

        public BacktestTaskRepository(DatabaseService db, ILogger<BacktestTaskRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 确保表存在
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS backtest_task (
  task_id BIGINT NOT NULL AUTO_INCREMENT,
  user_id BIGINT NOT NULL COMMENT '发起用户ID',
  req_id VARCHAR(64) NULL COMMENT '关联的协议 reqId',
  status VARCHAR(32) NOT NULL DEFAULT 'queued' COMMENT '任务状态: queued/running/completed/failed/cancelled',
  progress DECIMAL(5,4) NOT NULL DEFAULT 0 COMMENT '执行进度 [0,1]',
  stage VARCHAR(64) NULL COMMENT '当前阶段编码',
  stage_name VARCHAR(128) NULL COMMENT '当前阶段名称（前端展示）',
  message TEXT NULL COMMENT '阶段说明或错误信息',
  request_json LONGTEXT NOT NULL COMMENT '回测请求参数 JSON',
  result_json LONGTEXT NULL COMMENT '回测结果 JSON（完成后写入）',
  error_message TEXT NULL COMMENT '失败时的错误信息',
  exchange VARCHAR(32) NOT NULL DEFAULT '' COMMENT '交易所',
  timeframe VARCHAR(16) NOT NULL DEFAULT '' COMMENT '周期',
  symbols VARCHAR(512) NOT NULL DEFAULT '' COMMENT '标的列表（逗号分隔）',
  bar_count INT NOT NULL DEFAULT 0 COMMENT '回测 bar 数量',
  trade_count INT NOT NULL DEFAULT 0 COMMENT '交易次数',
  duration_ms BIGINT NOT NULL DEFAULT 0 COMMENT '回测执行耗时（毫秒）',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '任务创建时间',
  started_at DATETIME NULL COMMENT '开始执行时间',
  completed_at DATETIME NULL COMMENT '完成时间',
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最后更新时间',
  PRIMARY KEY (task_id),
  INDEX idx_user_status (user_id, status),
  INDEX idx_user_created (user_id, created_at DESC),
  INDEX idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='回测任务持久化表';";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 插入任务，返回 task_id
        /// </summary>
        public async Task<long> InsertAsync(BacktestTask task, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO backtest_task (user_id, req_id, status, request_json, exchange, timeframe, symbols, bar_count)
VALUES (@userId, @reqId, @status, @requestJson, @exchange, @timeframe, @symbols, @barCount);
SELECT LAST_INSERT_ID();";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", task.UserId);
            cmd.Parameters.AddWithValue("@reqId", (object?)task.ReqId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", task.Status);
            cmd.Parameters.AddWithValue("@requestJson", task.RequestJson);
            cmd.Parameters.AddWithValue("@exchange", task.Exchange);
            cmd.Parameters.AddWithValue("@timeframe", task.Timeframe);
            cmd.Parameters.AddWithValue("@symbols", task.Symbols);
            cmd.Parameters.AddWithValue("@barCount", task.BarCount);

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(result);
        }

        /// <summary>
        /// 获取单个任务（含 result_json）
        /// </summary>
        public async Task<BacktestTask?> GetByIdAsync(long taskId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT task_id, user_id, req_id, status, progress, stage, stage_name, message,
       request_json, result_json, error_message, exchange, timeframe, symbols,
       bar_count, trade_count, duration_ms, created_at, started_at, completed_at, updated_at
FROM backtest_task WHERE task_id = @taskId LIMIT 1";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskId", taskId);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;

            return ReadTask(reader);
        }

        /// <summary>
        /// 获取用户的任务列表（不含 result_json，降序）
        /// </summary>
        public async Task<List<BacktestTaskSummary>> ListByUserAsync(long userId, int limit, CancellationToken ct = default)
        {
            const string sql = @"
SELECT task_id, status, progress, stage, stage_name, message, error_message,
       exchange, timeframe, symbols, bar_count, trade_count, duration_ms,
       created_at, started_at, completed_at
FROM backtest_task WHERE user_id = @userId
ORDER BY created_at DESC LIMIT @limit";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@limit", Math.Max(1, Math.Min(limit, 100)));

            var list = new List<BacktestTaskSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(ReadSummary(reader));
            }

            return list;
        }

        /// <summary>
        /// 获取用户当前活跃任务数（queued + running）
        /// </summary>
        public async Task<int> CountActiveByUserAsync(long userId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT COUNT(*) FROM backtest_task
WHERE user_id = @userId AND status IN ('queued', 'running')";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        /// <summary>
        /// 获取全局活跃任务数
        /// </summary>
        public async Task<int> CountActiveGlobalAsync(CancellationToken ct = default)
        {
            const string sql = "SELECT COUNT(*) FROM backtest_task WHERE status IN ('queued', 'running')";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        /// <summary>
        /// 获取全局正在执行的任务数
        /// </summary>
        public async Task<int> CountRunningAsync(CancellationToken ct = default)
        {
            const string sql = "SELECT COUNT(*) FROM backtest_task WHERE status = 'running'";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        /// <summary>
        /// 获取队首任务（最早排队的）
        /// </summary>
        public async Task<BacktestTask?> DequeueAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT task_id, user_id, req_id, status, progress, stage, stage_name, message,
       request_json, result_json, error_message, exchange, timeframe, symbols,
       bar_count, trade_count, duration_ms, created_at, started_at, completed_at, updated_at
FROM backtest_task WHERE status = 'queued'
ORDER BY created_at ASC LIMIT 1";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;

            return ReadTask(reader);
        }

        /// <summary>
        /// 更新任务状态为 running
        /// </summary>
        public async Task MarkRunningAsync(long taskId, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE backtest_task SET status = 'running', started_at = NOW()
WHERE task_id = @taskId AND status = 'queued'";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 更新任务进度
        /// </summary>
        public async Task UpdateProgressAsync(
            long taskId, decimal progress, string? stage, string? stageName, string? message,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE backtest_task SET progress = @progress, stage = @stage,
       stage_name = @stageName, message = @message
WHERE task_id = @taskId";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@progress", progress);
            cmd.Parameters.AddWithValue("@stage", (object?)stage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@stageName", (object?)stageName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@message", (object?)message ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 标记任务完成
        /// </summary>
        public async Task MarkCompletedAsync(
            long taskId, string resultJson, int barCount, int tradeCount, long durationMs,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE backtest_task SET status = 'completed', progress = 1.0,
       stage = 'completed', stage_name = '回测完成',
       result_json = @resultJson, bar_count = @barCount,
       trade_count = @tradeCount, duration_ms = @durationMs,
       completed_at = NOW()
WHERE task_id = @taskId";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@resultJson", resultJson);
            cmd.Parameters.AddWithValue("@barCount", barCount);
            cmd.Parameters.AddWithValue("@tradeCount", tradeCount);
            cmd.Parameters.AddWithValue("@durationMs", durationMs);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 标记任务失败
        /// </summary>
        public async Task MarkFailedAsync(long taskId, string errorMessage, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE backtest_task SET status = 'failed', error_message = @errorMessage,
       stage = 'failed', stage_name = '回测失败', completed_at = NOW()
WHERE task_id = @taskId";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@errorMessage", errorMessage);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        public async Task<bool> CancelAsync(long taskId, long userId, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE backtest_task SET status = 'cancelled', completed_at = NOW(),
       stage = 'cancelled', stage_name = '已取消'
WHERE task_id = @taskId AND user_id = @userId AND status IN ('queued', 'running')";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@userId", userId);

            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
        }

        /// <summary>
        /// 清理过期任务结果
        /// </summary>
        public async Task<int> CleanupExpiredAsync(int retentionDays, CancellationToken ct = default)
        {
            const string sql = @"
DELETE FROM backtest_task
WHERE status IN ('completed', 'failed', 'cancelled')
  AND created_at < DATE_SUB(NOW(), INTERVAL @days DAY)";

            await using var conn = await _db.GetConnectionAsync().ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@days", Math.Max(1, retentionDays));

            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static BacktestTask ReadTask(MySqlDataReader reader)
        {
            return new BacktestTask
            {
                TaskId = reader.GetInt64(reader.GetOrdinal("task_id")),
                UserId = reader.GetInt64(reader.GetOrdinal("user_id")),
                ReqId = reader.IsDBNull(reader.GetOrdinal("req_id")) ? null : reader.GetString("req_id"),
                Status = reader.GetString("status"),
                Progress = reader.GetDecimal("progress"),
                Stage = reader.IsDBNull(reader.GetOrdinal("stage")) ? null : reader.GetString("stage"),
                StageName = reader.IsDBNull(reader.GetOrdinal("stage_name")) ? null : reader.GetString("stage_name"),
                Message = reader.IsDBNull(reader.GetOrdinal("message")) ? null : reader.GetString("message"),
                RequestJson = reader.GetString("request_json"),
                ResultJson = reader.IsDBNull(reader.GetOrdinal("result_json")) ? null : reader.GetString("result_json"),
                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString("error_message"),
                Exchange = reader.GetString("exchange"),
                Timeframe = reader.GetString("timeframe"),
                Symbols = reader.GetString("symbols"),
                BarCount = reader.GetInt32("bar_count"),
                TradeCount = reader.GetInt32("trade_count"),
                DurationMs = reader.GetInt64("duration_ms"),
                CreatedAt = reader.GetDateTime("created_at"),
                StartedAt = reader.IsDBNull(reader.GetOrdinal("started_at")) ? null : reader.GetDateTime("started_at"),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetDateTime("completed_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            };
        }

        private static BacktestTaskSummary ReadSummary(MySqlDataReader reader)
        {
            return new BacktestTaskSummary
            {
                TaskId = reader.GetInt64(reader.GetOrdinal("task_id")),
                Status = reader.GetString("status"),
                Progress = reader.GetDecimal("progress"),
                Stage = reader.IsDBNull(reader.GetOrdinal("stage")) ? null : reader.GetString("stage"),
                StageName = reader.IsDBNull(reader.GetOrdinal("stage_name")) ? null : reader.GetString("stage_name"),
                Message = reader.IsDBNull(reader.GetOrdinal("message")) ? null : reader.GetString("message"),
                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString("error_message"),
                Exchange = reader.GetString("exchange"),
                Timeframe = reader.GetString("timeframe"),
                Symbols = reader.GetString("symbols"),
                BarCount = reader.GetInt32("bar_count"),
                TradeCount = reader.GetInt32("trade_count"),
                DurationMs = reader.GetInt64("duration_ms"),
                CreatedAt = reader.GetDateTime("created_at"),
                StartedAt = reader.IsDBNull(reader.GetOrdinal("started_at")) ? null : reader.GetDateTime("started_at"),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetDateTime("completed_at")
            };
        }
    }
}
