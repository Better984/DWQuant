using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using StackExchange.Redis;

namespace ServerTest.Modules.Positions.Application
{
    /// <summary>
    /// 仓位风控引擎单节点租约：多实例部署时仅持租约节点执行全量风控，避免重复发平仓单。
    /// </summary>
    public sealed class PositionRiskLeaseService
    {
        private const string LeaseKey = "position_risk:master";

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
        private readonly ILogger<PositionRiskLeaseService> _logger;
        private bool _owned;

        public PositionRiskLeaseService(
            IConnectionMultiplexer redis,
            IOptions<StrategyOwnershipOptions> options,
            ILogger<PositionRiskLeaseService> logger)
        {
            _db = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
            _options = options?.Value ?? new StrategyOwnershipOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        }

        public string InstanceId { get; }

        /// <summary>
        /// 是否启用租约（与策略租约共用配置，多节点时启用）
        /// </summary>
        public bool IsEnabled => _options.Enabled;

        public bool IsOwned => _owned;

        public async Task<bool> TryAcquireAsync(CancellationToken ct)
        {
            if (!IsEnabled)
            {
                _owned = true;
                return true;
            }

            var result = await _db.ScriptEvaluateAsync(
                    ScriptAcquire,
                    new RedisKey[] { LeaseKey },
                    new RedisValue[] { InstanceId, _options.LeaseSeconds })
                .ConfigureAwait(false);

            var acquired = (int)result == 1;
            if (acquired)
            {
                _owned = true;
                _logger.LogInformation("仓位风控租约已获取: InstanceId={InstanceId}", InstanceId);
            }

            return acquired;
        }

        public async Task<bool> TryRenewAsync(CancellationToken ct)
        {
            if (!IsEnabled)
            {
                return true;
            }

            var result = await _db.ScriptEvaluateAsync(
                    ScriptRenew,
                    new RedisKey[] { LeaseKey },
                    new RedisValue[] { InstanceId, _options.LeaseSeconds })
                .ConfigureAwait(false);

            var renewed = (int)result == 1;
            if (!renewed)
            {
                _owned = false;
            }

            return renewed;
        }

        public async Task ReleaseAsync(CancellationToken ct)
        {
            _owned = false;

            if (!IsEnabled)
            {
                return;
            }

            await _db.ScriptEvaluateAsync(
                    ScriptRelease,
                    new RedisKey[] { LeaseKey },
                    new RedisValue[] { InstanceId })
                .ConfigureAwait(false);

            _logger.LogInformation("仓位风控租约已释放: InstanceId={InstanceId}", InstanceId);
        }
    }
}
