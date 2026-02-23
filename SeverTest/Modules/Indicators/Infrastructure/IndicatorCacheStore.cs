using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Collections.Concurrent;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// 指标缓存存储：
    /// - L1：进程内内存（低延迟）
    /// - L2：Redis（跨实例共享）
    /// </summary>
    public sealed class IndicatorCacheStore
    {
        private readonly IDistributedCache _distributedCache;
        private readonly IndicatorFrameworkOptions _options;
        private readonly ILogger<IndicatorCacheStore> _logger;
        private readonly ConcurrentDictionary<string, IndicatorSnapshot> _memoryCache = new(StringComparer.Ordinal);

        public IndicatorCacheStore(
            IDistributedCache distributedCache,
            IOptions<IndicatorFrameworkOptions> options,
            ILogger<IndicatorCacheStore> logger)
        {
            _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
            _options = options?.Value ?? new IndicatorFrameworkOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IndicatorSnapshot?> GetAsync(string code, string scopeKey, CancellationToken ct = default)
        {
            var cacheKey = BuildCacheKey(code, scopeKey);

            if (_memoryCache.TryGetValue(cacheKey, out var memorySnapshot))
            {
                return Clone(memorySnapshot);
            }

            try
            {
                var redisText = await _distributedCache.GetStringAsync(cacheKey, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(redisText))
                {
                    return null;
                }

                var redisSnapshot = ProtocolJson.Deserialize<IndicatorSnapshot>(redisText);
                if (redisSnapshot == null)
                {
                    return null;
                }

                _memoryCache[cacheKey] = Clone(redisSnapshot);
                return Clone(redisSnapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取指标缓存失败，降级继续: code={Code}, scope={ScopeKey}", code, scopeKey);
                return null;
            }
        }

        public async Task SetAsync(IndicatorSnapshot snapshot, CancellationToken ct = default)
        {
            var cacheKey = BuildCacheKey(snapshot.Code, snapshot.ScopeKey);
            _memoryCache[cacheKey] = Clone(snapshot);

            try
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var freshSeconds = Math.Max(1, (snapshot.ExpireAt - nowMs) / 1000);
                var keepSeconds = Math.Max(
                    _options.RedisCacheSeconds,
                    (int)freshSeconds + Math.Max(0, _options.StaleToleranceSeconds));

                var redisOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(keepSeconds)
                };

                await _distributedCache.SetStringAsync(
                    cacheKey,
                    ProtocolJson.Serialize(snapshot),
                    redisOptions,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入指标 Redis 缓存失败，已保留内存缓存: code={Code}, scope={ScopeKey}", snapshot.Code, snapshot.ScopeKey);
            }
        }

        private static string BuildCacheKey(string code, string scopeKey)
        {
            return $"indicator:snapshot:{code}:{scopeKey}";
        }

        private static IndicatorSnapshot Clone(IndicatorSnapshot snapshot)
        {
            return new IndicatorSnapshot
            {
                Code = snapshot.Code,
                ScopeKey = snapshot.ScopeKey,
                Provider = snapshot.Provider,
                Shape = snapshot.Shape,
                Unit = snapshot.Unit,
                DisplayName = snapshot.DisplayName,
                Description = snapshot.Description,
                PayloadJson = snapshot.PayloadJson,
                SourceTs = snapshot.SourceTs,
                FetchedAt = snapshot.FetchedAt,
                ExpireAt = snapshot.ExpireAt
            };
        }
    }
}
