using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Discover.Domain;

namespace ServerTest.Modules.Discover.Infrastructure
{
    /// <summary>
    /// 发现页资讯仓储（新闻 + 快讯）。
    /// </summary>
    public sealed class DiscoverFeedRepository
    {
        private const string ArticleTableName = "coinglass_news_articles";
        private const string NewsflashTableName = "coinglass_news_flashes";

        private readonly IDbManager _db;

        public DiscoverFeedRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 确保资讯表结构存在。
        /// </summary>
        public Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS `coinglass_news_articles` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `dedupe_key` CHAR(64) NOT NULL COMMENT '去重键：title + release_time + source',
  `title` VARCHAR(512) NOT NULL COMMENT '标题',
  `summary` TEXT NULL COMMENT '摘要（纯文本）',
  `content_html` LONGTEXT NULL COMMENT '正文（HTML）',
  `source_name` VARCHAR(128) NOT NULL COMMENT '来源名称',
  `source_logo` VARCHAR(512) NULL COMMENT '来源 logo',
  `picture_url` VARCHAR(512) NULL COMMENT '配图',
  `release_time` BIGINT NOT NULL COMMENT '发布时间（毫秒）',
  `raw_payload_json` LONGTEXT NULL COMMENT '上游原始 JSON',
  `created_at` BIGINT NOT NULL COMMENT '入库时间（毫秒）',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_article_dedupe_key` (`dedupe_key`),
  KEY `idx_article_release_time` (`release_time`),
  KEY `idx_article_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='CoinGlass 新闻（开发阶段聚合商数据）';

CREATE TABLE IF NOT EXISTS `coinglass_news_flashes` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `dedupe_key` CHAR(64) NOT NULL COMMENT '去重键：title + release_time + source',
  `title` VARCHAR(512) NOT NULL COMMENT '标题',
  `summary` TEXT NULL COMMENT '摘要（纯文本）',
  `content_html` LONGTEXT NULL COMMENT '正文（HTML）',
  `source_name` VARCHAR(128) NOT NULL COMMENT '来源名称',
  `source_logo` VARCHAR(512) NULL COMMENT '来源 logo',
  `picture_url` VARCHAR(512) NULL COMMENT '配图',
  `release_time` BIGINT NOT NULL COMMENT '发布时间（毫秒）',
  `raw_payload_json` LONGTEXT NULL COMMENT '上游原始 JSON',
  `created_at` BIGINT NOT NULL COMMENT '入库时间（毫秒）',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_flash_dedupe_key` (`dedupe_key`),
  KEY `idx_flash_release_time` (`release_time`),
  KEY `idx_flash_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='CoinGlass 快讯（开发阶段聚合商数据）';
";
            return _db.ExecuteAsync(sql, null, null, ct);
        }

        /// <summary>
        /// 批量插入（重复 dedupe_key 自动忽略）。
        /// </summary>
        public Task<int> InsertIgnoreBatchAsync(
            DiscoverFeedKind kind,
            IReadOnlyList<DiscoverFeedItem> items,
            CancellationToken ct = default)
        {
            if (items.Count == 0)
            {
                return Task.FromResult(0);
            }

            var tableName = ResolveTableName(kind);
            var sql = $@"
INSERT IGNORE INTO `{tableName}`
(
  dedupe_key,
  title,
  summary,
  content_html,
  source_name,
  source_logo,
  picture_url,
  release_time,
  raw_payload_json,
  created_at
)
VALUES
(
  @DedupeKey,
  @Title,
  @Summary,
  @ContentHtml,
  @SourceName,
  @SourceLogo,
  @PictureUrl,
  @ReleaseTime,
  @RawPayloadJson,
  @CreatedAt
)";

            return _db.ExecuteAsync(sql, items, null, ct);
        }

        /// <summary>
        /// 读取最新 N 条（按发布时间倒序，发布时间相同再按 ID 倒序）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverFeedItem>> GetLatestAsync(
            DiscoverFeedKind kind,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  title AS Title,
  summary AS Summary,
  content_html AS ContentHtml,
  source_name AS SourceName,
  source_logo AS SourceLogo,
  picture_url AS PictureUrl,
  release_time AS ReleaseTime,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt
FROM `{tableName}`
ORDER BY release_time DESC, id DESC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverFeedItem>(
                    sql,
                    new { Limit = limit },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows.ToList();
        }

        /// <summary>
        /// 查询比某个游标更新的数据（按发布时间升序，发布时间相同再按 ID 升序，便于前端顺序追加）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverFeedItem>> GetAfterIdAsync(
            DiscoverFeedKind kind,
            long latestId,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  title AS Title,
  summary AS Summary,
  content_html AS ContentHtml,
  source_name AS SourceName,
  source_logo AS SourceLogo,
  picture_url AS PictureUrl,
  release_time AS ReleaseTime,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt
FROM `{tableName}`
WHERE (
  release_time > (SELECT release_time FROM `{tableName}` WHERE id = @LatestId LIMIT 1)
  OR (
    release_time = (SELECT release_time FROM `{tableName}` WHERE id = @LatestId LIMIT 1)
    AND id > @LatestId
  )
)
ORDER BY release_time ASC, id ASC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverFeedItem>(
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
        /// 查询更早的数据（按发布时间倒序，发布时间相同再按 ID 倒序，便于下拉分页）。
        /// </summary>
        public async Task<IReadOnlyList<DiscoverFeedItem>> GetBeforeIdAsync(
            DiscoverFeedKind kind,
            long beforeId,
            int limit,
            CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT
  id AS Id,
  dedupe_key AS DedupeKey,
  title AS Title,
  summary AS Summary,
  content_html AS ContentHtml,
  source_name AS SourceName,
  source_logo AS SourceLogo,
  picture_url AS PictureUrl,
  release_time AS ReleaseTime,
  raw_payload_json AS RawPayloadJson,
  created_at AS CreatedAt
FROM `{tableName}`
WHERE (
  release_time < (SELECT release_time FROM `{tableName}` WHERE id = @BeforeId LIMIT 1)
  OR (
    release_time = (SELECT release_time FROM `{tableName}` WHERE id = @BeforeId LIMIT 1)
    AND id < @BeforeId
  )
)
ORDER BY release_time DESC, id DESC
LIMIT @Limit
";

            var rows = await _db.QueryAsync<DiscoverFeedItem>(
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
        /// 获取当前最大 ID。
        /// </summary>
        public async Task<long> GetMaxIdAsync(DiscoverFeedKind kind, CancellationToken ct = default)
        {
            var tableName = ResolveTableName(kind);
            var sql = $@"
SELECT COALESCE(MAX(id), 0)
FROM `{tableName}`
";
            return await _db.ExecuteScalarAsync<long>(sql, null, null, ct).ConfigureAwait(false);
        }

        private static string ResolveTableName(DiscoverFeedKind kind)
        {
            return kind switch
            {
                DiscoverFeedKind.Article => ArticleTableName,
                DiscoverFeedKind.Newsflash => NewsflashTableName,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知资讯类型")
            };
        }
    }
}
