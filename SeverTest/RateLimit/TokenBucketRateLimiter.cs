using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.RateLimit
{
    public class TokenBucketRateLimiter : IRateLimiter
    {
        private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
        private readonly IOptions<RateLimitOptions> _options;

        public TokenBucketRateLimiter(IOptions<RateLimitOptions> options)
        {
            _options = options;
        }

        public bool Allow(string userId, Protocol protocol)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var rate = protocol == Protocol.Http ? _options.Value.HttpRps : _options.Value.WsRps;
            if (rate <= 0)
            {
                return true;
            }

            var key = $"{userId}|{protocol}";
            var bucket = _buckets.GetOrAdd(key, _ => new Bucket(rate));
            return bucket.TryConsume(rate);
        }

        private sealed class Bucket
        {
            private readonly object _lock = new();
            private double _tokens;
            private DateTime _lastRefill;

            public Bucket(int capacity)
            {
                _tokens = capacity;
                _lastRefill = DateTime.UtcNow;
            }

            public bool TryConsume(int rate)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    var elapsedSeconds = (now - _lastRefill).TotalSeconds;
                    if (elapsedSeconds > 0)
                    {
                        var refill = elapsedSeconds * rate;
                        _tokens = Math.Min(rate, _tokens + refill);
                        _lastRefill = now;
                    }

                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return true;
                    }

                    return false;
                }
            }
        }
    }
}
