using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Infrastructure.Repositories;
using ServerTest.Models;
using ServerTest.Models.Strategy;

namespace ServerTest.Services
{
    public sealed class StrategyRuntimeBootstrapHostedService : IHostedService
    {
        private static readonly string[] RunnableStates = { "running", "paused_open_position", "testing" };

        private readonly IDbManager _db;
        private readonly UserExchangeApiKeyRepository _apiKeyRepository;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly ILogger<StrategyRuntimeBootstrapHostedService> _logger;

        public StrategyRuntimeBootstrapHostedService(
            IDbManager db,
            UserExchangeApiKeyRepository apiKeyRepository,
            StrategyJsonLoader strategyLoader,
            RealTimeStrategyEngine strategyEngine,
            ILogger<StrategyRuntimeBootstrapHostedService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
            _strategyLoader = strategyLoader ?? throw new ArgumentNullException(nameof(strategyLoader));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("从user_strategy引导运行时策略...");

            try
            {
                var rows = await _db.QueryAsync<RuntimeStrategyRow>(@"
SELECT
  us.us_id AS UsId,
  us.uid AS Uid,
  us.def_id AS DefId,
  us.pinned_version_id AS PinnedVersionId,
  us.alias_name AS AliasName,
  us.description AS Description,
  us.state AS State,
  us.visibility AS Visibility,
  us.share_code AS ShareCode,
  us.price_usdt AS PriceUsdt,
  us.source_type AS SourceType,
  us.source_ref AS SourceRef,
  us.exchange_api_key_id AS ExchangeApiKeyId,
  us.updated_at AS UpdatedAt,
  sd.name AS DefName,
  sd.description AS DefDescription,
  sd.def_type AS DefType,
  sd.creator_uid AS CreatorUid,
  sv.version_id AS VersionId,
  sv.version_no AS VersionNo,
  sv.config_json AS ConfigJson,
  sv.content_hash AS ContentHash,
  sv.artifact_uri AS ArtifactUri
FROM user_strategy us
JOIN strategy_def sd ON sd.def_id = us.def_id
JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE us.state IN @states
",
                    new { states = RunnableStates },
                    ct: cancellationToken).ConfigureAwait(false);

                var loaded = 0;
                var skipped = 0;
                foreach (var row in rows)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(row.ConfigJson))
                    {
                        _logger.LogWarning("跳过运行时策略 {UsId}: config_json为空", row.UsId);
                        skipped++;
                        continue;
                    }

                    var config = _strategyLoader.ParseConfig(row.ConfigJson);
                    if (config == null)
                    {
                        _logger.LogWarning("跳过运行时策略 {UsId}: config_json解析失败", row.UsId);
                        skipped++;
                        continue;
                    }

                    var normalizedExchange = MarketDataKeyNormalizer.NormalizeExchange(config.Trade?.Exchange ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(normalizedExchange))
                    {
                        _logger.LogWarning("跳过运行时策略 {UsId}: 交易所配置无效", row.UsId);
                        skipped++;
                        continue;
                    }

                    var exchangeApiKeyId = row.ExchangeApiKeyId;
                    UserExchangeApiKeyRecord? apiKey = null;
                    if (exchangeApiKeyId.HasValue && exchangeApiKeyId.Value > 0)
                    {
                        apiKey = await _apiKeyRepository.GetByIdAsync(exchangeApiKeyId.Value, row.Uid, cancellationToken).ConfigureAwait(false);
                        if (apiKey == null)
                        {
                            exchangeApiKeyId = null;
                        }
                    }

                    if (!exchangeApiKeyId.HasValue)
                    {
                        apiKey = await _apiKeyRepository.GetLatestByUidAsync(row.Uid, normalizedExchange, cancellationToken).ConfigureAwait(false);
                        if (apiKey != null)
                        {
                            exchangeApiKeyId = apiKey.Id;
                            await _db.ExecuteAsync(@"
UPDATE user_strategy
SET exchange_api_key_id = @exchangeApiKeyId
WHERE us_id = @usId AND uid = @uid
", new { exchangeApiKeyId, usId = row.UsId, uid = row.Uid }, ct: cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (!exchangeApiKeyId.HasValue)
                    {
                        _logger.LogWarning("跳过运行时策略 {UsId}: 未绑定交易所API", row.UsId);
                        skipped++;
                        continue;
                    }

                    apiKey ??= await _apiKeyRepository.GetByIdAsync(exchangeApiKeyId.Value, row.Uid, cancellationToken).ConfigureAwait(false);
                    if (apiKey == null)
                    {
                        _logger.LogWarning("跳过运行时策略 {UsId}: 绑定的API key无效", row.UsId);
                        skipped++;
                        continue;
                    }

                    var apiExchange = MarketDataKeyNormalizer.NormalizeExchange(apiKey.ExchangeType);
                    if (!string.IsNullOrWhiteSpace(apiExchange)
                        && !string.Equals(apiExchange, normalizedExchange, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "跳过运行时策略 {UsId}: 绑定API交易所不一致 strategy={StrategyExchange} api={ApiExchange}",
                            row.UsId,
                            normalizedExchange,
                            apiExchange);
                        skipped++;
                        continue;
                    }

                    row.ExchangeApiKeyId = exchangeApiKeyId;

                    var document = BuildDocument(row, config);
                    var runtimeStrategy = _strategyLoader.LoadFromDocument(document);
                    if (runtimeStrategy == null)
                    {
                        _logger.LogWarning("跳过运行时策略 {UsId}: 文档加载失败", row.UsId);
                        skipped++;
                        continue;
                    }

                    _strategyEngine.UpsertStrategy(runtimeStrategy);
                    loaded++;
                }

                _logger.LogInformation("运行时策略引导完成。已加载={Loaded}, 已跳过={Skipped}", loaded, skipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "运行时策略引导失败");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static StrategyDocument BuildDocument(RuntimeStrategyRow row, StrategyConfig config)
        {
            return new StrategyDocument
            {
                UserStrategy = new StrategyUserStrategy
                {
                    UsId = row.UsId,
                    Uid = row.Uid,
                    DefId = row.DefId,
                    AliasName = row.AliasName ?? string.Empty,
                    Description = row.Description ?? string.Empty,
                    State = row.State ?? string.Empty,
                    Visibility = row.Visibility ?? "private",
                    ShareCode = row.ShareCode,
                    PriceUsdt = row.PriceUsdt,
                    Source = new StrategySourceRef
                    {
                        Type = row.SourceType ?? "custom",
                        Ref = row.SourceRef
                    },
                    PinnedVersionId = row.PinnedVersionId,
                    ExchangeApiKeyId = row.ExchangeApiKeyId,
                    UpdatedAt = row.UpdatedAt.HasValue
                        ? new DateTimeOffset(row.UpdatedAt.Value, TimeSpan.Zero)
                        : DateTimeOffset.UtcNow
                },
                Definition = new StrategyDefinition
                {
                    DefId = row.DefId,
                    DefType = row.DefType ?? "custom",
                    Name = row.DefName ?? string.Empty,
                    Description = row.DefDescription ?? string.Empty,
                    CreatorUid = row.CreatorUid
                },
                Version = new StrategyVersion
                {
                    VersionId = row.VersionId,
                    VersionNo = row.VersionNo,
                    ContentHash = row.ContentHash ?? string.Empty,
                    ArtifactUri = row.ArtifactUri,
                    ConfigJson = config
                }
            };
        }

        private sealed class RuntimeStrategyRow
        {
            public long UsId { get; set; }
            public long Uid { get; set; }
            public long DefId { get; set; }
            public long PinnedVersionId { get; set; }
            public string? AliasName { get; set; }
            public string? Description { get; set; }
            public string? State { get; set; }
            public string? Visibility { get; set; }
            public string? ShareCode { get; set; }
            public decimal PriceUsdt { get; set; }
            public string? SourceType { get; set; }
            public string? SourceRef { get; set; }
            public long? ExchangeApiKeyId { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public string? DefName { get; set; }
            public string? DefDescription { get; set; }
            public string? DefType { get; set; }
            public long CreatorUid { get; set; }
            public long VersionId { get; set; }
            public int VersionNo { get; set; }
            public string? ConfigJson { get; set; }
            public string? ContentHash { get; set; }
            public string? ArtifactUri { get; set; }
        }
    }
}
