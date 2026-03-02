using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Protocol;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// 指标模块数据访问层。
    /// </summary>
    public sealed class IndicatorRepository
    {
        private readonly IDbManager _db;
        private readonly ConcurrentDictionary<string, byte> _historyTableReady = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _historyTableLocks = new(StringComparer.Ordinal);

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
            await EnsureHistoryTableReadyAsync("coinglass.fear_greed", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.etf_flow", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.liquidation_heatmap_model1", ct).ConfigureAwait(false);
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
  config_json = CASE
    WHEN indicator_definitions.config_json IS NULL
      OR TRIM(indicator_definitions.config_json) = ''
      OR TRIM(indicator_definitions.config_json) = '{}'
    THEN VALUES(config_json)
    ELSE indicator_definitions.config_json
  END,
  source_endpoint = CASE
    WHEN indicator_definitions.source_endpoint IS NULL
      OR TRIM(indicator_definitions.source_endpoint) = ''
    THEN VALUES(source_endpoint)
    ELSE indicator_definitions.source_endpoint
  END,
  default_scope_key = CASE
    WHEN indicator_definitions.default_scope_key IS NULL
      OR TRIM(indicator_definitions.default_scope_key) = ''
    THEN VALUES(default_scope_key)
    ELSE indicator_definitions.default_scope_key
  END
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
                    RefreshIntervalSec = 60 * 20,
                    TtlSec = 60 * 20,
                    HistoryRetentionDays = 180,
                    SourceEndpoint = "/api/index/fear-greed-history",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildFearGreedConfigJson(),
                    Enabled = false,
                    SortOrder = 10
                },
                new
                {
                    Code = "coinglass.etf_flow",
                    Provider = "coinglass",
                    DisplayName = "比特币现货 ETF 净流入",
                    Shape = "timeseries",
                    Unit = "usd",
                    Description = "CoinGlass 比特币现货 ETF 每日净流入（美元）",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 365,
                    SourceEndpoint = "/api/etf/bitcoin/flow-history",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildEtfFlowConfigJson(),
                    Enabled = true,
                    SortOrder = 20
                },
                new
                {
                    Code = "coinglass.liquidation_heatmap_model1",
                    Provider = "coinglass",
                    DisplayName = "交易对爆仓热力图（模型1）",
                    Shape = "heatmap",
                    Unit = "usd",
                    Description = "CoinGlass 交易对爆仓热力图（模型1），包含价格轴、热力点与K线数据",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 60,
                    SourceEndpoint = "/api/futures/liquidation/heatmap/model1",
                    DefaultScopeKey = "exchange=Binance&symbol=BTCUSDT&range=3d",
                    ConfigJson = BuildLiquidationHeatmapModel1ConfigJson(),
                    Enabled = true,
                    SortOrder = 30
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
        public async Task UpsertHistoryBatchAsync(string code, string scopeKey, IReadOnlyList<IndicatorHistoryPoint> points, CancellationToken ct = default)
        {
            if (points.Count == 0)
            {
                return;
            }

            var historyTable = await EnsureHistoryTableReadyAsync(code, ct).ConfigureAwait(false);
            var sql = $@"
INSERT INTO `{historyTable}`
(
  scope_key,
  source_ts,
  payload_json
)
VALUES
(
  @ScopeKey,
  @SourceTs,
  @PayloadJson
)
ON DUPLICATE KEY UPDATE
  payload_json = VALUES(payload_json)
";

            var rows = points.Select(point => new
            {
                ScopeKey = scopeKey,
                SourceTs = point.SourceTs,
                PayloadJson = point.PayloadJson
            }).ToList();

            await _db.ExecuteAsync(sql, rows, null, ct).ConfigureAwait(false);
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
            var historyTable = await EnsureHistoryTableReadyAsync(code, ct).ConfigureAwait(false);
            var sql = $@"
SELECT
  source_ts AS SourceTs,
  payload_json AS PayloadJson
FROM `{historyTable}`
WHERE scope_key = @ScopeKey
  AND (@StartMs IS NULL OR source_ts >= @StartMs)
  AND (@EndMs IS NULL OR source_ts <= @EndMs)
ORDER BY source_ts DESC
LIMIT @Limit
";
            var desc = await _db.QueryAsync<IndicatorHistoryPoint>(
                sql,
                new
                {
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
        public async Task<int> CleanupHistoryAsync(string code, string scopeKey, long cutoffMs, CancellationToken ct = default)
        {
            var historyTable = await EnsureHistoryTableReadyAsync(code, ct).ConfigureAwait(false);
            var sql = $@"
DELETE FROM `{historyTable}`
WHERE scope_key = @ScopeKey
  AND source_ts < @CutoffMs
";
            return await _db.ExecuteAsync(sql, new { ScopeKey = scopeKey, CutoffMs = cutoffMs }, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 确保指标历史分表存在。
        /// 表命名规范：coinglass_{指标代码}_history（示例：coinglass_fear_greed_history）。
        /// </summary>
        private async Task<string> EnsureHistoryTableReadyAsync(string code, CancellationToken ct)
        {
            var historyTable = BuildHistoryTableName(code);
            if (_historyTableReady.ContainsKey(historyTable))
            {
                return historyTable;
            }

            var tableLock = _historyTableLocks.GetOrAdd(historyTable, _ => new SemaphoreSlim(1, 1));
            await tableLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_historyTableReady.ContainsKey(historyTable))
                {
                    return historyTable;
                }

                var createSql = $@"
CREATE TABLE IF NOT EXISTS `{historyTable}` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `scope_key` VARCHAR(256) NOT NULL COMMENT '范围键',
  `source_ts` BIGINT NOT NULL COMMENT '指标点位时间戳（毫秒）',
  `payload_json` LONGTEXT NOT NULL COMMENT '点位负载 JSON',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_scope_ts` (`scope_key`, `source_ts`),
  KEY `idx_scope_source` (`scope_key`, `source_ts`),
  KEY `idx_created` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='指标历史分表';
";
                await _db.ExecuteAsync(createSql, null, null, ct).ConfigureAwait(false);

                _historyTableReady.TryAdd(historyTable, 0);
                return historyTable;
            }
            finally
            {
                tableLock.Release();
            }
        }

        private static string BuildHistoryTableName(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("指标 code 不能为空", nameof(code));
            }

            var normalized = code.Trim().ToLowerInvariant();
            if (normalized.StartsWith("coinglass.", StringComparison.Ordinal))
            {
                normalized = normalized["coinglass.".Length..];
            }
            else if (normalized.StartsWith("coinglass_", StringComparison.Ordinal))
            {
                normalized = normalized["coinglass_".Length..];
            }

            var metricCode = Regex.Replace(normalized, @"[^a-z0-9_]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(metricCode))
            {
                metricCode = "metric";
            }

            var tableName = $"coinglass_{metricCode}_history";
            if (!Regex.IsMatch(tableName, @"^[a-z0-9_]+$"))
            {
                throw new InvalidOperationException($"非法历史表名: {tableName}");
            }

            return tableName;
        }

        private static string BuildFearGreedConfigJson()
        {
            return ProtocolJson.Serialize(new
            {
                source = new
                {
                    type = "http_pull",
                    protocol = "coinglass.http",
                    mode = "pull",
                    supportsRealtimeWs = false
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "贪婪恐慌指数值",
                        dataType = "number",
                        unit = "index",
                        conditionSupported = true,
                        description = "当前贪婪恐慌指数值，范围 0-100。"
                    },
                    new
                    {
                        path = "signals.below9",
                        displayName = "指数低于9",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "当指数 < 9 时为 true。"
                    },
                    new
                    {
                        path = "signals.below10",
                        displayName = "指数低于10",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "当指数 < 10 时为 true。"
                    },
                    new
                    {
                        path = "signals.below9StreakDays",
                        displayName = "低于9连续天数",
                        dataType = "number",
                        unit = "day",
                        conditionSupported = true,
                        description = "连续低于 9 的天数统计。"
                    },
                    new
                    {
                        path = "signals.below10StreakDays",
                        displayName = "低于10连续天数",
                        dataType = "number",
                        unit = "day",
                        conditionSupported = true,
                        description = "连续低于 10 的天数统计。"
                    },
                    new
                    {
                        path = "signals.below9Consecutive3d",
                        displayName = "低于9连续3天",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "连续 3 天低于 9 时为 true。"
                    },
                    new
                    {
                        path = "signals.below10Consecutive3d",
                        displayName = "低于10连续3天",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "连续 3 天低于 10 时为 true。"
                    }
                }
            });
        }

        private static string BuildEtfFlowConfigJson()
        {
            return ProtocolJson.Serialize(new
            {
                source = new
                {
                    type = "http_pull",
                    protocol = "coinglass.http",
                    mode = "pull",
                    supportsRealtimeWs = false
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "ETF净流入主值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当日 ETF 净流入，等价于 netFlowUsd。"
                    },
                    new
                    {
                        path = "netFlowUsd",
                        displayName = "当日净流入金额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当日净流入金额（美元）。"
                    },
                    new
                    {
                        path = "signals.isNetInflow",
                        displayName = "当日是否净流入",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "当日净流入 > 0 时为 true。"
                    },
                    new
                    {
                        path = "signals.isNetOutflow",
                        displayName = "当日是否净流出",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "当日净流入 < 0 时为 true。"
                    },
                    new
                    {
                        path = "signals.netInflowStreakDays",
                        displayName = "连续净流入天数",
                        dataType = "number",
                        unit = "day",
                        conditionSupported = true,
                        description = "截至当前连续净流入天数。"
                    },
                    new
                    {
                        path = "signals.netOutflowStreakDays",
                        displayName = "连续净流出天数",
                        dataType = "number",
                        unit = "day",
                        conditionSupported = true,
                        description = "截至当前连续净流出天数。"
                    },
                    new
                    {
                        path = "signals.netInflow3dAllPositive",
                        displayName = "近3天持续净流入",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "近 3 天净流入均 > 0 时为 true。"
                    },
                    new
                    {
                        path = "signals.netFlow7dSumUsd",
                        displayName = "近7天净流入总和",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "近 7 天净流入金额总和。"
                    },
                    new
                    {
                        path = "signals.netFlow7dSumPositive",
                        displayName = "近7天净流入总和大于0",
                        dataType = "boolean",
                        unit = (string?)null,
                        conditionSupported = true,
                        description = "近 7 天净流入总和 > 0 时为 true。"
                    },
                    new
                    {
                        path = "signals.inflow7dRatio",
                        displayName = "近7天流入占比",
                        dataType = "number",
                        unit = "ratio",
                        conditionSupported = true,
                        description = "近 7 天流入占比 = inflow7dUsd / (inflow7dUsd + outflow7dAbsUsd)。"
                    },
                    new
                    {
                        path = "signals.inflow7dUsd",
                        displayName = "近7天流入总量",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "近 7 天正向流入金额合计。"
                    },
                    new
                    {
                        path = "signals.outflow7dAbsUsd",
                        displayName = "近7天流出绝对值总量",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "近 7 天负向流出绝对值金额合计。"
                    }
                }
            });
        }

        private static string BuildLiquidationHeatmapModel1ConfigJson()
        {
            return ProtocolJson.Serialize(new
            {
                source = new
                {
                    type = "http_pull",
                    protocol = "coinglass.http",
                    mode = "pull",
                    supportsRealtimeWs = false
                },
                fields = new object[]
                {
                    new
                    {
                        path = "yAxis",
                        displayName = "价格轴",
                        dataType = "array",
                        unit = "price",
                        conditionSupported = false,
                        description = "热力图价格轴。"
                    },
                    new
                    {
                        path = "liquidationLeverageData",
                        displayName = "热力点数据",
                        dataType = "array",
                        unit = "usd",
                        conditionSupported = false,
                        description = "热力图核心数据。"
                    },
                    new
                    {
                        path = "priceCandlesticks",
                        displayName = "价格K线",
                        dataType = "array",
                        unit = "price",
                        conditionSupported = false,
                        description = "叠加展示 K 线数据。"
                    }
                }
            });
        }
    }
}
