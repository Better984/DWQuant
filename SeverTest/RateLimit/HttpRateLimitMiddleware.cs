using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Services;

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

        public HttpRateLimitMiddleware(RequestDelegate next, ILogger<HttpRateLimitMiddleware> logger)
        {
            _next = next;
            _logger = logger;
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
                _logger.LogWarning("HTTP rate limit hit for user {UserId} path {Path}", userId, path);
                await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests, "rate_limit", "HTTP rate limit exceeded");
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

        private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var payload = ErrorResponse.Create(code, message, context.TraceIdentifier);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
