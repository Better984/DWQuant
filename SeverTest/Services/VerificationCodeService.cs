using Microsoft.Extensions.Caching.Distributed;

namespace ServerTest.Services
{
    public class VerificationCodeService
    {
        private readonly IDistributedCache _cache;

        public VerificationCodeService(IDistributedCache cache)
        {
            _cache = cache;
        }

        public async Task<string> CreateAndStoreAsync(string email, TimeSpan? ttl = null)
        {
            var code = Random.Shared.Next(100000, 999999).ToString();
            var opts = new DistributedCacheEntryOptions();
            opts.SetAbsoluteExpiration(ttl ?? TimeSpan.FromMinutes(10));
            await _cache.SetStringAsync(BuildKey(email), code, opts);
            return code;
        }

        public Task<string?> GetAsync(string email)
        {
            return _cache.GetStringAsync(BuildKey(email));
        }

        private static string BuildKey(string email)
        {
            return $"auth:verify:{email.ToLowerInvariant()}";
        }
    }
}
