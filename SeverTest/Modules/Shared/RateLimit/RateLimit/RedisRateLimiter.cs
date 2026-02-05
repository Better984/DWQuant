using System;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Config;
using ServerTest.Options;
using StackExchange.Redis;

namespace ServerTest.RateLimit
{
    public class RedisRateLimiter : IRateLimiter
    {
        private const string Script = @"
local rate = tonumber(ARGV[1])
local now = tonumber(ARGV[2])
local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(data[1])
local ts = tonumber(data[2])
if tokens == nil then
  tokens = rate
  ts = now
end
local elapsed = (now - ts) / 1000.0
if elapsed > 0 then
  local refill = elapsed * rate
  tokens = math.min(rate, tokens + refill)
  ts = now
end
if tokens < 1 then
  redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', ts)
  redis.call('EXPIRE', KEYS[1], 2)
  return 0
end
tokens = tokens - 1
redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', ts)
redis.call('EXPIRE', KEYS[1], 2)
return 1
";

        private readonly IDatabase _db;
        private readonly RateLimitOptions _fallbackOptions;
        private readonly ServerConfigStore _configStore;

        public RedisRateLimiter(
            IConnectionMultiplexer redis,
            IOptions<RateLimitOptions> options,
            ServerConfigStore configStore)
        {
            _db = redis.GetDatabase();
            _fallbackOptions = options.Value;
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        }

        public bool Allow(string userId, Protocol protocol)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var rate = protocol == Protocol.Http
                ? _configStore.GetInt("RateLimit:HttpRps", _fallbackOptions.HttpRps)
                : _configStore.GetInt("RateLimit:WsRps", _fallbackOptions.WsRps);
            if (rate <= 0)
            {
                return true;
            }

            var key = $"rl:{protocol}:{userId}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = (int)_db.ScriptEvaluate(Script, new RedisKey[] { key }, new RedisValue[] { rate, now });
            return result == 1;
        }
    }
}
