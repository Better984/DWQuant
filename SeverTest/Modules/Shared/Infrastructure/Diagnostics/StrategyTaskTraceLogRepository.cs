using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.Shared.Infrastructure.Diagnostics
{
    /// <summary>
    /// 策略链路追踪日志仓储。
    /// </summary>
    public sealed class StrategyTaskTraceLogRepository
    {
        private readonly IDbManager _db;
        private readonly ILogger<StrategyTaskTraceLogRepository> _logger;

        public StrategyTaskTraceLogRepository(
            IDbManager db,
            ILogger<StrategyTaskTraceLogRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 确保链路追踪日志表存在，并补充关键索引。
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string createSql = @"
CREATE TABLE IF NOT EXISTS strategy_task_trace_log (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  trace_id VARCHAR(64) NOT NULL COMMENT '根链路追踪ID',
  parent_trace_id VARCHAR(64) NULL COMMENT '父链路ID（动作子链路等）',
  event_stage VARCHAR(64) NOT NULL COMMENT '阶段标识',
  event_status VARCHAR(32) NOT NULL COMMENT '阶段状态: start/success/fail/skip/degraded',
  actor_module VARCHAR(64) NOT NULL COMMENT '处理模块',
  actor_instance VARCHAR(128) NOT NULL COMMENT '处理实例（机器:进程:实例ID）',
  uid BIGINT UNSIGNED NULL COMMENT '用户ID',
  us_id BIGINT UNSIGNED NULL COMMENT '策略实例ID',
  strategy_uid VARCHAR(64) NULL COMMENT '策略字符串UID',
  exchange VARCHAR(32) NULL COMMENT '交易所',
  symbol VARCHAR(32) NULL COMMENT '交易对',
  timeframe VARCHAR(16) NULL COMMENT '周期',
  candle_timestamp BIGINT NULL COMMENT 'K线时间戳(毫秒)',
  is_bar_close TINYINT(1) NULL COMMENT '是否收线',
  method VARCHAR(64) NULL COMMENT '方法/动作',
  flow VARCHAR(32) NULL COMMENT '流程分支',
  duration_ms INT NULL COMMENT '阶段耗时(毫秒)',
  metrics_json LONGTEXT NULL COMMENT '阶段明细JSON',
  error_message VARCHAR(1024) NULL COMMENT '失败原因',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
  PRIMARY KEY (id),
  INDEX idx_trace_stage (trace_id, event_stage, id),
  INDEX idx_created (created_at DESC),
  INDEX idx_market (exchange, symbol, timeframe, candle_timestamp),
  INDEX idx_usid_created (us_id, created_at DESC),
  INDEX idx_actor_created (actor_instance, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='策略任务链路追踪日志';";

            await _db.ExecuteAsync(createSql, null, null, ct).ConfigureAwait(false);

            await EnsureIndexAsync(
                    "idx_trace_stage",
                    "ALTER TABLE strategy_task_trace_log ADD INDEX idx_trace_stage (trace_id, event_stage, id);",
                    ct)
                .ConfigureAwait(false);
            await EnsureIndexAsync(
                    "idx_created",
                    "ALTER TABLE strategy_task_trace_log ADD INDEX idx_created (created_at DESC);",
                    ct)
                .ConfigureAwait(false);
            await EnsureIndexAsync(
                    "idx_market",
                    "ALTER TABLE strategy_task_trace_log ADD INDEX idx_market (exchange, symbol, timeframe, candle_timestamp);",
                    ct)
                .ConfigureAwait(false);
            await EnsureIndexAsync(
                    "idx_usid_created",
                    "ALTER TABLE strategy_task_trace_log ADD INDEX idx_usid_created (us_id, created_at DESC);",
                    ct)
                .ConfigureAwait(false);
            await EnsureIndexAsync(
                    "idx_actor_created",
                    "ALTER TABLE strategy_task_trace_log ADD INDEX idx_actor_created (actor_instance, created_at DESC);",
                    ct)
                .ConfigureAwait(false);
        }

        public Task<int> InsertAsync(StrategyTaskTraceLog log, CancellationToken ct = default)
        {
            if (log == null)
            {
                return Task.FromResult(0);
            }

            return InsertBatchAsync(new[] { log }, ct);
        }

        public Task<int> InsertBatchAsync(IReadOnlyCollection<StrategyTaskTraceLog> logs, CancellationToken ct = default)
        {
            if (logs == null || logs.Count == 0)
            {
                return Task.FromResult(0);
            }

            const string sql = @"
INSERT INTO strategy_task_trace_log
(
  trace_id,
  parent_trace_id,
  event_stage,
  event_status,
  actor_module,
  actor_instance,
  uid,
  us_id,
  strategy_uid,
  exchange,
  symbol,
  timeframe,
  candle_timestamp,
  is_bar_close,
  method,
  flow,
  duration_ms,
  metrics_json,
  error_message,
  created_at
)
VALUES
(
  @TraceId,
  @ParentTraceId,
  @EventStage,
  @EventStatus,
  @ActorModule,
  @ActorInstance,
  @Uid,
  @UsId,
  @StrategyUid,
  @Exchange,
  @Symbol,
  @Timeframe,
  @CandleTimestamp,
  @IsBarClose,
  @Method,
  @Flow,
  @DurationMs,
  @MetricsJson,
  @ErrorMessage,
  UTC_TIMESTAMP(3)
);";

            return _db.ExecuteAsync(sql, logs, null, ct);
        }

        /// <summary>
        /// 按 trace_id 聚合分页查询任务摘要（用于服务器实盘任务列表展示）。
        /// 每个任务一行：交易所、周期、币对、策略数、总耗时等。
        /// </summary>
        public async Task<(int Total, List<StrategyTaskTraceSummaryItem> Items)> ListAggregatedByActorInstancePrefixAsync(
            string actorInstancePrefix,
            int page = 1,
            int pageSize = 100,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(actorInstancePrefix))
            {
                return (0, new List<StrategyTaskTraceSummaryItem>());
            }

            page = Math.Max(1, page);
            pageSize = Math.Min(Math.Max(1, pageSize), 100);
            var offset = (page - 1) * pageSize;
            var pattern = actorInstancePrefix.TrimEnd(':') + ":%";

            const string countSql = @"
SELECT COUNT(DISTINCT trace_id) FROM strategy_task_trace_log
WHERE actor_instance LIKE @pattern;";

            const string listSql = @"
SELECT
  trace_id AS TraceId,
  MAX(exchange) AS Exchange,
  MAX(symbol) AS Symbol,
  MAX(timeframe) AS Timeframe,
  MAX(candle_timestamp) AS CandleTimestamp,
  COUNT(DISTINCT us_id) AS StrategyCount,
  COALESCE(SUM(duration_ms), 0) AS TotalDurationMs,
  MIN(created_at) AS FirstCreatedAt,
  MAX(created_at) AS LastCreatedAt
FROM strategy_task_trace_log
WHERE actor_instance LIKE @pattern
GROUP BY trace_id
ORDER BY MAX(created_at) DESC, trace_id DESC
LIMIT @pageSize OFFSET @offset;";

            var total = await _db.ExecuteScalarAsync<int>(countSql, new { pattern }, null, ct).ConfigureAwait(false);
            var items = (await _db.QueryAsync<StrategyTaskTraceSummaryItem>(listSql, new { pattern, pageSize, offset }, null, ct).ConfigureAwait(false)).ToList();
            return (total, items);
        }

        /// <summary>
        /// 按 exchange/symbol/timeframe 聚合查询布局（用于布局查看页签）。
        /// 每个市场一行：交易所、币对、周期、策略数、任务数、总耗时、最后执行时间。
        /// </summary>
        public async Task<List<StrategyTaskTraceLayoutItem>> ListLayoutByActorInstancePrefixAsync(
            string actorInstancePrefix,
            int limit = 200,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(actorInstancePrefix))
            {
                return new List<StrategyTaskTraceLayoutItem>();
            }

            limit = Math.Min(Math.Max(1, limit), 500);
            var pattern = actorInstancePrefix.TrimEnd(':') + ":%";

            const string sql = @"
SELECT
  COALESCE(exchange, '') AS Exchange,
  COALESCE(symbol, '') AS Symbol,
  COALESCE(timeframe, '') AS Timeframe,
  COUNT(DISTINCT us_id) AS StrategyCount,
  COUNT(DISTINCT trace_id) AS TaskCount,
  COALESCE(SUM(duration_ms), 0) AS TotalDurationMs,
  MAX(created_at) AS LastExecutedAt
FROM strategy_task_trace_log
WHERE actor_instance LIKE @pattern
  AND exchange IS NOT NULL AND exchange != ''
  AND symbol IS NOT NULL AND symbol != ''
  AND timeframe IS NOT NULL AND timeframe != ''
GROUP BY exchange, symbol, timeframe
ORDER BY MAX(created_at) DESC
LIMIT @limit;";

            return (await _db.QueryAsync<StrategyTaskTraceLayoutItem>(sql, new { pattern, limit }, null, ct).ConfigureAwait(false)).ToList();
        }

        /// <summary>
        /// 按 trace_id 查询完整链路明细（用于点击任务后的详情弹窗）。
        /// 不按 actor 过滤，返回该任务的全链路记录。
        /// </summary>
        public async Task<List<StrategyTaskTraceLogListItem>> GetByTraceIdAsync(string traceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return new List<StrategyTaskTraceLogListItem>();
            }

            const string sql = @"
SELECT
  id AS Id,
  trace_id AS TraceId,
  parent_trace_id AS ParentTraceId,
  event_stage AS EventStage,
  event_status AS EventStatus,
  actor_module AS ActorModule,
  actor_instance AS ActorInstance,
  uid AS Uid,
  us_id AS UsId,
  strategy_uid AS StrategyUid,
  exchange AS Exchange,
  symbol AS Symbol,
  timeframe AS Timeframe,
  candle_timestamp AS CandleTimestamp,
  is_bar_close AS IsBarClose,
  method AS Method,
  flow AS Flow,
  duration_ms AS DurationMs,
  metrics_json AS MetricsJson,
  error_message AS ErrorMessage,
  created_at AS CreatedAt
FROM strategy_task_trace_log
WHERE trace_id = @traceId
ORDER BY created_at ASC, id ASC;";

            return (await _db.QueryAsync<StrategyTaskTraceLogListItem>(sql, new { traceId }, null, ct).ConfigureAwait(false)).ToList();
        }

        /// <summary>
        /// 获取指定市场的任务汇总：任务数、平均耗时、成功率、按阶段统计、最近下单样本。
        /// </summary>
        public async Task<StrategyTaskTraceMarketSummary?> GetMarketTaskSummaryAsync(
            string actorInstancePrefix,
            string exchange,
            string symbol,
            string timeframe,
            int limitTraces = 500,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(actorInstancePrefix))
            {
                return null;
            }

            var pattern = actorInstancePrefix.TrimEnd(':') + ":%";

            const string sql = @"
WITH trace_totals AS (
  SELECT
    trace_id,
    SUM(duration_ms) AS total_duration_ms,
    MAX(CASE WHEN event_status = 'fail' THEN 1 ELSE 0 END) AS has_fail
  FROM strategy_task_trace_log
  WHERE actor_instance LIKE @pattern
    AND COALESCE(exchange, '') = @exchange
    AND COALESCE(symbol, '') = @symbol
    AND COALESCE(timeframe, '') = @timeframe
  GROUP BY trace_id
  ORDER BY MAX(created_at) DESC
  LIMIT @limitTraces
),
trace_stats AS (
  SELECT
    COUNT(*) AS task_count,
    AVG(total_duration_ms) AS avg_duration_ms,
    SUM(CASE WHEN has_fail = 0 THEN 1 ELSE 0 END) AS success_count
  FROM trace_totals
)
SELECT
  task_count AS TaskCount,
  ROUND(avg_duration_ms, 0) AS AvgDurationMs,
  CASE WHEN task_count > 0 THEN ROUND(success_count * 100.0 / task_count, 2) ELSE 100 END AS SuccessRatePct
FROM trace_stats;";

            var row = await _db.QuerySingleOrDefaultAsync<MarketSummaryRow>(sql, new { pattern, exchange, symbol, timeframe, limitTraces }, null, ct).ConfigureAwait(false);
            if (row == null)
            {
                return null;
            }

            var taskCount = row.TaskCount;
            if (taskCount == 0)
            {
                return new StrategyTaskTraceMarketSummary
                {
                    Exchange = exchange,
                    Symbol = symbol,
                    Timeframe = timeframe,
                    TaskCount = 0,
                    AvgDurationMs = 0,
                    SuccessRatePct = 100
                };
            }

            const string stageSql = @"
SELECT
  event_stage AS EventStage,
  COUNT(DISTINCT trace_id) AS TraceCount,
  ROUND(AVG(duration_ms), 0) AS AvgDurationMs
FROM strategy_task_trace_log
WHERE actor_instance LIKE @pattern
  AND COALESCE(exchange, '') = @exchange
  AND COALESCE(symbol, '') = @symbol
  AND COALESCE(timeframe, '') = @timeframe
  AND duration_ms IS NOT NULL
GROUP BY event_stage
ORDER BY event_stage;";

            var stageRows = (await _db.QueryAsync<StrategyTaskTraceStageStat>(
                    stageSql,
                    new { pattern, exchange, symbol, timeframe },
                    null,
                    ct)
                ).ToList();

            const string recentOrderSql = @"
SELECT
  JSON_UNQUOTE(JSON_EXTRACT(metrics_json, '$.orderId')) AS OrderId,
  uid AS Uid,
  us_id AS UsId,
  JSON_UNQUOTE(JSON_EXTRACT(metrics_json, '$.positionSide')) AS PositionSide,
  JSON_EXTRACT(metrics_json, '$.qty') AS Qty,
  JSON_EXTRACT(metrics_json, '$.averagePrice') AS AveragePrice,
  created_at AS CreatedAt
FROM strategy_task_trace_log
WHERE actor_instance LIKE @pattern
  AND COALESCE(exchange, '') = @exchange
  AND COALESCE(symbol, '') = @symbol
  AND COALESCE(timeframe, '') = @timeframe
  AND event_stage = 'trade.open.order'
  AND event_status = 'success'
ORDER BY created_at DESC
LIMIT @recentLimit;";

            const int recentLimit = 20;
            var recentOrders = (await _db.QueryAsync<StrategyTaskTraceOrderSample>(
                    recentOrderSql,
                    new { pattern, exchange, symbol, timeframe, recentLimit },
                    null,
                    ct)
                ).ToList();

            return new StrategyTaskTraceMarketSummary
            {
                Exchange = exchange,
                Symbol = symbol,
                Timeframe = timeframe,
                TaskCount = taskCount,
                AvgDurationMs = row.AvgDurationMs,
                SuccessRatePct = row.SuccessRatePct,
                StageStats = stageRows,
                RecentOrders = recentOrders
            };
        }

        private async Task EnsureIndexAsync(string indexName, string alterSql, CancellationToken ct)
        {
            const string checkSql = @"
SELECT COUNT(*) FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'strategy_task_trace_log' AND INDEX_NAME = @indexName;";

            var exists = await _db.ExecuteScalarAsync<int>(checkSql, new { indexName }, null, ct).ConfigureAwait(false);
            if (exists > 0)
            {
                return;
            }

            await _db.ExecuteAsync(alterSql, null, null, ct).ConfigureAwait(false);
            _logger.LogInformation("strategy_task_trace_log 已补充索引: {IndexName}", indexName);
        }
    }

    /// <summary>
    /// 策略任务按 exchange/symbol/timeframe 聚合的布局项（布局查看）。
    /// </summary>
    public sealed class StrategyTaskTraceLayoutItem
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
    /// 策略任务按 trace_id 聚合的摘要项（列表展示）。
    /// </summary>
    public sealed class StrategyTaskTraceSummaryItem
    {
        public string TraceId { get; set; } = string.Empty;
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
        public long? CandleTimestamp { get; set; }
        public int StrategyCount { get; set; }
        public int TotalDurationMs { get; set; }
        public DateTime FirstCreatedAt { get; set; }
        public DateTime LastCreatedAt { get; set; }
    }

    /// <summary>
    /// 策略任务链路追踪明细项（详情弹窗展示）。
    /// </summary>
    public sealed class StrategyTaskTraceLogListItem
    {
        public long Id { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string? ParentTraceId { get; set; }
        public string EventStage { get; set; } = string.Empty;
        public string EventStatus { get; set; } = string.Empty;
        public string ActorModule { get; set; } = string.Empty;
        public string ActorInstance { get; set; } = string.Empty;
        public long? Uid { get; set; }
        public long? UsId { get; set; }
        public string? StrategyUid { get; set; }
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
        public long? CandleTimestamp { get; set; }
        public bool? IsBarClose { get; set; }
        public string? Method { get; set; }
        public string? Flow { get; set; }
        public int? DurationMs { get; set; }
        public string? MetricsJson { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 指定市场的任务汇总（任务执行报告 Tab）。
    /// </summary>
    public sealed class StrategyTaskTraceMarketSummary
    {
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public int TaskCount { get; set; }
        public int AvgDurationMs { get; set; }
        public decimal SuccessRatePct { get; set; }
        public List<StrategyTaskTraceStageStat> StageStats { get; set; } = new();
        public List<StrategyTaskTraceOrderSample> RecentOrders { get; set; } = new();
    }

    /// <summary>
    /// 按阶段统计的任务耗时。
    /// </summary>
    public sealed class StrategyTaskTraceStageStat
    {
        public string EventStage { get; set; } = string.Empty;
        public int TraceCount { get; set; }
        public int AvgDurationMs { get; set; }
    }

    /// <summary>
    /// 最近下单样本（用于任务执行报告中按订单反查策略与任务）。
    /// </summary>
    public sealed class StrategyTaskTraceOrderSample
    {
        public string? OrderId { get; set; }
        public long? Uid { get; set; }
        public long? UsId { get; set; }
        public string? PositionSide { get; set; }
        public decimal? Qty { get; set; }
        public decimal? AveragePrice { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    internal sealed class MarketSummaryRow
    {
        public int TaskCount { get; set; }
        public int AvgDurationMs { get; set; }
        public decimal SuccessRatePct { get; set; }
    }
}
