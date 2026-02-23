using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Modules.Indicators.Infrastructure;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Application
{
    /// <summary>
    /// 指标查询与刷新服务。
    /// </summary>
    public sealed class IndicatorQueryService
    {
        private readonly IndicatorRepository _repository;
        private readonly IndicatorRegistry _registry;
        private readonly IndicatorCacheStore _cacheStore;
        private readonly IndicatorFrameworkOptions _options;
        private readonly ILogger<IndicatorQueryService> _logger;

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, long> _historyCleanupMap = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _initializeLock = new(1, 1);
        private volatile bool _initialized;

        public IndicatorQueryService(
            IndicatorRepository repository,
            IndicatorRegistry registry,
            IndicatorCacheStore cacheStore,
            IOptions<IndicatorFrameworkOptions> options,
            ILogger<IndicatorQueryService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
            _options = options?.Value ?? new IndicatorFrameworkOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureInitializedAsync(CancellationToken ct)
        {
            if (_initialized)
            {
                return;
            }

            await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_initialized)
                {
                    return;
                }

                if (_options.AutoCreateSchema)
                {
                    await _repository.EnsureSchemaAsync(ct).ConfigureAwait(false);
                }

                if (_options.AutoSeedDefinitions)
                {
                    await _repository.EnsureSeedDefinitionsAsync(ct).ConfigureAwait(false);
                }

                await _registry.ForceReloadAsync(ct).ConfigureAwait(false);
                _initialized = true;
                _logger.LogInformation("指标框架初始化完成");
            }
            finally
            {
                _initializeLock.Release();
            }
        }

        public async Task<IReadOnlyList<IndicatorDefinition>> GetDefinitionsAsync(bool includeDisabled, CancellationToken ct)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await _registry.GetDefinitionsAsync(includeDisabled, ct).ConfigureAwait(false);
        }

        public async Task<IndicatorQueryResult> GetLatestAsync(
            string code,
            IDictionary<string, string>? scope,
            bool allowStale,
            bool forceRefresh,
            CancellationToken ct)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var definition = await GetEnabledDefinitionAsync(code, ct).ConfigureAwait(false);
            var scopeKey = IndicatorScopeKey.Build(scope, definition.DefaultScopeKey);

            if (forceRefresh)
            {
                return await RefreshNowCoreAsync(definition, scopeKey, ct, force: true).ConfigureAwait(false);
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 1) 优先读取 L1/L2 缓存
            var cached = await _cacheStore.GetAsync(definition.Code, scopeKey, ct).ConfigureAwait(false);
            if (cached != null)
            {
                if (IsFresh(cached, nowMs))
                {
                    return BuildResult(definition, cached, stale: false, origin: "cache");
                }

                if (allowStale && IsWithinStaleTolerance(cached, nowMs))
                {
                    TriggerRefreshInBackground(definition, scopeKey);
                    return BuildResult(definition, cached, stale: true, origin: "cache");
                }
            }

            // 2) 再读取数据库快照
            var dbSnapshot = await _repository.GetSnapshotAsync(definition.Code, scopeKey, ct).ConfigureAwait(false);
            if (dbSnapshot != null)
            {
                await _cacheStore.SetAsync(dbSnapshot, ct).ConfigureAwait(false);

                if (IsFresh(dbSnapshot, nowMs))
                {
                    return BuildResult(definition, dbSnapshot, stale: false, origin: "database");
                }

                if (allowStale && IsWithinStaleTolerance(dbSnapshot, nowMs))
                {
                    TriggerRefreshInBackground(definition, scopeKey);
                    return BuildResult(definition, dbSnapshot, stale: true, origin: "database");
                }
            }

            // 3) 最后同步拉取数据源
            return await RefreshNowCoreAsync(definition, scopeKey, ct, force: true).ConfigureAwait(false);
        }

        public async Task<IndicatorQueryResult> RefreshNowAsync(
            string code,
            IDictionary<string, string>? scope,
            CancellationToken ct)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            var definition = await GetEnabledDefinitionAsync(code, ct).ConfigureAwait(false);
            var scopeKey = IndicatorScopeKey.Build(scope, definition.DefaultScopeKey);
            return await RefreshNowCoreAsync(definition, scopeKey, ct, force: true).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<IndicatorQueryResult>> GetBatchLatestAsync(
            IReadOnlyList<string> codes,
            IDictionary<string, string>? scope,
            bool allowStale,
            CancellationToken ct)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var normalized = codes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<IndicatorQueryResult>(normalized.Count);
            foreach (var code in normalized)
            {
                var result = await GetLatestAsync(code, scope, allowStale, forceRefresh: false, ct).ConfigureAwait(false);
                results.Add(result);
            }

            return results;
        }

        public async Task<IndicatorHistoryQueryResult> GetHistoryAsync(
            string code,
            IDictionary<string, string>? scope,
            long? startMs,
            long? endMs,
            int limit,
            CancellationToken ct)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            var definition = await GetEnabledDefinitionAsync(code, ct).ConfigureAwait(false);
            var scopeKey = IndicatorScopeKey.Build(scope, definition.DefaultScopeKey);
            var safeLimit = Math.Clamp(limit, 1, _options.MaxHistoryQueryPoints);

            var points = await _repository
                .GetHistoryAsync(definition.Code, scopeKey, startMs, endMs, safeLimit, ct)
                .ConfigureAwait(false);

            if (points.Count == 0)
            {
                // 历史为空时尝试触发一次刷新，避免前端首次打开无数据。
                try
                {
                    await RefreshNowCoreAsync(definition, scopeKey, ct, force: false).ConfigureAwait(false);
                    points = await _repository
                        .GetHistoryAsync(definition.Code, scopeKey, startMs, endMs, safeLimit, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "历史为空且补拉失败: code={Code}, scope={ScopeKey}", definition.Code, scopeKey);
                }
            }

            return new IndicatorHistoryQueryResult
            {
                Definition = definition,
                ScopeKey = scopeKey,
                Points = points
            };
        }

        public static JsonElement ParsePayload(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return JsonDocument.Parse("{}").RootElement.Clone();
            }

            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.Clone();
        }

        private async Task<IndicatorQueryResult> RefreshNowCoreAsync(
            IndicatorDefinition definition,
            string scopeKey,
            CancellationToken ct,
            bool force)
        {
            var lockKey = $"{definition.Code}:{scopeKey}";
            var refreshLock = _refreshLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await refreshLock.WaitAsync(ct).ConfigureAwait(false);

            var startedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (!force)
                {
                    var cached = await _cacheStore.GetAsync(definition.Code, scopeKey, ct).ConfigureAwait(false);
                    if (cached != null && IsFresh(cached, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                    {
                        return BuildResult(definition, cached, stale: false, origin: "cache");
                    }
                }

                var collector = _registry.ResolveCollector(definition);
                var collectResult = await collector.CollectAsync(definition, scopeKey, ct).ConfigureAwait(false);

                var fetchedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var sourceTs = collectResult.SourceTs > 0 ? collectResult.SourceTs : fetchedAtMs;
                var snapshot = new IndicatorSnapshot
                {
                    Code = definition.Code,
                    ScopeKey = scopeKey,
                    Provider = definition.Provider,
                    Shape = definition.Shape,
                    Unit = definition.Unit,
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    PayloadJson = collectResult.PayloadJson,
                    SourceTs = sourceTs,
                    FetchedAt = fetchedAtMs,
                    ExpireAt = fetchedAtMs + definition.TtlSec * 1000L
                };

                await _repository.UpsertSnapshotAsync(snapshot, ct).ConfigureAwait(false);

                var history = collectResult.History.Count > 0
                    ? collectResult.History
                    : new[]
                    {
                        new IndicatorHistoryPoint
                        {
                            SourceTs = sourceTs,
                            PayloadJson = collectResult.PayloadJson
                        }
                    };
                await _repository.UpsertHistoryBatchAsync(definition.Code, scopeKey, history, ct).ConfigureAwait(false);
                await _cacheStore.SetAsync(snapshot, ct).ConfigureAwait(false);

                stopwatch.Stop();
                var finishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await _repository.InsertRefreshLogAsync(
                    definition.Code,
                    scopeKey,
                    status: "success",
                    message: "ok",
                    latencyMs: (int)stopwatch.ElapsedMilliseconds,
                    startedAt: startedAtMs,
                    finishedAt: finishedAtMs,
                    ct: ct).ConfigureAwait(false);

                await CleanupHistoryIfNeededAsync(definition, scopeKey, finishedAtMs, ct).ConfigureAwait(false);

                return BuildResult(definition, snapshot, stale: false, origin: "provider");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var finishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await _repository.InsertRefreshLogAsync(
                    definition.Code,
                    scopeKey,
                    status: "failed",
                    message: Truncate(ex.Message, 250),
                    latencyMs: (int)stopwatch.ElapsedMilliseconds,
                    startedAt: startedAtMs,
                    finishedAt: finishedAtMs,
                    ct: ct).ConfigureAwait(false);

                _logger.LogError(ex, "刷新指标失败: code={Code}, scope={ScopeKey}", definition.Code, scopeKey);
                throw;
            }
            finally
            {
                refreshLock.Release();
            }
        }

        private async Task<IndicatorDefinition> GetEnabledDefinitionAsync(string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("指标 code 不能为空", nameof(code));
            }

            var definition = await _registry.GetDefinitionAsync(code.Trim(), ct).ConfigureAwait(false);
            if (definition == null)
            {
                throw new KeyNotFoundException($"未找到指标定义: {code}");
            }

            if (!definition.Enabled)
            {
                throw new InvalidOperationException($"指标已禁用: {code}");
            }

            return definition;
        }

        private static bool IsFresh(IndicatorSnapshot snapshot, long nowMs)
        {
            return snapshot.ExpireAt > nowMs;
        }

        private bool IsWithinStaleTolerance(IndicatorSnapshot snapshot, long nowMs)
        {
            return nowMs <= snapshot.ExpireAt + _options.StaleToleranceSeconds * 1000L;
        }

        private static IndicatorQueryResult BuildResult(
            IndicatorDefinition definition,
            IndicatorSnapshot snapshot,
            bool stale,
            string origin)
        {
            return new IndicatorQueryResult
            {
                Definition = definition,
                Snapshot = snapshot,
                Stale = stale,
                Origin = origin
            };
        }

        private void TriggerRefreshInBackground(IndicatorDefinition definition, string scopeKey)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshNowCoreAsync(definition, scopeKey, CancellationToken.None, force: false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "后台异步刷新失败: code={Code}, scope={ScopeKey}", definition.Code, scopeKey);
                }
            });
        }

        private async Task CleanupHistoryIfNeededAsync(
            IndicatorDefinition definition,
            string scopeKey,
            long nowMs,
            CancellationToken ct)
        {
            if (_options.HistoryCleanupIntervalMinutes <= 0 || definition.HistoryRetentionDays <= 0)
            {
                return;
            }

            var key = $"{definition.Code}:{scopeKey}";
            if (_historyCleanupMap.TryGetValue(key, out var lastCleanupMs))
            {
                var minIntervalMs = _options.HistoryCleanupIntervalMinutes * 60_000L;
                if (nowMs - lastCleanupMs < minIntervalMs)
                {
                    return;
                }
            }

            var cutoffMs = nowMs - definition.HistoryRetentionDays * 86_400_000L;
            var deletedCount = await _repository.CleanupHistoryAsync(definition.Code, scopeKey, cutoffMs, ct).ConfigureAwait(false);
            _historyCleanupMap[key] = nowMs;

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "历史数据清理完成: code={Code}, scope={ScopeKey}, deleted={Deleted}",
                    definition.Code,
                    scopeKey,
                    deletedCount);
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength];
        }
    }
}
