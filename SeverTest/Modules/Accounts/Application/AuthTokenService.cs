using Microsoft.Extensions.Caching.Distributed;
using System.Security.Claims;

namespace ServerTest.Modules.Accounts.Application
{
    public class AuthTokenService
    {
        private const string DefaultSystem = "web";
        private readonly IDistributedCache _cache;
        private readonly JwtService _jwtService;

        public AuthTokenService(IDistributedCache cache, JwtService jwtService)
        {
            _cache = cache;
            _jwtService = jwtService;
        }

        public async Task StoreTokenAsync(string userId, string token, TimeSpan? ttl = null)
        {
            await StoreTokenAsync(userId, DefaultSystem, token, ttl).ConfigureAwait(false);
        }

        /// <summary>
        /// 保存指定用户和终端类型的最新令牌，并废止该终端类型之前的旧令牌。
        /// </summary>
        public async Task<(bool ReplacedExisting, string? PreviousToken)> StoreTokenAsync(
            string userId,
            string system,
            string token,
            TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("用户标识不能为空", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("令牌不能为空", nameof(token));
            }

            var normalizedSystem = NormalizeSystem(system);
            var opts = new DistributedCacheEntryOptions();
            opts.SetAbsoluteExpiration(ttl ?? TimeSpan.FromDays(7));

            var activeKey = BuildActiveTokenKey(userId, normalizedSystem);
            var previousToken = await _cache.GetStringAsync(activeKey).ConfigureAwait(false);

            // 同终端类型只保留一个有效 token，旧 token 直接废止。
            if (!string.IsNullOrWhiteSpace(previousToken)
                && !string.Equals(previousToken, token, StringComparison.Ordinal))
            {
                await _cache.RemoveAsync(BuildTokenKey(previousToken)).ConfigureAwait(false);
            }

            await _cache.SetStringAsync(BuildTokenKey(token), BuildTokenPayload(userId, normalizedSystem), opts)
                .ConfigureAwait(false);
            await _cache.SetStringAsync(activeKey, token, opts).ConfigureAwait(false);

            return (
                !string.IsNullOrWhiteSpace(previousToken)
                && !string.Equals(previousToken, token, StringComparison.Ordinal),
                previousToken);
        }

        public async Task<(bool IsValid, ClaimsPrincipal? Principal, string? UserId, string? System)> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, null, null, null);
            }

            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
            {
                return (false, null, null, null);
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (false, principal, null, null);
            }

            var tokenPayload = await _cache.GetStringAsync(BuildTokenKey(token)).ConfigureAwait(false);
            var parsed = ParseTokenPayload(tokenPayload);
            if (parsed == null || !string.Equals(parsed.Value.UserId, userId, StringComparison.Ordinal))
            {
                return (false, principal, userId, null);
            }

            // 兼容历史 token（仅保存 userId，无 system 维度），保持原行为。
            if (parsed.Value.IsLegacy)
            {
                var activeTokenForLegacy = await _cache.GetStringAsync(BuildActiveTokenKey(userId, parsed.Value.System))
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(activeTokenForLegacy))
                {
                    return (true, principal, userId, parsed.Value.System);
                }

                // 当同 system 已切换到新会话后，历史 token 自动失效。
                if (!string.Equals(activeTokenForLegacy, token, StringComparison.Ordinal))
                {
                    return (false, principal, userId, parsed.Value.System);
                }

                return (true, principal, userId, parsed.Value.System);
            }

            var activeToken = await _cache.GetStringAsync(BuildActiveTokenKey(userId, parsed.Value.System))
                .ConfigureAwait(false);
            if (!string.Equals(activeToken, token, StringComparison.Ordinal))
            {
                return (false, principal, userId, parsed.Value.System);
            }

            return (true, principal, userId, parsed.Value.System);
        }

        public async Task RemoveTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var tokenKey = BuildTokenKey(token);
            var tokenPayload = await _cache.GetStringAsync(tokenKey).ConfigureAwait(false);
            var parsed = ParseTokenPayload(tokenPayload);

            await _cache.RemoveAsync(tokenKey).ConfigureAwait(false);

            if (parsed != null && !parsed.Value.IsLegacy)
            {
                var activeKey = BuildActiveTokenKey(parsed.Value.UserId, parsed.Value.System);
                var activeToken = await _cache.GetStringAsync(activeKey).ConfigureAwait(false);
                if (string.Equals(activeToken, token, StringComparison.Ordinal))
                {
                    await _cache.RemoveAsync(activeKey).ConfigureAwait(false);
                }
            }
        }

        public static string NormalizeSystem(string? system)
        {
            return string.IsNullOrWhiteSpace(system)
                ? DefaultSystem
                : system.Trim().ToLowerInvariant();
        }

        private static string BuildTokenPayload(string userId, string system)
        {
            return $"{userId}|{system}";
        }

        private static string BuildTokenKey(string token)
        {
            return $"auth:token:{token}";
        }

        private static string BuildActiveTokenKey(string userId, string system)
        {
            return $"auth:active:{userId}:{system}";
        }

        private static (string UserId, string System, bool IsLegacy)? ParseTokenPayload(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var separatorIndex = payload.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
            {
                // 历史格式：仅存 userId。
                return (payload, DefaultSystem, true);
            }

            var userId = payload[..separatorIndex].Trim();
            var system = payload[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(system))
            {
                return null;
            }

            return (userId, NormalizeSystem(system), false);
        }
    }
}
