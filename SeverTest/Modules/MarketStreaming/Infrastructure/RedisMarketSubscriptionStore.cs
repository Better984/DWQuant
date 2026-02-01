using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ServerTest.Options;

namespace ServerTest.Modules.MarketStreaming.Infrastructure
{
    public sealed class RedisMarketSubscriptionStore : IMarketSubscriptionStore
    {
        private readonly IDatabase _db;
        private readonly ILogger<RedisMarketSubscriptionStore> _logger;
        private readonly RedisKeyOptions _redisKeyOptions;
        private ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _cache =
            new(StringComparer.Ordinal);
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(2);
        private long _lastRefreshTicks = DateTime.MinValue.Ticks;
        private int _refreshing;

        public RedisMarketSubscriptionStore(
            IConnectionMultiplexer redis,
            IOptions<RedisKeyOptions> redisKeyOptions,
            ILogger<RedisMarketSubscriptionStore> logger)
        {
            _db = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
            _redisKeyOptions = redisKeyOptions?.Value ?? new RedisKeyOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Subscribe(string userId, IEnumerable<string> symbols)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var symbolSet = NormalizeSymbols(symbols);
            if (symbolSet.Count == 0)
            {
                return;
            }

            try
            {
                var key = BuildUserKey(userId);
                var values = symbolSet.Select(symbol => (RedisValue)symbol).ToArray();
                _db.SetAdd(key, values);
                _db.SetAdd(_redisKeyOptions.MarketSubUserSetKey, userId);
                UpdateCacheOnSubscribe(userId, symbolSet);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis 订阅写入失败: userId={UserId}", userId);
            }
        }

        public void Unsubscribe(string userId, IEnumerable<string> symbols)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var symbolSet = NormalizeSymbols(symbols);
            if (symbolSet.Count == 0)
            {
                return;
            }

            try
            {
                var key = BuildUserKey(userId);
                var values = symbolSet.Select(symbol => (RedisValue)symbol).ToArray();
                _db.SetRemove(key, values);

                if (_db.SetLength(key) == 0)
                {
                    _db.KeyDelete(key);
                    _db.SetRemove(_redisKeyOptions.MarketSubUserSetKey, userId);
                    UpdateCacheOnRemoveAll(userId);
                }
                else
                {
                    UpdateCacheOnUnsubscribe(userId, symbolSet);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis 取消订阅失败: userId={UserId}", userId);
            }
        }

        public IReadOnlyCollection<string> GetSubscriptions(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Array.Empty<string>();
            }

            try
            {
                if (TryGetCachedSubscriptions(userId, out var cached))
                {
                    return cached;
                }

                var members = _db.SetMembers(BuildUserKey(userId));
                if (members.Length == 0)
                {
                    return Array.Empty<string>();
                }

                UpdateCacheOnSubscribe(userId, members.Select(member => member.ToString()).Where(symbol => !string.IsNullOrWhiteSpace(symbol)).ToHashSet(StringComparer.OrdinalIgnoreCase));
                return members
                    .Select(member => member.ToString())
                    .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis 读取订阅失败: userId={UserId}", userId);
                return Array.Empty<string>();
            }
        }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetAllSubscriptions()
        {
            try
            {
                EnsureCache();
                return SnapshotCache();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis 读取订阅全量失败");
                return new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            }
        }

        private void EnsureCache()
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref _lastRefreshTicks);
            if (nowTicks - lastTicks <= _cacheTtl.Ticks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            {
                return;
            }

            _ = RefreshCacheFromRedisAsync();
        }

        private async Task RefreshCacheFromRedisAsync()
        {
            try
            {
                var users = await _db.SetMembersAsync(_redisKeyOptions.MarketSubUserSetKey).ConfigureAwait(false);
                if (users.Length == 0)
                {
                    Interlocked.Exchange(ref _cache, new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal));
                    return;
                }

                var batch = _db.CreateBatch();
                var tasks = new Dictionary<string, Task<RedisValue[]>>(StringComparer.Ordinal);
                foreach (var userValue in users)
                {
                    var userId = userValue.ToString();
                    if (string.IsNullOrWhiteSpace(userId))
                    {
                        continue;
                    }

                    tasks[userId] = batch.SetMembersAsync(BuildUserKey(userId));
                }

                batch.Execute();
                await Task.WhenAll(tasks.Values).ConfigureAwait(false);

                var snapshot = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);
                foreach (var entry in tasks)
                {
                    var members = await entry.Value.ConfigureAwait(false);
                    if (members.Length == 0)
                    {
                        continue;
                    }

                    var symbols = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in members)
                    {
                        var symbol = member.ToString();
                        if (string.IsNullOrWhiteSpace(symbol))
                        {
                            continue;
                        }

                        symbols.TryAdd(symbol, 0);
                    }

                    if (!symbols.IsEmpty)
                    {
                        snapshot[entry.Key] = symbols;
                    }
                }

                Interlocked.Exchange(ref _cache, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis 缓存刷新失败");
            }
            finally
            {
                Interlocked.Exchange(ref _lastRefreshTicks, DateTime.UtcNow.Ticks);
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private bool TryGetCachedSubscriptions(string userId, out IReadOnlyCollection<string> subscriptions)
        {
            subscriptions = Array.Empty<string>();
            EnsureCache();
            if (!_cache.TryGetValue(userId, out var symbols) || symbols.IsEmpty)
            {
                return false;
            }

            subscriptions = symbols.Keys.ToList();
            return true;
        }

        private IReadOnlyDictionary<string, IReadOnlyCollection<string>> SnapshotCache()
        {
            var snapshot = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var entry in _cache)
            {
                if (entry.Value.IsEmpty)
                {
                    continue;
                }

                snapshot[entry.Key] = entry.Value.Keys.ToList();
            }

            return snapshot;
        }

        private void UpdateCacheOnSubscribe(string userId, HashSet<string> symbols)
        {
            EnsureCache();
            var set = _cache.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            foreach (var symbol in symbols)
            {
                set.TryAdd(symbol, 0);
            }
        }

        private void UpdateCacheOnUnsubscribe(string userId, HashSet<string> symbols)
        {
            EnsureCache();
            if (!_cache.TryGetValue(userId, out var set))
            {
                return;
            }

            foreach (var symbol in symbols)
            {
                set.TryRemove(symbol, out _);
            }

            if (set.IsEmpty)
            {
                _cache.TryRemove(userId, out _);
            }
        }

        private void UpdateCacheOnRemoveAll(string userId)
        {
            EnsureCache();
            _cache.TryRemove(userId, out _);
        }

        private static HashSet<string> NormalizeSymbols(IEnumerable<string> symbols)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (symbols == null)
            {
                return result;
            }

            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                result.Add(symbol.Trim());
            }

            return result;
        }

        private string BuildUserKey(string userId) => $"{_redisKeyOptions.MarketSubUserPrefix}{userId}";
    }
}
