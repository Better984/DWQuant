using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Models.Strategy;
using System.Globalization;
using System.Linq;

namespace ServerTest.Modules.StrategyEngine.Infrastructure
{
    public sealed class StrategyEngineRunLogRepository
    {
        private readonly IDbManager _db;
        private readonly ILogger<StrategyEngineRunLogRepository> _logger;

        public StrategyEngineRunLogRepository(
            IDbManager db,
            ILogger<StrategyEngineRunLogRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 确保策略任务主记录表存在，并补齐关键字段与索引。
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string createSql = @"
CREATE TABLE IF NOT EXISTS strategy_engine_run_log (
  run_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  run_at DATETIME(3) NOT NULL COMMENT '任务执行时间',
  trace_id VARCHAR(64) NULL COMMENT '任务追踪ID（一个任务一条）',
  run_status VARCHAR(32) NOT NULL DEFAULT 'success' COMMENT '任务状态: success/fail/skip_*',
  exchange VARCHAR(32) NOT NULL COMMENT '交易所',
  symbol VARCHAR(32) NOT NULL COMMENT '交易对',
  timeframe VARCHAR(16) NOT NULL COMMENT '周期',
  candle_timestamp BIGINT NOT NULL COMMENT 'K线时间戳(毫秒)',
  is_bar_close TINYINT(1) NOT NULL COMMENT '是否收线',
  duration_ms INT NOT NULL COMMENT '任务总耗时(毫秒)',
  lookup_ms INT NOT NULL DEFAULT 0 COMMENT '查找阶段耗时',
  indicator_ms INT NOT NULL DEFAULT 0 COMMENT '指标阶段耗时',
  execute_ms INT NOT NULL DEFAULT 0 COMMENT '执行阶段耗时',
  runnable_strategy_count INT NOT NULL DEFAULT 0 COMMENT '可运行策略数',
  state_skipped_count INT NOT NULL DEFAULT 0 COMMENT '状态跳过数',
  runtime_gate_skipped_count INT NOT NULL DEFAULT 0 COMMENT '时间门禁跳过数',
  indicator_request_count INT NOT NULL DEFAULT 0 COMMENT '指标请求数',
  indicator_success_count INT NOT NULL DEFAULT 0 COMMENT '指标成功数',
  indicator_total_count INT NOT NULL DEFAULT 0 COMMENT '指标总数',
  matched_count INT NOT NULL COMMENT '匹配策略数',
  executed_count INT NOT NULL COMMENT '执行策略数',
  skipped_count INT NOT NULL COMMENT '跳过策略数',
  condition_eval_count INT NOT NULL COMMENT '条件评估次数',
  action_exec_count INT NOT NULL COMMENT '动作执行次数',
  open_task_count INT NOT NULL COMMENT '开仓任务数',
  executed_strategy_ids LONGTEXT NULL COMMENT '执行策略ID列表（|分隔）',
  open_task_strategy_ids LONGTEXT NULL COMMENT '开仓策略ID列表（|分隔）',
  open_task_trace_ids LONGTEXT NULL COMMENT '开仓动作任务ID列表（|分隔）',
  open_order_ids LONGTEXT NULL COMMENT '开仓订单ID列表（|分隔）',
  extra_json JSON NULL COMMENT '扩展画像JSON',
  engine_instance VARCHAR(128) NULL COMMENT '处理实例（机器:进程:实例ID）',
  PRIMARY KEY (run_id),
  INDEX idx_run_at (run_at),
  INDEX idx_symbol_tf (exchange, symbol, timeframe, candle_timestamp),
  INDEX idx_engine_market_run (engine_instance, exchange, symbol, timeframe, run_at DESC),
  INDEX idx_trace_id (trace_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='策略引擎任务主记录（单任务单行）';";

            await _db.ExecuteAsync(createSql, null, null, ct).ConfigureAwait(false);

            await EnsureColumnAsync("trace_id", "ALTER TABLE strategy_engine_run_log ADD COLUMN trace_id VARCHAR(64) NULL AFTER run_at;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("run_status", "ALTER TABLE strategy_engine_run_log ADD COLUMN run_status VARCHAR(32) NOT NULL DEFAULT 'success' AFTER trace_id;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("lookup_ms", "ALTER TABLE strategy_engine_run_log ADD COLUMN lookup_ms INT NOT NULL DEFAULT 0 AFTER duration_ms;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("indicator_ms", "ALTER TABLE strategy_engine_run_log ADD COLUMN indicator_ms INT NOT NULL DEFAULT 0 AFTER lookup_ms;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("execute_ms", "ALTER TABLE strategy_engine_run_log ADD COLUMN execute_ms INT NOT NULL DEFAULT 0 AFTER indicator_ms;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("runnable_strategy_count", "ALTER TABLE strategy_engine_run_log ADD COLUMN runnable_strategy_count INT NOT NULL DEFAULT 0 AFTER execute_ms;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("state_skipped_count", "ALTER TABLE strategy_engine_run_log ADD COLUMN state_skipped_count INT NOT NULL DEFAULT 0 AFTER runnable_strategy_count;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("runtime_gate_skipped_count", "ALTER TABLE strategy_engine_run_log ADD COLUMN runtime_gate_skipped_count INT NOT NULL DEFAULT 0 AFTER state_skipped_count;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("indicator_request_count", "ALTER TABLE strategy_engine_run_log ADD COLUMN indicator_request_count INT NOT NULL DEFAULT 0 AFTER runtime_gate_skipped_count;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("indicator_success_count", "ALTER TABLE strategy_engine_run_log ADD COLUMN indicator_success_count INT NOT NULL DEFAULT 0 AFTER indicator_request_count;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("indicator_total_count", "ALTER TABLE strategy_engine_run_log ADD COLUMN indicator_total_count INT NOT NULL DEFAULT 0 AFTER indicator_success_count;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("open_task_trace_ids", "ALTER TABLE strategy_engine_run_log ADD COLUMN open_task_trace_ids LONGTEXT NULL AFTER open_task_strategy_ids;", ct).ConfigureAwait(false);
            await EnsureColumnAsync("open_order_ids", "ALTER TABLE strategy_engine_run_log ADD COLUMN open_order_ids LONGTEXT NULL AFTER open_task_trace_ids;", ct).ConfigureAwait(false);

            await EnsureIndexAsync("idx_run_at", "ALTER TABLE strategy_engine_run_log ADD INDEX idx_run_at (run_at);", ct).ConfigureAwait(false);
            await EnsureIndexAsync("idx_symbol_tf", "ALTER TABLE strategy_engine_run_log ADD INDEX idx_symbol_tf (exchange, symbol, timeframe, candle_timestamp);", ct).ConfigureAwait(false);
            await EnsureIndexAsync("idx_engine_market_run", "ALTER TABLE strategy_engine_run_log ADD INDEX idx_engine_market_run (engine_instance, exchange, symbol, timeframe, run_at DESC);", ct).ConfigureAwait(false);
            await EnsureIndexAsync("idx_trace_id", "ALTER TABLE strategy_engine_run_log ADD INDEX idx_trace_id (trace_id);", ct).ConfigureAwait(false);
        }

        public Task<int> InsertAsync(StrategyEngineRunLog log, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO strategy_engine_run_log
(
    run_at,
    trace_id,
    run_status,
    exchange,
    symbol,
    timeframe,
    candle_timestamp,
    is_bar_close,
    duration_ms,
    lookup_ms,
    indicator_ms,
    execute_ms,
    runnable_strategy_count,
    state_skipped_count,
    runtime_gate_skipped_count,
    indicator_request_count,
    indicator_success_count,
    indicator_total_count,
    matched_count,
    executed_count,
    skipped_count,
    condition_eval_count,
    action_exec_count,
    open_task_count,
    executed_strategy_ids,
    open_task_strategy_ids,
    open_task_trace_ids,
    open_order_ids,
    extra_json,
    engine_instance
)
VALUES
(
    @RunAt,
    @TraceId,
    @RunStatus,
    @Exchange,
    @Symbol,
    @Timeframe,
    @CandleTimestamp,
    @IsBarClose,
    @DurationMs,
    @LookupMs,
    @IndicatorMs,
    @ExecuteMs,
    @RunnableStrategyCount,
    @StateSkippedCount,
    @RuntimeGateSkippedCount,
    @IndicatorRequestCount,
    @IndicatorSuccessCount,
    @IndicatorTotalCount,
    @MatchedCount,
    @ExecutedCount,
    @SkippedCount,
    @ConditionEvalCount,
    @ActionExecCount,
    @OpenTaskCount,
    @ExecutedStrategyIds,
    @OpenTaskStrategyIds,
    @OpenTaskTraceIds,
    @OpenOrderIds,
    @ExtraJson,
    @EngineInstance
);";

            return _db.ExecuteAsync(sql, log, null, ct);
        }

        /// <summary>
        /// 开仓成功后按 trace_id 追加订单ID，避免再次扫描大表链路日志。
        /// </summary>
        public async Task<int> AppendOpenOrderIdAsync(string traceId, string? orderId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(traceId) || string.IsNullOrWhiteSpace(orderId))
            {
                return 0;
            }

            const string sql = @"
UPDATE strategy_engine_run_log
SET open_order_ids = CASE
    WHEN open_order_ids IS NULL OR open_order_ids = '' THEN @orderId
    WHEN FIND_IN_SET(@orderId, REPLACE(open_order_ids, '|', ',')) > 0 THEN open_order_ids
    ELSE CONCAT(open_order_ids, '|', @orderId)
END
WHERE run_id = (
    SELECT run_id FROM (
        SELECT run_id
        FROM strategy_engine_run_log
        WHERE trace_id = @traceId
        ORDER BY run_at DESC, run_id DESC
        LIMIT 1
    ) t
);";

            return await _db.ExecuteAsync(sql, new { traceId, orderId }, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 按策略实例读取最近运行画像（用于管理端策略详情弹窗）。
        /// </summary>
        public async Task<List<StrategyRunMetricSnapshotItem>> ListRecentByStrategyAsync(
            string actorInstancePrefix,
            long usId,
            int limit = 5,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(actorInstancePrefix) || usId <= 0)
            {
                return new List<StrategyRunMetricSnapshotItem>();
            }

            limit = Math.Min(Math.Max(1, limit), 20);
            var pattern = actorInstancePrefix.TrimEnd(':') + ":%";
            var strategyId = usId.ToString(CultureInfo.InvariantCulture);

            const string sql = @"
SELECT
  run_at AS RunAt,
  trace_id AS TraceId,
  run_status AS RunStatus,
  exchange AS Exchange,
  symbol AS Symbol,
  timeframe AS Timeframe,
  candle_timestamp AS CandleTimestamp,
  is_bar_close AS IsBarClose,
  duration_ms AS DurationMs,
  lookup_ms AS LookupMs,
  indicator_ms AS IndicatorMs,
  execute_ms AS ExecuteMs,
  runnable_strategy_count AS RunnableStrategyCount,
  state_skipped_count AS StateSkippedCount,
  runtime_gate_skipped_count AS RuntimeGateSkippedCount,
  indicator_request_count AS IndicatorRequestCount,
  indicator_success_count AS IndicatorSuccessCount,
  indicator_total_count AS IndicatorTotalCount,
  matched_count AS MatchedCount,
  executed_count AS ExecutedCount,
  skipped_count AS SkippedCount,
  condition_eval_count AS ConditionEvalCount,
  action_exec_count AS ActionExecCount,
  open_task_count AS OpenTaskCount,
  executed_strategy_ids AS ExecutedStrategyIds,
  open_task_strategy_ids AS OpenTaskStrategyIds,
  open_task_trace_ids AS OpenTaskTraceIds,
  open_order_ids AS OpenOrderIds,
  engine_instance AS EngineInstance,
  CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(extra_json, '$.strategy.perStrategySamples')), '0') AS SIGNED) AS PerStrategySamples,
  CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(extra_json, '$.strategy.perStrategyAvgMs')), '0') AS SIGNED) AS PerStrategyAvgMs,
  CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(extra_json, '$.strategy.perStrategyMaxMs')), '0') AS SIGNED) AS PerStrategyMaxMs
FROM strategy_engine_run_log
WHERE engine_instance LIKE @pattern
  AND (
    FIND_IN_SET(@strategyId, REPLACE(COALESCE(executed_strategy_ids, ''), '|', ',')) > 0
    OR FIND_IN_SET(@strategyId, REPLACE(COALESCE(open_task_strategy_ids, ''), '|', ',')) > 0
  )
ORDER BY run_at DESC
LIMIT @limit;";

