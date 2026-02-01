using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ServerTest.Options;

namespace ServerTest.Modules.MarketStreaming.Infrastructure
{
    public sealed class RedisMarketSubscriptionStore : IMarketSubscriptionStore
    {
        private readonly IDatabase _db;
        private readonly ILogger<RedisMarketSubscriptionStore> _logger;
        private readonly RedisKeyOptions _redisKeyOptions;
        private readonly ConcurrentDictionary<string, HashSet<string>> _cache =
            new(StringComparer.Ordinal);
        private readonly object _cacheLock = new object();
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(2);
        private DateTime _lastRefreshUtc = DateTime.MinValue;

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
            var now = DateTime.UtcNow;
            if (now - _lastRefreshUtc <= _cacheTtl)
            {
                return;
            }

            lock (_cacheLock)
            {
                if (now - _lastRefreshUtc <= _cacheTtl)
                {
                    return;
                }

                RefreshCacheFromRedis();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        private void RefreshCacheFromRedis()
        {
            var users = _db.SetMembers(_redisKeyOptions.MarketSubUserSetKey);
            if (users.Length == 0)
            {
                _cache.Clear();
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

            var snapshot = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var entry in tasks)
            {
                var members = entry.Value.GetAwaiter().GetResult();
                if (members.Length == 0)
                {
                    continue;
                }

                var symbols = members
                    .Select(member => member.ToString())
                    .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (symbols.Count > 0)
                {
                    snapshot[entry.Key] = symbols;
                }
            }

            _cache.Clear();
            foreach (var entry in snapshot)
            {
                _cache[entry.Key] = entry.Value;
            }
        }

        private bool TryGetCachedSubscriptions(string userId, out IReadOnlyCollection<string> subscriptions)
        {
            subscriptions = Array.Empty<string>();
            EnsureCache();
            if (!_cache.TryGetValue(userId, out var symbols) || symbols.Count == 0)
            {
                return false;
            }

            subscriptions = symbols.ToList();
            return true;
        }

        private IReadOnlyDictionary<string, IReadOnlyCollection<string>> SnapshotCache()
        {
            var snapshot = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var entry in _cache)
            {
                if (entry.Value.Count == 0)
                {
                    continue;
                }

                snapshot[entry.Key] = entry.Value.ToList();
            }

            return snapshot;
        }

        private void UpdateCacheOnSubscribe(string userId, HashSet<string> symbols)
        {
            EnsureCache();
            var set = _cache.GetOrAdd(userId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var symbol in symbols)
            {
                set.Add(symbol);
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
                set.Remove(symbol);
            }

            if (set.Count == 0)
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
