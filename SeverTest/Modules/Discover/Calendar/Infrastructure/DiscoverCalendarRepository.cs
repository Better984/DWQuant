using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Discover.Domain;

namespace ServerTest.Modules.Discover.Infrastructure
{
    /// <summary>
    /// Discover 日历仓储（央行活动 / 财经事件 / 经济数据）。
    /// </summary>
    public sealed class DiscoverCalendarRepository
    {
        private const string CentralBankTableName = "coinglass_calendar_central_bank_activities";
        private const string FinancialEventsTableName = "coinglass_calendar_financial_events";
        private const string EconomicDataTableName = "coinglass_calendar_economic_data";

        private readonly IDbManager _db;

        public DiscoverCalendarRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 确保日历表结构存在。
        /// </summary>
        public Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS `coinglass_calendar_central_bank_activities` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `dedupe_key` CHAR(64) NOT NULL COMMENT '去重键：calendar_name + publish_timestamp + country_code',
  `calendar_name` VARCHAR(512) NOT NULL COMMENT '事件名称',
  `country_code` VARCHAR(32) NOT NULL COMMENT '国家代码',
  `country_name` VARCHAR(128) NOT NULL COMMENT '国家名称',
  `publish_timestamp` BIGINT NOT NULL COMMENT '发布时间（毫秒）',
  `importance_level` INT NOT NULL DEFAULT 0 COMMENT '重要等级',
  `has_exact_publish_time` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否有精确发布时间',
  `data_effect` VARCHAR(128) NULL COMMENT '数据影响（主要用于经济数据）',
  `forecast_value` VARCHAR(128) NULL COMMENT '预测值',
  `previous_value` VARCHAR(128) NULL COMMENT '前值',
  `revised_previous_value` VARCHAR(128) NULL COMMENT '修正前值',
  `published_value` VARCHAR(128) NULL COMMENT '公布值',
  `raw_payload_json` LONGTEXT NULL COMMENT '上游原始 JSON',
  `created_at` BIGINT NOT NULL COMMENT '入库时间（毫秒）',
  `updated_at` BIGINT NOT NULL COMMENT '更新时间（毫秒）',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_central_bank_dedupe_key` (`dedupe_key`),
  KEY `idx_central_bank_publish_time` (`publish_timestamp`),
  KEY `idx_central_bank_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='CoinGlass 央行活动（开发阶段聚合商数据）';

CREATE TABLE IF NOT EXISTS `coinglass_calendar_financial_events` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `dedupe_key` CHAR(64) NOT NULL COMMENT '去重键：calendar_name + publish_timestamp + country_code',
  `calendar_name` VARCHAR(512) NOT NULL COMMENT '事件名称',
  `country_code` VARCHAR(32) NOT NULL COMMENT '国家代码',
  `country_name` VARCHAR(128) NOT NULL COMMENT '国家名称',
  `publish_timestamp` BIGINT NOT NULL COMMENT '发布时间（毫秒）',
  `importance_level` INT NOT NULL DEFAULT 0 COMMENT '重要等级',
  `has_exact_publish_time` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否有精确发布时间',
  `data_effect` VARCHAR(128) NULL COMMENT '数据影响（主要用于经济数据）',
  `forecast_value` VARCHAR(128) NULL COMMENT '预测值',
  `previous_value` VARCHAR(128) NULL COMMENT '前值',
  `revised_previous_value` VARCHAR(128) NULL COMMENT '修正前值',
  `published_value` VARCHAR(128) NULL COMMENT '公布值',
  `raw_payload_json` LONGTEXT NULL COMMENT '上游原始 JSON',
  `created_at` BIGINT NOT NULL COMMENT '入库时间（毫秒）',
  `updated_at` BIGINT NOT NULL COMMENT '更新时间（毫秒）',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_financial_event_dedupe_key` (`dedupe_key`),
  KEY `idx_financial_event_publish_time` (`publish_timestamp`),
  KEY `idx_financial_event_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='CoinGlass 财经事件（开发阶段聚合商数据）';

CREATE TABLE IF NOT EXISTS `coinglass_calendar_economic_data` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `dedupe_key` CHAR(64) NOT NULL COMMENT '去重键：calendar_name + publish_timestamp + country_code',
  `calendar_name` VARCHAR(512) NOT NULL COMMENT '事件名称',
  `country_code` VARCHAR(32) NOT NULL COMMENT '国家代码',
  `country_name` VARCHAR(128) NOT NULL COMMENT '国家名称',
  `publish_timestamp` BIGINT NOT NULL COMMENT '发布时间（毫秒）',
  `importance_level` INT NOT NULL DEFAULT 0 COMMENT '重要等级',
  `has_exact_publish_time` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否有精确发布时间',
  `data_effect` VARCHAR(128) NULL COMMENT '数据影响（主要用于经济数据）',
  `forecast_value` VARCHAR(128) NULL COMMENT '预测值',
  `previous_value` VARCHAR(128) NULL COMMENT '前值',
  `revised_previous_value` VARCHAR(128) NULL COMMENT '修正前值',
  `published_value` VARCHAR(128) NULL COMMENT '公布值',
  `raw_payload_json` LONGTEXT NULL COMMENT '上游原始 JSON',
  `created_at` BIGINT NOT NULL COMMENT '入库时间（毫秒）',
  `updated_at` BIGINT NOT NULL COMMENT '更新时间（毫秒）',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_economic_data_dedupe_key` (`dedupe_key`),
  KEY `idx_economic_data_publish_time` (`publish_timestamp`),
  KEY `idx_economic_data_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='CoinGlass 经济数据（开发阶段聚合商数据）';
";
            return _db.ExecuteAsync(sql, null, null, ct);
        }