            var rows = (await _db.QueryAsync<StrategyRunMetricSnapshotItem>(
                    sql,
                    new { pattern, strategyId, limit },
                    null,
                    ct)
                .ConfigureAwait(false)).ToList();

            foreach (var row in rows)
            {
                row.SuccessRatePct = row.MatchedCount > 0
                    ? Math.Round(row.ExecutedCount * 100m / row.MatchedCount, 2)
                    : 0m;
                row.OpenTaskRatePct = row.ExecutedCount > 0
                    ? Math.Round(row.OpenTaskCount * 100m / row.ExecutedCount, 2)
                    : 0m;
                if (string.IsNullOrWhiteSpace(row.RunStatus))
                {
                    row.RunStatus = ResolveRunStatus(row.ExecutedCount);
                }
            }

            return rows;
        }

        /// <summary>
        /// 基于 strategy_engine_run_log 生成市场任务执行报告（主记录模式）。
        /// </summary>
        public async Task<StrategyRunMarketSummary?> GetMarketTaskSummaryAsync(
            string actorInstancePrefix,
            string exchange,
            string symbol,
            string timeframe,
            int limitTasks = 500,
            int recentTaskLimit = 5,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(actorInstancePrefix))
            {
                return null;
            }

            limitTasks = Math.Min(Math.Max(1, limitTasks), 2000);
            recentTaskLimit = Math.Min(Math.Max(1, recentTaskLimit), 20);
            var pattern = actorInstancePrefix.TrimEnd(':') + ":%";

