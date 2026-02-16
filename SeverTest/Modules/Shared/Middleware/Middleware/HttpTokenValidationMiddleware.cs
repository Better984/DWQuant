using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Options;
using ServerTest.Protocol;

namespace ServerTest.Middleware
{
    /// <summary>
    /// HTTP Token 统一校验中间件：
    /// - 只要请求携带 Authorization: Bearer，就先做 token 有效性校验。
    /// - 当 token 已失效/被替换登录挤下线时，统一返回协议化鉴权错误。
    /// </summary>
    public class HttpTokenValidationMiddleware
    {
        private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/auth/login",
            "/api/auth/register"
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<HttpTokenValidationMiddleware> _logger;
        private readonly RequestLimitsOptions _requestLimits;

        public HttpTokenValidationMiddleware(
            RequestDelegate next,
            ILogger<HttpTokenValidationMiddleware> logger,
            IOptions<RequestLimitsOptions> requestLimits)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _requestLimits = requestLimits?.Value ?? new RequestLimitsOptions();
        }

        public async Task InvokeAsync(HttpContext context, AuthTokenService tokenService)
        {
            if (ShouldBypass(context))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            var token = GetBearerToken(context.Request.Headers.Authorization.ToString());
            if (string.IsNullOrWhiteSpace(token))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            var validation = await tokenService.ValidateTokenAsync(token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "HTTP 鉴权失败：Token 无效或已失效 | Path={Path} | IP={RemoteIp}",
                    context.Request.Path.Value,
                    context.Connection.RemoteIpAddress);

                await WriteAuthErrorAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    ProtocolErrorCodes.TokenInvalid,
                    "登录状态已失效或已在其他同类型设备登录，请重新登录").ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(validation.UserId))
            {
                _logger.LogWarning(
                    "HTTP 鉴权失败：Token 缺少用户标识 | Path={Path} | IP={RemoteIp}",
                    context.Request.Path.Value,
                    context.Connection.RemoteIpAddress);

                await WriteAuthErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    ProtocolErrorCodes.Forbidden,
                    "令牌缺少用户标识，请重新登录").ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
        }

        private static bool ShouldBypass(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                return true;
            }

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                return true;
            }

            var path = NormalizePath(context.Request.Path.Value);
            return BypassPaths.Contains(path);
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

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader[prefix.Length..].Trim();
            }

            return null;
        }

        private async Task WriteAuthErrorAsync(HttpContext context, int statusCode, int code, string message)
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

            await context.Response.WriteAsync(json).ConfigureAwait(false);
        }
    }
}
