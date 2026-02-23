using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;
using System.Security.Claims;
using System.Text.Json;
using System.Linq;

namespace ServerTest.RateLimit
{
    public class HttpRateLimitMiddleware
    {
        private static readonly HashSet<string> AuthPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/auth/login",
            "/api/auth/register"
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<HttpRateLimitMiddleware> _logger;
        private readonly RequestLimitsOptions _requestLimits;

        public HttpRateLimitMiddleware(
            RequestDelegate next,
            ILogger<HttpRateLimitMiddleware> logger,
            IOptions<RequestLimitsOptions> requestLimits)
        {
            _next = next;
            _logger = logger;
            _requestLimits = requestLimits?.Value ?? new RequestLimitsOptions();
        }

        public async Task InvokeAsync(HttpContext context, IRateLimiter rateLimiter, JwtService jwtService)
        {
            var path = NormalizePath(context.Request.Path.Value);
            if (context.WebSockets.IsWebSocketRequest || HttpMethods.IsOptions(context.Request.Method))
            {
                await _next(context);
                return;
            }

            var rateLimitKey = BuildRateLimitKey(context, jwtService, path);
            if (!string.IsNullOrWhiteSpace(rateLimitKey) && !rateLimiter.Allow(rateLimitKey, Protocol.Http))
            {
                _logger.LogWarning("HTTP 触发限流: Key={RateLimitKey} 路径={Path}", rateLimitKey, path);
                await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests, ProtocolErrorCodes.RateLimited, "请求过于频繁");
                return;
            }

            await _next(context);
        }

        private static string? BuildRateLimitKey(HttpContext context, JwtService jwtService, string path)
        {
            var userId = TryGetUserId(context, jwtService);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return $"uid:{userId}";
            }

            var remoteIp = ResolveClientAddress(context);

            // 登录/注册使用独立维度限流，避免被其他匿名接口流量稀释。
            if (AuthPaths.Contains(path))
            {
                return $"auth:{path}:{remoteIp}";
            }

            return $"ip:{remoteIp}";
        }

        private static string ResolveClientAddress(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(remoteIp))
            {
                return remoteIp;
            }

            var xff = context.Request.Headers["X-Forwarded-For"].ToString();
            var forwarded = TryParseForwardedFor(xff);
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded;
            }

            var xRealIp = context.Request.Headers["X-Real-IP"].ToString();
            if (!string.IsNullOrWhiteSpace(xRealIp))
            {
                return xRealIp.Trim();
            }

            // 兜底使用连接ID，避免 RemoteIp 缺失时匿名请求绕过限流。
            return $"conn:{context.Connection.Id}";
        }

        private static string? TryParseForwardedFor(string? rawHeader)
        {
            if (string.IsNullOrWhiteSpace(rawHeader))
            {
                return null;
            }

            var first = rawHeader
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            var normalized = path.Trim();
            if (normalized.Length > 1)
            {
                normalized = normalized.TrimEnd('/');
            }

            return string.IsNullOrWhiteSpace(normalized) ? "/" : normalized;
        }

        private static string? TryGetUserId(HttpContext context, JwtService jwtService)
        {
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var fromContext = GetUserIdFromClaims(context.User);
                if (!string.IsNullOrWhiteSpace(fromContext))
                {
                    return fromContext;
                }
            }

            var token = GetBearerToken(context.Request.Headers.Authorization.ToString());
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var principal = jwtService.ValidateToken(token);
            if (principal == null)
            {
                return null;
            }

            return GetUserIdFromClaims(principal);
        }

        private static string? GetUserIdFromClaims(ClaimsPrincipal principal)
        {
            return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader.Substring(prefix.Length).Trim();
            }

            return null;
        }

        private async Task WriteErrorAsync(HttpContext context, int statusCode, int code, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var reqId = await ProtocolRequestIdResolver.ResolveAsync(context, _requestLimits.DefaultMaxBodyBytes)
                .ConfigureAwait(false);
            var payload = ProtocolEnvelopeFactory.Error(reqId, code, message, null, context.TraceIdentifier);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