            const string summarySql = @"
SELECT
  COUNT(*) AS TaskCount,
  ROUND(AVG(duration_ms), 0) AS AvgDurationMs,
  SUM(CASE WHEN COALESCE(run_status, '') = 'success' THEN 1 ELSE 0 END) AS SuccessCount
FROM (
  SELECT duration_ms, run_status
  FROM strategy_engine_run_log
  WHERE engine_instance LIKE @pattern
    AND exchange = @exchange
    AND symbol = @symbol
    AND timeframe = @timeframe
  ORDER BY run_at DESC
  LIMIT @limitTasks
) recent;";

            var summary = await _db.QuerySingleOrDefaultAsync<MarketSummaryRow>(
                    summarySql,
                    new { pattern, exchange, symbol, timeframe, limitTasks },
                    null,
                    ct)
                .ConfigureAwait(false);

            if (summary == null)
            {
                return null;
            }

            var result = new StrategyRunMarketSummary
            {
                Exchange = exchange,
                Symbol = symbol,
                Timeframe = timeframe,
                TaskCount = summary.TaskCount,
                AvgDurationMs = summary.AvgDurationMs,
                SuccessRatePct = summary.TaskCount > 0
                    ? Math.Round(summary.SuccessCount * 100m / summary.TaskCount, 2)
                    : 100m
            };

