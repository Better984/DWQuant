using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Indicators.Domain;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// 指标模块数据访问层。
    /// </summary>
    public sealed class IndicatorRepository
    {
        private readonly IDbManager _db;

        public IndicatorRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 确保指标相关表结构存在。
        /// </summary>
        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS `indicator_definitions` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `code` VARCHAR(128) NOT NULL COMMENT '指标编码，例如 coinglass.fear_greed',
  `provider` VARCHAR(64) NOT NULL COMMENT '数据提供方，例如 coinglass',
  `display_name` VARCHAR(128) NOT NULL COMMENT '展示名称',
  `shape` VARCHAR(32) NOT NULL COMMENT '数据形态，例如 gauge/timeseries/table',
  `unit` VARCHAR(32) NULL COMMENT '单位',
  `description` VARCHAR(255) NULL COMMENT '说明',
  `refresh_interval_sec` INT NOT NULL DEFAULT 300 COMMENT '刷新周期秒',
  `ttl_sec` INT NOT NULL DEFAULT 600 COMMENT '快照有效期秒',
  `history_retention_days` INT NOT NULL DEFAULT 30 COMMENT '历史保留天数',
  `source_endpoint` VARCHAR(255) NOT NULL DEFAULT '' COMMENT '数据源接口路径',
  `default_scope_key` VARCHAR(128) NOT NULL DEFAULT 'global' COMMENT '默认范围',
  `config_json` LONGTEXT NULL COMMENT '扩展配置 JSON',
  `enabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
  `sort_order` INT NOT NULL DEFAULT 100 COMMENT '展示排序',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_code` (`code`),
  KEY `idx_provider_enabled` (`provider`, `enabled`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='指标定义表';

CREATE TABLE IF NOT EXISTS `indicator_snapshots` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `code` VARCHAR(128) NOT NULL COMMENT '指标编码',
  `scope_key` VARCHAR(256) NOT NULL COMMENT '范围键',
  `provider` VARCHAR(64) NOT NULL COMMENT '数据提供方',
  `shape` VARCHAR(32) NOT NULL COMMENT '数据形态',
  `unit` VARCHAR(32) NULL COMMENT '单位',
  `display_name` VARCHAR(128) NULL COMMENT '展示名称冗余',
  `description` VARCHAR(255) NULL COMMENT '描述冗余',
  `payload_json` LONGTEXT NOT NULL COMMENT '指标负载 JSON',
  `source_ts` BIGINT NOT NULL COMMENT '源数据时间戳（毫秒）',
  `fetched_at` BIGINT NOT NULL COMMENT '采集时间戳（毫秒）',
  `expire_at` BIGINT NOT NULL COMMENT '过期时间戳（毫秒）',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_code_scope` (`code`, `scope_key`),
  KEY `idx_code_expire` (`code`, `expire_at`),
  KEY `idx_fetched` (`fetched_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='指标最新快照表';

CREATE TABLE IF NOT EXISTS `indicator_history` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `code` VARCHAR(128) NOT NULL COMMENT '指标编码',
  `scope_key` VARCHAR(256) NOT NULL COMMENT '范围键',
  `source_ts` BIGINT NOT NULL COMMENT '指标点位时间戳（毫秒）',
  `payload_json` LONGTEXT NOT NULL COMMENT '点位负载 JSON',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_code_scope_ts` (`code`, `scope_key`, `source_ts`),
  KEY `idx_code_scope_source` (`code`, `scope_key`, `source_ts`),
  KEY `idx_created` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='指标历史表';

CREATE TABLE IF NOT EXISTS `indicator_refresh_logs` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `code` VARCHAR(128) NOT NULL COMMENT '指标编码',
  `scope_key` VARCHAR(256) NOT NULL COMMENT '范围键',
  `status` VARCHAR(16) NOT NULL COMMENT 'success/failed/skipped',
  `message` VARCHAR(255) NULL COMMENT '刷新信息',
  `latency_ms` INT NULL COMMENT '耗时毫秒',
  `started_at` BIGINT NOT NULL COMMENT '开始时间戳（毫秒）',
  `finished_at` BIGINT NOT NULL COMMENT '结束时间戳（毫秒）',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_code_created` (`code`, `created_at`),
  KEY `idx_status_created` (`status`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='指标刷新日志表';
";
            await _db.ExecuteAsync(sql, null, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 写入默认指标定义（仅在不存在时插入）。
        /// </summary>
        public Task EnsureSeedDefinitionsAsync(CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO indicator_definitions
(
  code,
  provider,
  display_name,
  shape,
  unit,
  description,
  refresh_interval_sec,
  ttl_sec,
  history_retention_days,
  source_endpoint,
  default_scope_key,
  config_json,
  enabled,
  sort_order
)
VALUES
(
  @Code,
  @Provider,
  @DisplayName,
  @Shape,
  @Unit,
  @Description,
  @RefreshIntervalSec,
  @TtlSec,
  @HistoryRetentionDays,
  @SourceEndpoint,
  @DefaultScopeKey,
  @ConfigJson,
  @Enabled,
  @SortOrder
)
ON DUPLICATE KEY UPDATE
  code = code
";

            var seed = new[]
            {
                new
                {
                    Code = "coinglass.fear_greed",
                    Provider = "coinglass",
                    DisplayName = "贪婪恐慌指数",
                    Shape = "gauge",
                    Unit = "index",
                    Description = "CoinGlass 贪婪恐慌指数，范围 0-100",
                    RefreshIntervalSec = 300,
                    TtlSec = 600,
                    HistoryRetentionDays = 180,
                    SourceEndpoint = "/api/index/fear-greed-history",
                    DefaultScopeKey = "global",
                    ConfigJson = "{}",
                    Enabled = true,
                    SortOrder = 10
                }
            };

            return _db.ExecuteAsync(sql, seed, null, ct);
        }

        /// <summary>
        /// 获取所有指标定义。
        /// </summary>
        public Task<IEnumerable<IndicatorDefinition>> GetAllDefinitionsAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  code AS Code,
  provider AS Provider,
  display_name AS DisplayName,
  shape AS Shape,
  unit AS Unit,
  description AS Description,
  refresh_interval_sec AS RefreshIntervalSec,
  ttl_sec AS TtlSec,
  history_retention_days AS HistoryRetentionDays,
  source_endpoint AS SourceEndpoint,
  default_scope_key AS DefaultScopeKey,
  config_json AS ConfigJson,
  enabled AS Enabled,
  sort_order AS SortOrder
FROM indicator_definitions
ORDER BY sort_order ASC, code ASC
";
            return _db.QueryAsync<IndicatorDefinition>(sql, null, null, ct);
        }

        /// <summary>
        /// 获取单个指标定义。
        /// </summary>
        public Task<IndicatorDefinition?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  code AS Code,
  provider AS Provider,
  display_name AS DisplayName,
  shape AS Shape,
  unit AS Unit,
  description AS Description,
  refresh_interval_sec AS RefreshIntervalSec,
  ttl_sec AS TtlSec,
  history_retention_days AS HistoryRetentionDays,
  source_endpoint AS SourceEndpoint,
  default_scope_key AS DefaultScopeKey,
  config_json AS ConfigJson,
  enabled AS Enabled,
  sort_order AS SortOrder
FROM indicator_definitions
WHERE code = @Code
LIMIT 1
";
            return _db.QuerySingleOrDefaultAsync<IndicatorDefinition>(sql, new { Code = code }, null, ct);
        }

        /// <summary>
        /// 获取单个指标快照。
        /// </summary>
        public Task<IndicatorSnapshot?> GetSnapshotAsync(string code, string scopeKey, CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  code AS Code,
  scope_key AS ScopeKey,
  provider AS Provider,
  shape AS Shape,
  unit AS Unit,
  display_name AS DisplayName,
  description AS Description,
  payload_json AS PayloadJson,
  source_ts AS SourceTs,
  fetched_at AS FetchedAt,
  expire_at AS ExpireAt
FROM indicator_snapshots
WHERE code = @Code AND scope_key = @ScopeKey
LIMIT 1
";
            return _db.QuerySingleOrDefaultAsync<IndicatorSnapshot>(sql, new { Code = code, ScopeKey = scopeKey }, null, ct);
        }

        /// <summary>
        /// 快照写入（存在则更新）。
        /// </summary>
        public Task UpsertSnapshotAsync(IndicatorSnapshot snapshot, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO indicator_snapshots
(
  code,
  scope_key,
  provider,
  shape,
  unit,
  display_name,
  description,
  payload_json,
  source_ts,
  fetched_at,
  expire_at
)
VALUES
(
  @Code,
  @ScopeKey,
  @Provider,
  @Shape,
  @Unit,
  @DisplayName,
  @Description,
  @PayloadJson,
  @SourceTs,
  @FetchedAt,
  @ExpireAt
)
ON DUPLICATE KEY UPDATE
  provider = VALUES(provider),
  shape = VALUES(shape),
  unit = VALUES(unit),
  display_name = VALUES(display_name),
  description = VALUES(description),
  payload_json = VALUES(payload_json),
  source_ts = VALUES(source_ts),
  fetched_at = VALUES(fetched_at),
  expire_at = VALUES(expire_at),
  updated_at = CURRENT_TIMESTAMP
";
            return _db.ExecuteAsync(sql, snapshot, null, ct);
        }

        /// <summary>
        /// 批量写入历史点位。
        /// </summary>
        public Task UpsertHistoryBatchAsync(string code, string scopeKey, IReadOnlyList<IndicatorHistoryPoint> points, CancellationToken ct = default)
        {
            if (points.Count == 0)
            {
                return Task.CompletedTask;
            }

            const string sql = @"
INSERT INTO indicator_history
(
  code,
  scope_key,
  source_ts,
  payload_json
)
VALUES
(
  @Code,
  @ScopeKey,
  @SourceTs,
  @PayloadJson
)
ON DUPLICATE KEY UPDATE
  payload_json = VALUES(payload_json)
";

            var rows = points.Select(point => new
            {
                Code = code,
                ScopeKey = scopeKey,
                SourceTs = point.SourceTs,
                PayloadJson = point.PayloadJson
            }).ToList();

            return _db.ExecuteAsync(sql, rows, null, ct);
        }

        /// <summary>
        /// 查询历史点位（按时间升序）。
        /// </summary>
        public async Task<IReadOnlyList<IndicatorHistoryPoint>> GetHistoryAsync(
            string code,
            string scopeKey,
            long? startMs,
            long? endMs,
            int limit,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  source_ts AS SourceTs,
  payload_json AS PayloadJson
FROM indicator_history
WHERE code = @Code
  AND scope_key = @ScopeKey
  AND (@StartMs IS NULL OR source_ts >= @StartMs)
  AND (@EndMs IS NULL OR source_ts <= @EndMs)
ORDER BY source_ts DESC
LIMIT @Limit
";
            var desc = await _db.QueryAsync<IndicatorHistoryPoint>(
                sql,
                new
                {
                    Code = code,
                    ScopeKey = scopeKey,
                    StartMs = startMs,
                    EndMs = endMs,
                    Limit = limit
                },
                null,
                ct).ConfigureAwait(false);

            return desc.OrderBy(item => item.SourceTs).ToList();
        }

        /// <summary>
        /// 写入刷新日志。
        /// </summary>
        public Task InsertRefreshLogAsync(
            string code,
            string scopeKey,
            string status,
            string? message,
            int? latencyMs,
            long startedAt,
            long finishedAt,
            CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO indicator_refresh_logs
(
  code,
  scope_key,
  status,
  message,
  latency_ms,
  started_at,
  finished_at
)
VALUES
(
  @Code,
  @ScopeKey,
  @Status,
  @Message,
  @LatencyMs,
  @StartedAt,
  @FinishedAt
)
";
            return _db.ExecuteAsync(sql, new
            {
                Code = code,
                ScopeKey = scopeKey,
                Status = status,
                Message = message,
                LatencyMs = latencyMs,
                StartedAt = startedAt,
                FinishedAt = finishedAt
            }, null, ct);
        }

        /// <summary>
        /// 清理历史数据。
        /// </summary>
        public Task<int> CleanupHistoryAsync(string code, string scopeKey, long cutoffMs, CancellationToken ct = default)
        {
            const string sql = @"
DELETE FROM indicator_history
WHERE code = @Code
  AND scope_key = @ScopeKey
  AND source_ts < @CutoffMs
";
            return _db.ExecuteAsync(sql, new { Code = code, ScopeKey = scopeKey, CutoffMs = cutoffMs }, null, ct);
        }
    }
}