        /// <summary>
        /// 批量 UPSERT（dedupe_key 冲突时更新事件字段，保留原 id）。
        /// </summary>
        public Task<int> UpsertBatchAsync(
            DiscoverCalendarKind kind,
            IReadOnlyList<DiscoverCalendarItem> items,
            CancellationToken ct = default)
        {
            if (items.Count == 0)
            {
                return Task.FromResult(0);
            }

            var tableName = ResolveTableName(kind);
            var sql = $@"
INSERT INTO `{tableName}`
(
  dedupe_key,
  calendar_name,
  country_code,
  country_name,
  publish_timestamp,
  importance_level,
  has_exact_publish_time,
  data_effect,
  forecast_value,
  previous_value,
  revised_previous_value,
  published_value,
  raw_payload_json,
  created_at,
  updated_at
)
VALUES
(
  @DedupeKey,
  @CalendarName,
  @CountryCode,
  @CountryName,
  @PublishTimestamp,
  @ImportanceLevel,
  @HasExactPublishTime,
  @DataEffect,
  @ForecastValue,
  @PreviousValue,
  @RevisedPreviousValue,
  @PublishedValue,
  @RawPayloadJson,
  @CreatedAt,
  @UpdatedAt
)
ON DUPLICATE KEY UPDATE
  calendar_name = VALUES(calendar_name),
  country_code = VALUES(country_code),
  country_name = VALUES(country_name),
  publish_timestamp = VALUES(publish_timestamp),
  importance_level = VALUES(importance_level),
  has_exact_publish_time = VALUES(has_exact_publish_time),
  data_effect = VALUES(data_effect),
  forecast_value = VALUES(forecast_value),
  previous_value = VALUES(previous_value),
  revised_previous_value = VALUES(revised_previous_value),
  published_value = VALUES(published_value),
  raw_payload_json = VALUES(raw_payload_json),
  updated_at = VALUES(updated_at)
";

            return _db.ExecuteAsync(sql, items, null, ct);
        }