            if (summary.TaskCount <= 0)
            {
                return result;
            }

            const string stageSql = @"
SELECT EventStage, TraceCount, AvgDurationMs
FROM (
    SELECT 'strategy.lookup' AS EventStage, COUNT(*) AS TraceCount, ROUND(AVG(lookup_ms), 0) AS AvgDurationMs
    FROM strategy_engine_run_log
    WHERE engine_instance LIKE @pattern
      AND exchange = @exchange
      AND symbol = @symbol
      AND timeframe = @timeframe
    UNION ALL
    SELECT 'strategy.indicator' AS EventStage, COUNT(*) AS TraceCount, ROUND(AVG(indicator_ms), 0) AS AvgDurationMs
    FROM strategy_engine_run_log
    WHERE engine_instance LIKE @pattern
      AND exchange = @exchange
      AND symbol = @symbol
      AND timeframe = @timeframe
    UNION ALL
    SELECT 'strategy.execute' AS EventStage, COUNT(*) AS TraceCount, ROUND(AVG(execute_ms), 0) AS AvgDurationMs
    FROM strategy_engine_run_log
    WHERE engine_instance LIKE @pattern
      AND exchange = @exchange
      AND symbol = @symbol
      AND timeframe = @timeframe
) x
ORDER BY EventStage;";

