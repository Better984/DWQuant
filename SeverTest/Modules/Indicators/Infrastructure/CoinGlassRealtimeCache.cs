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
    /// CoinGlass WS 实时频道缓存：
    /// - L1：进程内内存
    /// - L2：Redis
    /// </summary>
    public sealed class CoinGlassRealtimeCache
    {
        private readonly IDistributedCache _distributedCache;
        private readonly CoinGlassOptions _options;
        private readonly ILogger<CoinGlassRealtimeCache> _logger;
        private readonly ConcurrentDictionary<string, IndicatorRealtimeChannelSnapshot> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

        public CoinGlassRealtimeCache(
            IDistributedCache distributedCache,
            IOptions<CoinGlassOptions> options,
            ILogger<CoinGlassRealtimeCache> logger)
        {
            _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
            _options = options?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IndicatorRealtimeChannelSnapshot?> GetAsync(string channel, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channel))
            {
                return null;
            }

            var normalized = NormalizeChannel(channel);
            var cacheKey = BuildCacheKey(normalized);

            if (_memoryCache.TryGetValue(cacheKey, out var memorySnapshot))
            {
                // _logger.LogDebug("[coinglass][实时缓存] 实时频道缓存命中(内存): channel={Channel}, payloadLength={Length}", normalized, memorySnapshot.PayloadJson?.Length ?? 0);
                return Clone(memorySnapshot);
            }

            try
            {
                var text = await _distributedCache.GetStringAsync(cacheKey, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                var snapshot = ProtocolJson.Deserialize<IndicatorRealtimeChannelSnapshot>(text);
                if (snapshot == null)
                {
                    return null;
                }

                _memoryCache[cacheKey] = Clone(snapshot);
                // _logger.LogDebug("[coinglass][实时缓存] 实时频道缓存命中(Redis): channel={Channel}, payloadLength={Length}", normalized, snapshot.PayloadJson?.Length ?? 0);
                return Clone(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[coinglass][实时缓存] 读取 WS 实时缓存失败，降级返回空: channel={Channel}", normalized);
                return null;
            }
        }

        public async Task SetAsync(string channel, string payloadJson, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(payloadJson))
            {
                return;
            }

            var normalized = NormalizeChannel(channel);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var keepSeconds = Math.Max(1, _options.WsChannelCacheSeconds);
            var snapshot = new IndicatorRealtimeChannelSnapshot
            {
                Channel = normalized,
                PayloadJson = payloadJson,
                ReceivedAt = nowMs,
                ExpireAt = nowMs + keepSeconds * 1000L,
                Source = "coinglass.ws.stream"
            };

            var cacheKey = BuildCacheKey(normalized);
            _memoryCache[cacheKey] = Clone(snapshot);

            // _logger.LogDebug("[coinglass][实时缓存] 写入实时频道缓存: channel={Channel}, payloadLength={Length}, keepSeconds={KeepSeconds}", normalized, payloadJson.Length, keepSeconds);

            try
            {
                var redisOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(keepSeconds, keepSeconds * 2))
                };

                await _distributedCache.SetStringAsync(
                    cacheKey,
                    ProtocolJson.Serialize(snapshot),
                    redisOptions,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[coinglass][实时缓存] 写入 WS 实时 Redis 缓存失败，已保留内存缓存: channel={Channel}", normalized);
            }
        }

        private static string NormalizeChannel(string channel)
        {
            return channel.Trim().ToLowerInvariant();
        }

        private static string BuildCacheKey(string channel)
        {
            return $"indicator:realtime:channel:{channel}";
        }

        private static IndicatorRealtimeChannelSnapshot Clone(IndicatorRealtimeChannelSnapshot snapshot)
        {
            return new IndicatorRealtimeChannelSnapshot
            {
                Channel = snapshot.Channel,
                PayloadJson = snapshot.PayloadJson,
                ReceivedAt = snapshot.ReceivedAt,
                ExpireAt = snapshot.ExpireAt,
                Source = snapshot.Source
            };
        }
    }
}
