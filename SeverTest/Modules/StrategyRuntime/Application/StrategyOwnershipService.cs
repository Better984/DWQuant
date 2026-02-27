using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 策略租约管理，确保多实例下单策略只在单实例执行。
    /// </summary>
    public sealed class StrategyOwnershipService
    {
        private const string ScriptAcquire = @"
local current = redis.call('GET', KEYS[1])
if not current then
  redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
  return 1
end
if current == ARGV[1] then
  redis.call('EXPIRE', KEYS[1], ARGV[2])
  return 1
end
return 0
";

        private const string ScriptRenew = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
  redis.call('EXPIRE', KEYS[1], ARGV[2])
  return 1
end
return 0
";

        private const string ScriptRelease = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('DEL', KEYS[1])
end
return 0
";

        private readonly IDatabase _db;
        private readonly StrategyOwnershipOptions _options;
        private readonly ILogger<StrategyOwnershipService> _logger;
        private readonly ConcurrentDictionary<long, byte> _owned = new();

        public StrategyOwnershipService(
            IConnectionMultiplexer redis,
            IOptions<StrategyOwnershipOptions> options,
            ILogger<StrategyOwnershipService> logger)
        {
            _db = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
            _options = options?.Value ?? new StrategyOwnershipOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        }

        public string InstanceId { get; }

        public bool IsEnabled => _options.Enabled;

        public IReadOnlyCollection<long> GetOwnedIdsSnapshot()
        {
            return _owned.Keys.ToList();
        }

        public async Task<bool> TryAcquireAsync(long usId, CancellationToken ct)
        {
            if (!IsEnabled)
            {
                _owned.TryAdd(usId, 0);
                return true;
            }

            var key = BuildKey(usId);
            var result = await _db.ScriptEvaluateAsync(
                    ScriptAcquire,
                    new RedisKey[] { key },
                    new RedisValue[] { InstanceId, _options.LeaseSeconds })
                .ConfigureAwait(false);

            var acquired = (int)result == 1;
            if (acquired)
            {
                _owned.TryAdd(usId, 0);
            }

            return acquired;
        }

        public async Task<bool> TryRenewAsync(long usId, CancellationToken ct)
        {
            if (!IsEnabled)
            {
                return true;
            }

            var key = BuildKey(usId);
            var result = await _db.ScriptEvaluateAsync(
                    ScriptRenew,
                    new RedisKey[] { key },
                    new RedisValue[] { InstanceId, _options.LeaseSeconds })
                .ConfigureAwait(false);

            return (int)result == 1;
        }

        public async Task<bool> ReleaseAsync(long usId, CancellationToken ct)
        {
            _owned.TryRemove(usId, out _);

            if (!IsEnabled)
            {
                return true;
            }

            var key = BuildKey(usId);
            var result = await _db.ScriptEvaluateAsync(
                    ScriptRelease,
                    new RedisKey[] { key },
                    new RedisValue[] { InstanceId })
                .ConfigureAwait(false);

            return (int)result >= 1;
        }

        public async Task ReleaseAllAsync(CancellationToken ct)
        {
            var ownedIds = GetOwnedIdsSnapshot();
            foreach (var usId in ownedIds)
            {
                try
                {
                    await ReleaseAsync(usId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "释放策略租约失败: {UsId}", usId);
                }
            }
        }

        public void TrackOwned(long usId)
        {
            _owned.TryAdd(usId, 0);
        }

        public void Untrack(long usId)
        {
            _owned.TryRemove(usId, out _);
        }

        private string BuildKey(long usId)
        {
            return $"{_options.KeyPrefix}{usId}";
        }
    }
}
