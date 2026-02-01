using System;
using System.Collections.Generic;
using System.Linq;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.StrategyRuntime.Domain;

namespace ServerTest.Modules.StrategyRuntime.Infrastructure
{
    /// <summary>
    /// 策略运行时数据访问，集中管理运行时加载与更新相关SQL。
    /// </summary>
    public sealed class StrategyRuntimeRepository
    {
        private readonly IDbManager _db;

        public StrategyRuntimeRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<StrategyRuntimeRow>> GetRunnableAsync(
            IReadOnlyCollection<string> states,
            CancellationToken ct = default)
        {
            if (states == null || states.Count == 0)
            {
                return Array.Empty<StrategyRuntimeRow>();
            }

            const string sql = @"
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
";

            var rows = await _db.QueryAsync<StrategyRuntimeRow>(sql, new { states }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<StrategyRuntimeRow>> GetByIdsAsync(
            IReadOnlyCollection<long> usIds,
            CancellationToken ct = default)
        {
            if (usIds == null || usIds.Count == 0)
            {
                return Array.Empty<StrategyRuntimeRow>();
            }

            const string sql = @"
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
WHERE us.us_id IN @usIds
";

            var rows = await _db.QueryAsync<StrategyRuntimeRow>(sql, new { usIds }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public Task<int> UpdateExchangeApiKeyAsync(
            long usId,
            long uid,
            long exchangeApiKeyId,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE user_strategy
SET exchange_api_key_id = @exchangeApiKeyId
WHERE us_id = @usId AND uid = @uid
";

            return _db.ExecuteAsync(sql, new { exchangeApiKeyId, usId, uid }, null, ct);
        }
    }

}
