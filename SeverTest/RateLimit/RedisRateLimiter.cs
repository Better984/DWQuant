using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ServerTest.Options;

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
        private readonly RateLimitOptions _options;

        public RedisRateLimiter(IConnectionMultiplexer redis, IOptions<RateLimitOptions> options)
        {
            _db = redis.GetDatabase();
            _options = options.Value;
        }

        public bool Allow(string userId, Protocol protocol)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var rate = protocol == Protocol.Http ? _options.HttpRps : _options.WsRps;
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
