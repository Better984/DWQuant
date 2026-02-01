using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;
using System.Security.Claims;
using System.Text.Json;

namespace ServerTest.RateLimit
{
    public class HttpRateLimitMiddleware
    {
        private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
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
            var path = context.Request.Path.Value ?? string.Empty;
            if (context.WebSockets.IsWebSocketRequest || BypassPaths.Contains(path))
            {
                await _next(context);
                return;
            }

            var userId = TryGetUserId(context, jwtService);
            if (!string.IsNullOrWhiteSpace(userId) && !rateLimiter.Allow(userId, Protocol.Http))
            {
                _logger.LogWarning("HTTP 触发限流: 用户 {UserId} 路径 {Path}", userId, path);
                await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests, ProtocolErrorCodes.RateLimited, "请求过于频繁");
                return;
            }

            await _next(context);
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
