using Microsoft.Extensions.Caching.Distributed;

namespace ServerTest.Services
{
    public class RedisCacheService
    {
        private readonly IDistributedCache _cache;

        public RedisCacheService(IDistributedCache cache)
        {
            _cache = cache;
        }

        public Task SetAsync(string key, string value, TimeSpan? ttl = null)
        {
            var opts = new DistributedCacheEntryOptions();
            if (ttl.HasValue)
            {
                opts.SetAbsoluteExpiration(ttl.Value);
            }

            return _cache.SetStringAsync(key, value, opts);
        }

        public Task<string?> GetAsync(string key)
        {
            return _cache.GetStringAsync(key);
        }
    }
}
