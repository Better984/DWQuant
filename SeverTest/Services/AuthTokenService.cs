using Microsoft.Extensions.Caching.Distributed;
using System.Security.Claims;

namespace ServerTest.Services
{
    public class AuthTokenService
    {
        private readonly IDistributedCache _cache;
        private readonly JwtService _jwtService;

        public AuthTokenService(IDistributedCache cache, JwtService jwtService)
        {
            _cache = cache;
            _jwtService = jwtService;
        }

        public async Task StoreTokenAsync(string userId, string token, TimeSpan? ttl = null)
        {
            var opts = new DistributedCacheEntryOptions();
            opts.SetAbsoluteExpiration(ttl ?? TimeSpan.FromDays(7));
            await _cache.SetStringAsync(BuildTokenKey(token), userId, opts);
        }

        public async Task<(bool IsValid, ClaimsPrincipal? Principal, string? UserId)> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, null, null);
            }

            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
            {
                return (false, null, null);
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (false, principal, null);
            }

            var cachedUserId = await _cache.GetStringAsync(BuildTokenKey(token));
            if (!string.Equals(cachedUserId, userId, StringComparison.Ordinal))
            {
                return (false, principal, userId);
            }

            return (true, principal, userId);
        }

        public Task RemoveTokenAsync(string token)
        {
            return _cache.RemoveAsync(BuildTokenKey(token));
        }

        private static string BuildTokenKey(string token)
        {
            return $"auth:token:{token}";
        }
    }
}