            var stageStats = (await _db.QueryAsync<StrategyRunStageStat>(
                    stageSql,
                    new { pattern, exchange, symbol, timeframe },
                    null,
                    ct)
                .ConfigureAwait(false)).ToList();
            result.StageStats = stageStats;

            const string recentTaskSql = @"
SELECT
  run_at AS RunAt,
  trace_id AS TraceId,
  run_status AS RunStatus,
  exchange AS Exchange,
  symbol AS Symbol,
  timeframe AS Timeframe,
  candle_timestamp AS CandleTimestamp,
  is_bar_close AS IsBarClose,
  duration_ms AS DurationMs,
  lookup_ms AS LookupMs,
  indicator_ms AS IndicatorMs,
  execute_ms AS ExecuteMs,
  matched_count AS MatchedCount,
  runnable_strategy_count AS RunnableStrategyCount,
  executed_count AS ExecutedCount,
  skipped_count AS SkippedCount,
  condition_eval_count AS ConditionEvalCount,
  action_exec_count AS ActionExecCount,
  open_task_count AS OpenTaskCount,
  state_skipped_count AS StateSkippedCount,
  runtime_gate_skipped_count AS RuntimeGateSkippedCount,
  indicator_request_count AS IndicatorRequestCount,
  indicator_success_count AS IndicatorSuccessCount,
  indicator_total_count AS IndicatorTotalCount,
  executed_strategy_ids AS ExecutedStrategyIds,
  open_task_strategy_ids AS OpenTaskStrategyIds,
  open_task_trace_ids AS OpenTaskTraceIds,
  open_order_ids AS OpenOrderIds,
  engine_instance AS EngineInstance,
  CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(extra_json, '$.strategy.perStrategySamples')), '0') AS SIGNED) AS PerStrategySamples,
  CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(extra_json, '$.strategy.perStrategyAvgMs')), '0') AS SIGNED) AS PerStrategyAvgMs,
  CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(extra_json, '$.strategy.perStrategyMaxMs')), '0') AS SIGNED) AS PerStrategyMaxMs
FROM strategy_engine_run_log
WHERE engine_instance LIKE @pattern
  AND exchange = @exchange
  AND symbol = @symbol
  AND timeframe = @timeframe
ORDER BY run_at DESC
LIMIT @recentTaskLimit;";

            var recentTasks = (await _db.QueryAsync<StrategyRunTaskSample>(
                    recentTaskSql,
                    new { pattern, exchange, symbol, timeframe, recentTaskLimit },
                    null,
                    ct)
                .ConfigureAwait(false)).ToList();
            foreach (var task in recentTasks)
            {
                task.RunStatus = string.IsNullOrWhiteSpace(task.RunStatus)
                    ? ResolveRunStatus(task.ExecutedCount)
                    : task.RunStatus;
                task.SuccessRatePct = task.MatchedCount > 0
                    ? Math.Round(task.ExecutedCount * 100m / task.MatchedCount, 2)
                    : 0m;
                task.OpenTaskRatePct = task.ExecutedCount > 0
                    ? Math.Round(task.OpenTaskCount * 100m / task.ExecutedCount, 2)
                    : 0m;
            }

            result.RecentTasks = recentTasks;
            result.RecentOrders = BuildRecentOrdersFromTasks(recentTasks, 20);
            return result;
        }

        /// <summary>
        /// 按服务器实例前缀聚合市场布局（主记录模式）。
        /// </summary>
        public async Task<List<StrategyRunLayoutItem>> ListLayoutByActorInstancePrefixAsync(
            string actorInstancePrefix,
            int limit = 200,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(actorInstancePrefix))
            {
                return new List<StrategyRunLayoutItem>();
            }

            limit = Math.Min(Math.Max(1, limit), 500);
            var scanLimit = Math.Min(Math.Max(limit * 200, 200), 20000);
            var pattern = actorInstancePrefix.TrimEnd(':') + ":%";

            const string sql = @"
