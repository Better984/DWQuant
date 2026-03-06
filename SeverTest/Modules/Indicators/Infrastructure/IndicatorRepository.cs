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
            await EnsureHistoryTableReadyAsync("coinglass.top_long_short_account_ratio", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.futures_footprint", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.grayscale_holdings", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.coin_unlock_list", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.coin_vesting", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.liquidation_heatmap_model1", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.exchange_assets", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.exchange_balance_list", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.exchange_balance_chart", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.hyperliquid_whale_alert", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.hyperliquid_whale_position", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.hyperliquid_position", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.hyperliquid_user_position", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.hyperliquid_wallet_position_distribution", ct).ConfigureAwait(false);
            await EnsureHistoryTableReadyAsync("coinglass.hyperliquid_wallet_pnl_distribution", ct).ConfigureAwait(false);
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
                    DisplayName = "现货 ETF 净流入",
                    Shape = "timeseries",
                    Unit = "usd",
                    Description = "CoinGlass 现货 ETF 每日净流入（支持 BTC/ETH/SOL/XRP）",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 365,
                    SourceEndpoint = "/api/etf/{asset}/flow-history",
                    DefaultScopeKey = "asset=BTC",
                    ConfigJson = BuildEtfFlowConfigJson(),
                    Enabled = true,
                    SortOrder = 20
                },
                new
                {
                    Code = "coinglass.top_long_short_account_ratio",
                    Provider = "coinglass",
                    DisplayName = "大户账户数多空比",
                    Shape = "timeseries",
                    Unit = "ratio",
                    Description = "CoinGlass 大户账户数多空比历史（当前支持 BTC / ETH，默认 Binance 15m）",
                    RefreshIntervalSec = 60,
                    TtlSec = 60,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/futures/top-long-short-account-ratio/history",
                    DefaultScopeKey = "asset=BTC",
                    ConfigJson = BuildTopLongShortAccountRatioConfigJson(),
                    Enabled = true,
                    SortOrder = 22
                },
                new
                {
                    Code = "coinglass.futures_footprint",
                    Provider = "coinglass",
                    DisplayName = "合约足迹图",
                    Shape = "heatmap",
                    Unit = "usd",
                    Description = "CoinGlass 合约足迹图历史（当前支持 BTC / ETH，默认 Binance 15m）",
                    RefreshIntervalSec = 60,
                    TtlSec = 60,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/futures/volume/footprint-history",
                    DefaultScopeKey = "asset=BTC",
                    ConfigJson = BuildFuturesFootprintConfigJson(),
                    Enabled = true,
                    SortOrder = 21
                },
                new
                {
                    Code = "coinglass.grayscale_holdings",
                    Provider = "coinglass",
                    DisplayName = "灰度持仓",
                    Shape = "table",
                    Unit = "usd",
                    Description = "CoinGlass 灰度投资资产持仓列表",
                    RefreshIntervalSec = 60,
                    TtlSec = 60,
                    HistoryRetentionDays = 180,
                    SourceEndpoint = "/api/grayscale/holdings-list",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildGrayscaleHoldingsConfigJson(),
                    Enabled = true,
                    SortOrder = 25
                },
                new
                {
                    Code = "coinglass.coin_unlock_list",
                    Provider = "coinglass",
                    DisplayName = "代币解锁列表",
                    Shape = "table",
                    Unit = "usd",
                    Description = "CoinGlass 代币解锁列表，展示即将到来的解锁计划与流通概况",
                    RefreshIntervalSec = 60 * 60 * 6,
                    TtlSec = 60 * 60 * 6,
                    HistoryRetentionDays = 365,
                    SourceEndpoint = "/api/coin/unlock-list",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildCoinUnlockListConfigJson(),
                    Enabled = true,
                    SortOrder = 26
                },
                new
                {
                    Code = "coinglass.coin_vesting",
                    Provider = "coinglass",
                    DisplayName = "代币解锁详情",
                    Shape = "table",
                    Unit = "usd",
                    Description = "CoinGlass 代币解锁详情，展示单个币种的锁仓分配与未来解锁计划",
                    RefreshIntervalSec = 60 * 60 * 6,
                    TtlSec = 60 * 60 * 6,
                    HistoryRetentionDays = 365,
                    SourceEndpoint = "/api/coin/vesting",
                    DefaultScopeKey = "symbol=HYPE",
                    ConfigJson = BuildCoinVestingConfigJson(),
                    Enabled = true,
                    SortOrder = 27
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
                },
                new
                {
                    Code = "coinglass.exchange_assets",
                    Provider = "coinglass",
                    DisplayName = "交易所资产明细",
                    Shape = "bar",
                    Unit = "usd",
                    Description = "CoinGlass 交易所资产明细，展示指定交易所的链上资产分布",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/exchange/assets",
                    DefaultScopeKey = "exchangeName=Binance",
                    ConfigJson = BuildExchangeAssetsConfigJson(),
                    Enabled = true,
                    SortOrder = 35
                },
                new
                {
                    Code = "coinglass.exchange_balance_list",
                    Provider = "coinglass",
                    DisplayName = "交易所余额排行",
                    Shape = "bar",
                    Unit = "coin",
                    Description = "CoinGlass 交易所余额排行，展示指定币种在各交易所的持有余额与变化",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/exchange/balance/list",
                    DefaultScopeKey = "symbol=BTC",
                    ConfigJson = BuildExchangeBalanceListConfigJson(),
                    Enabled = true,
                    SortOrder = 36
                },
                new
                {
                    Code = "coinglass.exchange_balance_chart",
                    Provider = "coinglass",
                    DisplayName = "交易所余额趋势",
                    Shape = "timeseries",
                    Unit = "coin",
                    Description = "CoinGlass 交易所余额趋势，展示指定币种在多交易所的余额时间序列",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 90,
                    SourceEndpoint = "/api/exchange/balance/chart",
                    DefaultScopeKey = "symbol=BTC",
                    ConfigJson = BuildExchangeBalanceChartConfigJson(),
                    Enabled = true,
                    SortOrder = 37
                },
                new
                {
                    Code = "coinglass.hyperliquid_whale_alert",
                    Provider = "coinglass",
                    DisplayName = "Hyperliquid 鲸鱼提醒",
                    Shape = "bar",
                    Unit = "usd",
                    Description = "CoinGlass Hyperliquid 鲸鱼提醒，展示最新大额仓位异动",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/hyperliquid/whale-alert",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildHyperliquidWhaleAlertConfigJson(),
                    Enabled = true,
                    SortOrder = 40
                },
                new
                {
                    Code = "coinglass.hyperliquid_whale_position",
                    Provider = "coinglass",
                    DisplayName = "Hyperliquid 鲸鱼持仓",
                    Shape = "bar",
                    Unit = "usd",
                    Description = "CoinGlass Hyperliquid 鲸鱼持仓，展示大户当前主要仓位排行",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/hyperliquid/whale-position",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildHyperliquidWhalePositionConfigJson(),
                    Enabled = true,
                    SortOrder = 41
                },
                new
                {
                    Code = "coinglass.hyperliquid_position",
                    Provider = "coinglass",
                    DisplayName = "Hyperliquid 持仓排行",
                    Shape = "bar",
                    Unit = "usd",
                    Description = "CoinGlass Hyperliquid 持仓排行，按币种查看市场主要持仓榜单",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/hyperliquid/position",
                    DefaultScopeKey = "symbol=BTC",
                    ConfigJson = BuildHyperliquidPositionConfigJson(),
                    Enabled = true,
                    SortOrder = 42
                },
                new
                {
                    Code = "coinglass.hyperliquid_user_position",
                    Provider = "coinglass",
                    DisplayName = "Hyperliquid 用户持仓",
                    Shape = "table",
                    Unit = "usd",
                    Description = "CoinGlass Hyperliquid 用户持仓，展示指定地址的账户权益与资产仓位",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/hyperliquid/user-position",
                    DefaultScopeKey = "userAddress=0xa5b0edf6b55128e0ddae8e51ac538c3188401d41",
                    ConfigJson = BuildHyperliquidUserPositionConfigJson(),
                    Enabled = true,
                    SortOrder = 43
                },
                new
                {
                    Code = "coinglass.hyperliquid_wallet_position_distribution",
                    Provider = "coinglass",
                    DisplayName = "Hyperliquid 钱包持仓分布",
                    Shape = "bar",
                    Unit = "usd",
                    Description = "CoinGlass Hyperliquid 钱包持仓分布，按资金层级统计多空持仓结构",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/hyperliquid/wallet/position-distribution",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildHyperliquidWalletPositionDistributionConfigJson(),
                    Enabled = true,
                    SortOrder = 44
                },
                new
                {
                    Code = "coinglass.hyperliquid_wallet_pnl_distribution",
                    Provider = "coinglass",
                    DisplayName = "Hyperliquid 钱包盈亏分布",
                    Shape = "bar",
                    Unit = "usd",
                    Description = "CoinGlass Hyperliquid 钱包盈亏分布，按盈亏分层统计多空持仓与地址表现",
                    RefreshIntervalSec = 20,
                    TtlSec = 20,
                    HistoryRetentionDays = 30,
                    SourceEndpoint = "/api/hyperliquid/wallet/pnl-distribution",
                    DefaultScopeKey = "global",
                    ConfigJson = BuildHyperliquidWalletPnlDistributionConfigJson(),
                    Enabled = true,
                    SortOrder = 45
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "asset",
                            displayName = "ETF资产",
                            defaultValue = "BTC",
                            options = new[] { "BTC", "ETH", "SOL", "XRP" },
                            description = "按资产切换现货 ETF 净流入数据。"
                        }
                    }
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
                        description = "当前资产当日 ETF 净流入，等价于 netFlowUsd。"
                    },
                    new
                    {
                        path = "asset",
                        displayName = "ETF资产代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前返回数据对应的 ETF 资产代码，例如 BTC / ETH / SOL / XRP。"
                    },
                    new
                    {
                        path = "netFlowUsd",
                        displayName = "当日净流入金额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前资产当日净流入金额（美元）。"
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

        private static string BuildTopLongShortAccountRatioConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "asset",
                            displayName = "币种",
                            defaultValue = "BTC",
                            options = new[] { "BTC", "ETH" },
                            description = "当前前端按币种切换大户账户数多空比，默认固定读取 Binance 的 15 分钟级别数据。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "多空比主值",
                        dataType = "number",
                        unit = "ratio",
                        conditionSupported = true,
                        description = "当前大户账户数多空比主值，等价于 latestRatio。"
                    },
                    new
                    {
                        path = "asset",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前返回数据对应的基础币种代码，例如 BTC / ETH。"
                    },
                    new
                    {
                        path = "exchange",
                        displayName = "交易所",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前多空比来源交易所。"
                    },
                    new
                    {
                        path = "symbol",
                        displayName = "交易对",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前多空比对应的合约交易对。"
                    },
                    new
                    {
                        path = "interval",
                        displayName = "时间粒度",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前多空比点位的时间粒度，当前默认固定为 15m。"
                    },
                    new
                    {
                        path = "latestRatio",
                        displayName = "大户账户数多空比",
                        dataType = "number",
                        unit = "ratio",
                        conditionSupported = true,
                        description = "大户账户多单占比 / 空单占比。"
                    },
                    new
                    {
                        path = "topAccountLongPercent",
                        displayName = "大户账户多单占比",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = true,
                        description = "当前时间点大户账户多单占比（%）。"
                    },
                    new
                    {
                        path = "topAccountShortPercent",
                        displayName = "大户账户空单占比",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = true,
                        description = "当前时间点大户账户空单占比（%）。"
                    }
                }
            });
        }

        private static string BuildFuturesFootprintConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "asset",
                            displayName = "币种",
                            defaultValue = "BTC",
                            description = "按币种切换合约足迹图，当前支持 BTC / ETH。"
                        },
                        new
                        {
                            name = "exchange",
                            displayName = "交易所",
                            defaultValue = "Binance",
                            description = "当前默认固定使用 Binance。"
                        },
                        new
                        {
                            name = "interval",
                            displayName = "时间粒度",
                            defaultValue = "15m",
                            description = "当前仅稳定输出 15 分钟级别数据。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "最新净主动买卖额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "最新一根足迹 K 线的净主动买卖额，等价于 latestNetDeltaUsd。"
                    },
                    new
                    {
                        path = "asset",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前返回数据对应的基础币种代码，例如 BTC / ETH。"
                    },
                    new
                    {
                        path = "exchange",
                        displayName = "交易所",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前足迹图来源交易所。"
                    },
                    new
                    {
                        path = "symbol",
                        displayName = "交易对",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前足迹图对应的合约交易对。"
                    },
                    new
                    {
                        path = "interval",
                        displayName = "时间粒度",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前足迹图点位时间粒度，当前默认固定为 15m。"
                    },
                    new
                    {
                        path = "latestNetDeltaUsd",
                        displayName = "净主动买卖额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "最新一根足迹 K 线内的主动买入额减主动卖出额。"
                    },
                    new
                    {
                        path = "latestBuyUsd",
                        displayName = "主动买入额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "最新一根足迹 K 线内各价格桶主动买入额汇总。"
                    },
                    new
                    {
                        path = "latestSellUsd",
                        displayName = "主动卖出额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "最新一根足迹 K 线内各价格桶主动卖出额汇总。"
                    },
                    new
                    {
                        path = "latestTotalTradeCount",
                        displayName = "最新成交笔数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "最新一根足迹 K 线内各价格桶主动成交笔数汇总。"
                    },
                    new
                    {
                        path = "latestBins[].deltaUsd",
                        displayName = "价格桶净主动买卖额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "最新一根足迹 K 线中每个价格桶的净主动买卖额。"
                    },
                    new
                    {
                        path = "series[].netDeltaUsd",
                        displayName = "时间序列净主动买卖额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "最近若干根足迹 K 线的净主动买卖额序列。"
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

        private static string BuildExchangeAssetsConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "exchangeName",
                            displayName = "交易所",
                            defaultValue = "Binance",
                            description = "按交易所切换链上资产明细。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "总资产市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前交易所展示资产的总市值。"
                    },
                    new
                    {
                        path = "exchangeName",
                        displayName = "交易所名称",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前返回资产明细对应的交易所名称。"
                    },
                    new
                    {
                        path = "totalBalanceUsd",
                        displayName = "总资产市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前交易所链上资产总市值。"
                    },
                    new
                    {
                        path = "items[].symbol",
                        displayName = "资产代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "交易所资产明细中的币种代码。"
                    },
                    new
                    {
                        path = "items[].balanceUsd",
                        displayName = "资产市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单项资产当前折算后的美元市值。"
                    }
                }
            });
        }

        private static string BuildExchangeBalanceListConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "symbol",
                            displayName = "币种",
                            defaultValue = "BTC",
                            description = "按币种切换交易所余额排行。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "总余额",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = true,
                        description = "当前币种在展示交易所中的总余额。"
                    },
                    new
                    {
                        path = "symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前余额排行对应的币种代码。"
                    },
                    new
                    {
                        path = "items[].exchangeName",
                        displayName = "交易所名称",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "余额排行中的交易所名称。"
                    },
                    new
                    {
                        path = "items[].balance",
                        displayName = "交易所余额",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = false,
                        description = "指定交易所当前币种余额。"
                    },
                    new
                    {
                        path = "items[].changePercent1d",
                        displayName = "1日变化百分比",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = false,
                        description = "指定交易所近 1 日余额变化百分比。"
                    }
                }
            });
        }

        private static string BuildExchangeBalanceChartConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "symbol",
                            displayName = "币种",
                            defaultValue = "BTC",
                            description = "按币种切换交易所余额趋势。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "最新总余额",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = true,
                        description = "当前展示交易所在最近时间点的余额总和。"
                    },
                    new
                    {
                        path = "symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前趋势图对应的币种代码。"
                    },
                    new
                    {
                        path = "timeList",
                        displayName = "时间轴",
                        dataType = "array",
                        unit = "ms",
                        conditionSupported = false,
                        description = "交易所余额趋势图的时间轴。"
                    },
                    new
                    {
                        path = "priceList",
                        displayName = "价格序列",
                        dataType = "array",
                        unit = "usd",
                        conditionSupported = false,
                        description = "对应时间轴上的现货价格序列。"
                    },
                    new
                    {
                        path = "series[].latestBalance",
                        displayName = "交易所最新余额",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = false,
                        description = "各交易所当前最新余额。"
                    }
                }
            });
        }

        private static string BuildHyperliquidWhaleAlertConfigJson()
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
                        displayName = "提醒总仓位市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前返回鲸鱼提醒列表的总仓位市值。"
                    },
                    new
                    {
                        path = "totalAlertCount",
                        displayName = "提醒数量",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前返回的鲸鱼提醒条数。"
                    },
                    new
                    {
                        path = "longAlertCount",
                        displayName = "多头提醒数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前列表中持仓数量为正的提醒数量。"
                    },
                    new
                    {
                        path = "shortAlertCount",
                        displayName = "空头提醒数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前列表中持仓数量为负的提醒数量。"
                    },
                    new
                    {
                        path = "items[].symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "鲸鱼提醒对应的交易币种。"
                    },
                    new
                    {
                        path = "items[].positionValueUsd",
                        displayName = "仓位市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单条鲸鱼提醒对应的仓位市值。"
                    }
                }
            });
        }

        private static string BuildHyperliquidWhalePositionConfigJson()
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
                        displayName = "鲸鱼仓位总市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前展示鲸鱼仓位列表的总仓位市值。"
                    },
                    new
                    {
                        path = "totalMarginBalance",
                        displayName = "总保证金",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前展示鲸鱼仓位列表的总保证金余额。"
                    },
                    new
                    {
                        path = "longCount",
                        displayName = "多头仓位数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前鲸鱼持仓列表中的多头仓位数量。"
                    },
                    new
                    {
                        path = "shortCount",
                        displayName = "空头仓位数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前鲸鱼持仓列表中的空头仓位数量。"
                    },
                    new
                    {
                        path = "items[].symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "鲸鱼持仓对应的交易币种。"
                    },
                    new
                    {
                        path = "items[].unrealizedPnl",
                        displayName = "未实现盈亏",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单条鲸鱼仓位的未实现盈亏。"
                    }
                }
            });
        }

        private static string BuildHyperliquidPositionConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "symbol",
                            displayName = "币种",
                            defaultValue = "BTC",
                            description = "按币种切换 Hyperliquid 持仓排行。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "持仓总市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前币种持仓排行列表的总仓位市值。"
                    },
                    new
                    {
                        path = "symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前持仓排行对应的币种代码。"
                    },
                    new
                    {
                        path = "totalPages",
                        displayName = "总页数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = false,
                        description = "上游接口返回的总页数。"
                    },
                    new
                    {
                        path = "items[].positionValueUsd",
                        displayName = "仓位市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单条持仓排行记录的仓位市值。"
                    },
                    new
                    {
                        path = "items[].unrealizedPnl",
                        displayName = "未实现盈亏",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单条持仓排行记录的未实现盈亏。"
                    }
                }
            });
        }

        private static string BuildHyperliquidUserPositionConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "userAddress",
                            displayName = "钱包地址",
                            defaultValue = "0xa5b0edf6b55128e0ddae8e51ac538c3188401d41",
                            description = "按地址切换 Hyperliquid 用户持仓。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "账户权益",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前地址的账户权益。"
                    },
                    new
                    {
                        path = "withdrawable",
                        displayName = "可提金额",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前地址可提取金额。"
                    },
                    new
                    {
                        path = "totalNotionalPosition",
                        displayName = "总名义仓位",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前地址的总名义仓位。"
                    },
                    new
                    {
                        path = "totalMarginUsed",
                        displayName = "总保证金占用",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前地址的总保证金占用。"
                    },
                    new
                    {
                        path = "assetPositions[].coin",
                        displayName = "持仓币种",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "用户持仓明细中的币种代码。"
                    },
                    new
                    {
                        path = "assetPositions[].unrealizedPnl",
                        displayName = "持仓未实现盈亏",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "用户持仓明细中的未实现盈亏。"
                    }
                }
            });
        }

        private static string BuildHyperliquidWalletPositionDistributionConfigJson()
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
                        displayName = "总持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "钱包持仓分布各层级合计持仓市值。"
                    },
                    new
                    {
                        path = "totalPositionAddressCount",
                        displayName = "持仓地址数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前分组下参与持仓的地址数量合计。"
                    },
                    new
                    {
                        path = "items[].groupName",
                        displayName = "分组名称",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "钱包持仓层级分组名称。"
                    },
                    new
                    {
                        path = "items[].biasScore",
                        displayName = "偏向评分",
                        dataType = "number",
                        unit = "score",
                        conditionSupported = false,
                        description = "分组多空偏向评分，数值越高越偏多。"
                    },
                    new
                    {
                        path = "items[].positionUsd",
                        displayName = "分组持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "分组总持仓市值。"
                    }
                }
            });
        }

        private static string BuildHyperliquidWalletPnlDistributionConfigJson()
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
                        displayName = "总持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "钱包盈亏分布各分层合计持仓市值。"
                    },
                    new
                    {
                        path = "totalPositionAddressCount",
                        displayName = "持仓地址数",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前盈亏分层下参与持仓的地址数量合计。"
                    },
                    new
                    {
                        path = "items[].groupName",
                        displayName = "分组名称",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "钱包盈亏分层名称。"
                    },
                    new
                    {
                        path = "items[].biasScore",
                        displayName = "偏向评分",
                        dataType = "number",
                        unit = "score",
                        conditionSupported = false,
                        description = "盈亏分层的多空偏向评分。"
                    },
                    new
                    {
                        path = "items[].positionUsd",
                        displayName = "分层持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "盈亏分层总持仓市值。"
                    }
                }
            });
        }

        private static string BuildCoinUnlockListConfigJson()
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
                        displayName = "解锁总价值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前展示币种列表的即将解锁总价值（美元）。"
                    },
                    new
                    {
                        path = "totalCoinCount",
                        displayName = "币种数量",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前代币解锁列表包含的币种数量。"
                    },
                    new
                    {
                        path = "totalMarketCap",
                        displayName = "总市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前展示币种列表的总市值。"
                    },
                    new
                    {
                        path = "totalNextUnlockValue",
                        displayName = "即将解锁总价值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前展示币种列表即将解锁价值汇总。"
                    },
                    new
                    {
                        path = "nextUnlockSymbol",
                        displayName = "最近解锁币种",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前列表中最近一笔解锁对应的币种代码。"
                    },
                    new
                    {
                        path = "nextUnlockTime",
                        displayName = "最近解锁时间",
                        dataType = "number",
                        unit = "ms",
                        conditionSupported = false,
                        description = "当前列表中最近一笔解锁的时间戳（毫秒）。"
                    },
                    new
                    {
                        path = "items[].symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "代币解锁列表中的币种代码。"
                    },
                    new
                    {
                        path = "items[].marketCap",
                        displayName = "市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单个币种当前市值。"
                    },
                    new
                    {
                        path = "items[].unlockedPercent",
                        displayName = "已解锁占比",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = false,
                        description = "单个币种当前已解锁供应占比。"
                    },
                    new
                    {
                        path = "items[].nextUnlockValue",
                        displayName = "下次解锁价值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单个币种下一次解锁对应的估算价值。"
                    }
                }
            });
        }

        private static string BuildCoinVestingConfigJson()
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
                scope = new
                {
                    keys = new object[]
                    {
                        new
                        {
                            name = "symbol",
                            displayName = "币种",
                            defaultValue = "HYPE",
                            description = "按币种查看锁仓分配与解锁计划详情。"
                        }
                    }
                },
                fields = new object[]
                {
                    new
                    {
                        path = "value",
                        displayName = "下次解锁价值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前币种下一次解锁对应的估算价值。"
                    },
                    new
                    {
                        path = "symbol",
                        displayName = "币种代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前详情对应的币种代码。"
                    },
                    new
                    {
                        path = "marketCap",
                        displayName = "市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前币种市值。"
                    },
                    new
                    {
                        path = "circulatingSupply",
                        displayName = "流通供应量",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = true,
                        description = "当前币种流通供应量。"
                    },
                    new
                    {
                        path = "totalSupply",
                        displayName = "总供应量",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = true,
                        description = "当前币种总供应量。"
                    },
                    new
                    {
                        path = "unlockedPercent",
                        displayName = "已解锁占比",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = true,
                        description = "当前币种已解锁供应占比。"
                    },
                    new
                    {
                        path = "lockedPercent",
                        displayName = "未解锁占比",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = true,
                        description = "当前币种未解锁供应占比。"
                    },
                    new
                    {
                        path = "nextUnlockTime",
                        displayName = "下次解锁时间",
                        dataType = "number",
                        unit = "ms",
                        conditionSupported = false,
                        description = "当前币种下一次解锁时间戳（毫秒）。"
                    },
                    new
                    {
                        path = "nextUnlockAmount",
                        displayName = "下次解锁数量",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = true,
                        description = "当前币种下一次解锁数量。"
                    },
                    new
                    {
                        path = "nextUnlockValue",
                        displayName = "下次解锁价值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前币种下一次解锁估算价值。"
                    },
                    new
                    {
                        path = "allocationItems[].label",
                        displayName = "分配项名称",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "锁仓分配明细中的分类名称。"
                    },
                    new
                    {
                        path = "scheduleItems[].unlockTime",
                        displayName = "解锁时间",
                        dataType = "number",
                        unit = "ms",
                        conditionSupported = false,
                        description = "未来或历史解锁计划中的时间点。"
                    },
                    new
                    {
                        path = "scheduleItems[].unlockValue",
                        displayName = "解锁价值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "未来或历史解锁计划中的单笔解锁价值。"
                    }
                }
            });
        }

        private static string BuildGrayscaleHoldingsConfigJson()
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
                        displayName = "总持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "灰度全部资产当前持仓总市值（美元）。"
                    },
                    new
                    {
                        path = "totalHoldingsUsd",
                        displayName = "总持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "灰度全部资产当前持仓总市值（美元）。"
                    },
                    new
                    {
                        path = "assetCount",
                        displayName = "持仓资产数量",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前灰度持仓覆盖的资产数量。"
                    },
                    new
                    {
                        path = "maxHoldingSymbol",
                        displayName = "最大持仓资产代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "当前持仓市值最大的资产代码。"
                    },
                    new
                    {
                        path = "maxHoldingUsd",
                        displayName = "最大持仓资产市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = true,
                        description = "当前持仓市值最大的资产对应美元金额。"
                    },
                    new
                    {
                        path = "premiumPositiveCount",
                        displayName = "正溢价资产数量",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前溢价率大于 0 的资产数量。"
                    },
                    new
                    {
                        path = "premiumNegativeCount",
                        displayName = "负溢价资产数量",
                        dataType = "number",
                        unit = "count",
                        conditionSupported = true,
                        description = "当前溢价率小于 0 的资产数量。"
                    },
                    new
                    {
                        path = "items[].symbol",
                        displayName = "持仓资产代码",
                        dataType = "string",
                        unit = (string?)null,
                        conditionSupported = false,
                        description = "灰度持仓明细中的资产代码。"
                    },
                    new
                    {
                        path = "items[].holdingsUsd",
                        displayName = "持仓市值",
                        dataType = "number",
                        unit = "usd",
                        conditionSupported = false,
                        description = "单个资产当前持仓总市值（美元）。"
                    },
                    new
                    {
                        path = "items[].premiumRate",
                        displayName = "溢价率",
                        dataType = "number",
                        unit = "percent",
                        conditionSupported = false,
                        description = "单个资产当前溢价率（%）。"
                    },
                    new
                    {
                        path = "items[].holdingsAmountChange1d",
                        displayName = "近1天持仓变化",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = false,
                        description = "单个资产近 1 天持仓数量变化。"
                    },
                    new
                    {
                        path = "items[].holdingsAmountChange7d",
                        displayName = "近7天持仓变化",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = false,
                        description = "单个资产近 7 天持仓数量变化。"
                    },
                    new
                    {
                        path = "items[].holdingsAmountChange30d",
                        displayName = "近30天持仓变化",
                        dataType = "number",
                        unit = "coin",
                        conditionSupported = false,
                        description = "单个资产近 30 天持仓数量变化。"
                    }
                }
            });
        }
    }
}
