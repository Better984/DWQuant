using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;
using System.Net;
using System.Text.Json;

namespace ServerTest.Middleware
{
    /// <summary>
    /// 系统就绪检查中间件：关键模块就绪后才允许处理请求
    /// </summary>
    public class SystemReadinessMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SystemReadinessMiddleware> _logger;
        private readonly SystemStartupManager _startupManager;
        private readonly RequestLimitsOptions _requestLimits;

        // 允许在系统未就绪时访问的路径（健康检查等）
        private static readonly string[] AllowedPaths = new[]
        {
            "/api/health",
            "/swagger",
            "/swagger/index.html",
            "/swagger/v1/swagger.json"
        };

        public SystemReadinessMiddleware(
            RequestDelegate next,
            ILogger<SystemReadinessMiddleware> logger,
            SystemStartupManager startupManager,
            IOptions<RequestLimitsOptions> requestLimits)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
            _requestLimits = requestLimits?.Value ?? new RequestLimitsOptions();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // 允许健康检查和 Swagger 访问
            if (IsAllowedPath(path))
            {
                await _next(context);
                return;
            }

            // 检查基础设施是否就绪
            if (!_startupManager.IsReady(SystemModule.Infrastructure))
            {
                _logger.LogWarning(
                    "请求被阻断: 基础设施未就绪 | Path: {Path} | IP: {RemoteIp}",
                    path,
                    context.Connection.RemoteIpAddress);

                await WriteServiceUnavailableAsync(
                    context,
                    ProtocolErrorCodes.ServiceUnavailable,
                    "系统基础设施正在启动中，请稍后重试");
                return;
            }

            // 检查交易相关请求是否需要实盘系统就绪
            if (IsTradingRelatedPath(path) && !_startupManager.IsReady(SystemModule.TradingSystem))
            {
                _logger.LogWarning(
                    "交易请求被阻断: 实盘交易系统未就绪 | Path: {Path} | IP: {RemoteIp}",
                    path,
                    context.Connection.RemoteIpAddress);

                await WriteServiceUnavailableAsync(
                    context,
                    ProtocolErrorCodes.ServiceUnavailable,
                    "实盘交易系统正在启动中，请稍后重试");
                return;
            }

            // 系统就绪，继续处理请求
            await _next(context);
        }

        private static bool IsAllowedPath(string path)
        {
            foreach (var allowed in AllowedPaths)
            {
                if (path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsTradingRelatedPath(string path)
        {
            // 交易相关的路径
            var tradingPaths = new[]
            {
                "/api/marketdata",
                "/api/strategy",
                "/api/trade"
            };

            foreach (var tradingPath in tradingPaths)
            {
                if (path.StartsWith(tradingPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task WriteServiceUnavailableAsync(
            HttpContext context,
            int code,
            string message)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
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