SELECT
  exchange AS Exchange,
  symbol AS Symbol,
  timeframe AS Timeframe,
  MAX(runnable_strategy_count) AS StrategyCount,
  COUNT(*) AS TaskCount,
  COALESCE(ROUND(SUM(duration_ms), 0), 0) AS TotalDurationMs,
  MAX(run_at) AS LastExecutedAt
FROM (
  SELECT exchange, symbol, timeframe, runnable_strategy_count, duration_ms, run_at
  FROM strategy_engine_run_log
  WHERE engine_instance LIKE @pattern
  ORDER BY run_at DESC
  LIMIT @scanLimit
) recent
GROUP BY exchange, symbol, timeframe
ORDER BY LastExecutedAt DESC
LIMIT @limit;";

            return (await _db.QueryAsync<StrategyRunLayoutItem>(
                    sql,
                    new { pattern, scanLimit, limit },
                    null,
                    ct)
                .ConfigureAwait(false)).ToList();
        }

        private async Task EnsureColumnAsync(string columnName, string alterSql, CancellationToken ct)
        {
            const string checkSql = @"
SELECT COUNT(*) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'strategy_engine_run_log'
  AND COLUMN_NAME = @columnName;";

            var exists = await _db.ExecuteScalarAsync<int>(checkSql, new { columnName }, null, ct).ConfigureAwait(false);
            if (exists > 0)
            {
                return;
            }

            await _db.ExecuteAsync(alterSql, null, null, ct).ConfigureAwait(false);
            _logger.LogInformation("strategy_engine_run_log 已补充字段: {ColumnName}", columnName);
        }

        private async Task EnsureIndexAsync(string indexName, string alterSql, CancellationToken ct)
        {
            const string checkSql = @"
SELECT COUNT(*) FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'strategy_engine_run_log'
  AND INDEX_NAME = @indexName;";

            var exists = await _db.ExecuteScalarAsync<int>(checkSql, new { indexName }, null, ct).ConfigureAwait(false);
            if (exists > 0)
            {
                return;
            }

            await _db.ExecuteAsync(alterSql, null, null, ct).ConfigureAwait(false);
            _logger.LogInformation("strategy_engine_run_log 已补充索引: {IndexName}", indexName);
        }

        private static string ResolveRunStatus(int executedCount)
        {
            return executedCount > 0 ? "success" : "skip_no_execute";
        }

        private static List<StrategyRunOrderSample> BuildRecentOrdersFromTasks(
            IReadOnlyCollection<StrategyRunTaskSample> tasks,
            int limit)
        {
            var result = new List<StrategyRunOrderSample>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (tasks == null || tasks.Count == 0 || limit <= 0)
            {
                return result;
            }

            foreach (var task in tasks)
            {
                if (string.IsNullOrWhiteSpace(task.OpenOrderIds))
                {
                    continue;
                }

                var usId = ParseFirstLong(task.OpenTaskStrategyIds);
                foreach (var orderId in SplitPipeValues(task.OpenOrderIds))
                {
                    if (string.IsNullOrWhiteSpace(orderId))
                    {
                        continue;
                    }

                    if (!seen.Add(orderId))
                    {
                        continue;
                    }

                    result.Add(new StrategyRunOrderSample
                    {
                        OrderId = orderId,
                        UsId = usId,
                        Uid = null,
                        PositionSide = null,
                        Qty = null,
                        AveragePrice = null,
                        CreatedAt = task.RunAt
                    });

                    if (result.Count >= limit)
                    {
                        return result;
                    }
                }
            }

            return result;
        }

        private static long? ParseFirstLong(string? ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
            {
                return null;
            }

            foreach (var value in SplitPipeValues(ids))
            {
                if (long.TryParse(value, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static IEnumerable<string> SplitPipeValues(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part;
                }
            }
        }

        private sealed class MarketSummaryRow
        {
            public int TaskCount { get; set; }
            public int AvgDurationMs { get; set; }
            public int SuccessCount { get; set; }
        }
    }

    /// <summary>
    /// 市场任务执行报告（主记录模式）。
    /// </summary>
    public sealed class StrategyRunMarketSummary
    {
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public int TaskCount { get; set; }
        public int AvgDurationMs { get; set; }
        public decimal SuccessRatePct { get; set; }
        public List<StrategyRunStageStat> StageStats { get; set; } = new();
        public List<StrategyRunOrderSample> RecentOrders { get; set; } = new();
        public List<StrategyRunTaskSample> RecentTasks { get; set; } = new();
    }

    /// <summary>
    /// 任务阶段统计（主记录分段字段汇总）。
    /// </summary>
    public sealed class StrategyRunStageStat
    {
        public string EventStage { get; set; } = string.Empty;
        public int TraceCount { get; set; }
        public int AvgDurationMs { get; set; }
    }

    /// <summary>
    /// 市场布局项（按交易所/币种/周期聚合）。
    /// </summary>
    public sealed class StrategyRunLayoutItem
    {
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public int StrategyCount { get; set; }
        public int TaskCount { get; set; }
        public int TotalDurationMs { get; set; }
        public DateTime LastExecutedAt { get; set; }
    }

    /// <summary>
    /// 最近任务样本（用于市场详情“最近5次任务”列表）。
    /// </summary>
    public sealed class StrategyRunTaskSample
    {
        public DateTime RunAt { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string RunStatus { get; set; } = "success";
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long CandleTimestamp { get; set; }
        public bool IsBarClose { get; set; }
        public int DurationMs { get; set; }
        public int LookupMs { get; set; }
        public int IndicatorMs { get; set; }
        public int ExecuteMs { get; set; }
        public int MatchedCount { get; set; }
        public int RunnableStrategyCount { get; set; }
        public int ExecutedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ConditionEvalCount { get; set; }
        public int ActionExecCount { get; set; }
        public int OpenTaskCount { get; set; }
        public int StateSkippedCount { get; set; }
        public int RuntimeGateSkippedCount { get; set; }
        public int IndicatorRequestCount { get; set; }
        public int IndicatorSuccessCount { get; set; }
        public int IndicatorTotalCount { get; set; }
        public string? ExecutedStrategyIds { get; set; }
        public string? OpenTaskStrategyIds { get; set; }
        public string? OpenTaskTraceIds { get; set; }
        public string? OpenOrderIds { get; set; }
        public string? EngineInstance { get; set; }
        public int PerStrategySamples { get; set; }
        public int PerStrategyAvgMs { get; set; }
        public int PerStrategyMaxMs { get; set; }
        public decimal SuccessRatePct { get; set; }
        public decimal OpenTaskRatePct { get; set; }
    }

    /// <summary>
    /// 最近开仓订单样本（从主记录里的 open_order_ids 解析）。
    /// </summary>
    public sealed class StrategyRunOrderSample
    {
        public string? OrderId { get; set; }
        public long? Uid { get; set; }
        public long? UsId { get; set; }
        public string? PositionSide { get; set; }
        public decimal? Qty { get; set; }
        public decimal? AveragePrice { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 策略运行画像快照（管理端策略详情最近5条）。
    /// </summary>
    public sealed class StrategyRunMetricSnapshotItem
    {
        public DateTime RunAt { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long CandleTimestamp { get; set; }
        public bool IsBarClose { get; set; }
        public int DurationMs { get; set; }
        public int LookupMs { get; set; }
        public int IndicatorMs { get; set; }
        public int ExecuteMs { get; set; }
        public int RunnableStrategyCount { get; set; }
        public int StateSkippedCount { get; set; }
        public int RuntimeGateSkippedCount { get; set; }
        public int IndicatorRequestCount { get; set; }
        public int IndicatorSuccessCount { get; set; }
        public int IndicatorTotalCount { get; set; }
        public int MatchedCount { get; set; }
        public int ExecutedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ConditionEvalCount { get; set; }
        public int ActionExecCount { get; set; }
        public int OpenTaskCount { get; set; }
        public string? ExecutedStrategyIds { get; set; }
        public string? OpenTaskStrategyIds { get; set; }
        public string? OpenTaskTraceIds { get; set; }
        public string? OpenOrderIds { get; set; }
        public string? EngineInstance { get; set; }
        public string? TraceId { get; set; }
        public string? RunStatus { get; set; }
        public int PerStrategySamples { get; set; }
        public int PerStrategyAvgMs { get; set; }
        public int PerStrategyMaxMs { get; set; }
        public decimal SuccessRatePct { get; set; }
        public decimal OpenTaskRatePct { get; set; }
    }
}