        /// <summary>
        /// 读取最新 N 条（按发布时间倒序，再按 ID 倒序）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverCalendarItem>> GetLatestAsync(
            DiscoverCalendarKind kind,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  calendar_name AS CalendarName,
  country_code AS CountryCode,
  country_name AS CountryName,
  publish_timestamp AS PublishTimestamp,
  importance_level AS ImportanceLevel,
  has_exact_publish_time AS HasExactPublishTime,
  data_effect AS DataEffect,
  forecast_value AS ForecastValue,
  previous_value AS PreviousValue,
  revised_previous_value AS RevisedPreviousValue,
  published_value AS PublishedValue,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM `{tableName}`
ORDER BY publish_timestamp DESC, id DESC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverCalendarItem>(
                    sql,
                    new { Limit = limit },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows.ToList();
        }

        /// <summary>
        /// 查询比某个 ID 更新的数据（按 ID 升序，便于前端顺序追加）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverCalendarItem>> GetAfterIdAsync(
            DiscoverCalendarKind kind,
            long latestId,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  calendar_name AS CalendarName,
  country_code AS CountryCode,
  country_name AS CountryName,
  publish_timestamp AS PublishTimestamp,
  importance_level AS ImportanceLevel,
  has_exact_publish_time AS HasExactPublishTime,
  data_effect AS DataEffect,
  forecast_value AS ForecastValue,
  previous_value AS PreviousValue,
  revised_previous_value AS RevisedPreviousValue,
  published_value AS PublishedValue,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM `{tableName}`
WHERE id > @LatestId
ORDER BY id ASC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverCalendarItem>(
                    sql,
                    new
                    {
                        LatestId = latestId,
                        Limit = limit
                    },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows.ToList();
        }

        /// <summary>
        /// 查询更早的数据（按 ID 倒序，便于下拉分页）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverCalendarItem>> GetBeforeIdAsync(
            DiscoverCalendarKind kind,
            long beforeId,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  calendar_name AS CalendarName,
  country_code AS CountryCode,
  country_name AS CountryName,
  publish_timestamp AS PublishTimestamp,
  importance_level AS ImportanceLevel,
  has_exact_publish_time AS HasExactPublishTime,
  data_effect AS DataEffect,
  forecast_value AS ForecastValue,
  previous_value AS PreviousValue,
  revised_previous_value AS RevisedPreviousValue,
  published_value AS PublishedValue,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM `{tableName}`
WHERE id < @BeforeId
ORDER BY id DESC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverCalendarItem>(
                    sql,
                    new
                    {
                        BeforeId = beforeId,
                        Limit = limit
                    },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows.ToList();
        }

        /// <summary>
        /// 按发布时间区间查询（默认倒序）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverCalendarItem>> GetByPublishRangeAsync(
            DiscoverCalendarKind kind,
            long? startTime,
            long? endTime,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  calendar_name AS CalendarName,
  country_code AS CountryCode,
  country_name AS CountryName,
  publish_timestamp AS PublishTimestamp,
  importance_level AS ImportanceLevel,
  has_exact_publish_time AS HasExactPublishTime,
  data_effect AS DataEffect,
  forecast_value AS ForecastValue,
  previous_value AS PreviousValue,
  revised_previous_value AS RevisedPreviousValue,
  published_value AS PublishedValue,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM `{tableName}`
WHERE (@StartTime IS NULL OR publish_timestamp >= @StartTime)
  AND (@EndTime IS NULL OR publish_timestamp <= @EndTime)
ORDER BY publish_timestamp DESC, id DESC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverCalendarItem>(
                    sql,
                    new
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Limit = limit
                    },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows.ToList();
        }

        /// <summary>
        /// 获取当前最大 ID。
        /// </summary>
        public async Task<long> GetMaxIdAsync(DiscoverCalendarKind kind, CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT COALESCE(MAX(id), 0)
FROM `{tableName}`
";
            return await _db.ExecuteScalarAsync<long>(sql, null, null, ct).ConfigureAwait(false);
        }

        private static string ResolveTableName(DiscoverCalendarKind kind)
        {
            return kind switch
            {
                DiscoverCalendarKind.CentralBankActivities => CentralBankTableName,
                DiscoverCalendarKind.FinancialEvents => FinancialEventsTableName,
                DiscoverCalendarKind.EconomicData => EconomicDataTableName,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知日历类型")
            };
        }
    }
}
