using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Monitoring.Application;
using ServerTest.Modules.Monitoring.Domain;
using ServerTest.Options;
using ServerTest.Protocol;

namespace ServerTest.Modules.Monitoring.Infrastructure
{
    public sealed class ProtocolPerformanceHttpMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ProtocolPerformanceStorageFeature _storageFeature;
        private readonly ProtocolPerformanceRecorder _recorder;
        private readonly RequestLimitsOptions _requestLimits;

        public ProtocolPerformanceHttpMiddleware(
            RequestDelegate next,
            ProtocolPerformanceStorageFeature storageFeature,
            ProtocolPerformanceRecorder recorder,
            IOptions<RequestLimitsOptions> requestLimits)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _storageFeature = storageFeature ?? throw new ArgumentNullException(nameof(storageFeature));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _requestLimits = requestLimits?.Value ?? new RequestLimitsOptions();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_storageFeature.IsEnabled || !ShouldTrack(context))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            PrepareRequestBuffer(context);

            var serverStartedAt = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            context.Response.OnCompleted(() => RecordAsync(context, serverStartedAt, stopwatch));

            await _next(context).ConfigureAwait(false);
        }

        private async Task RecordAsync(HttpContext context, DateTime serverStartedAt, Stopwatch stopwatch)
        {
            stopwatch.Stop();

            var metadata = await ProtocolRequestMetadataResolver.ResolveAsync(context, _requestLimits.DefaultMaxBodyBytes)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(metadata.ReqId))
            {
                return;
            }

            var protocolType = !string.IsNullOrWhiteSpace(metadata.Type)
                ? metadata.Type!
                : context.Request.Path.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(protocolType))
            {
                return;
            }

            var protocolCode = ProtocolContext.GetResponseCode(context);
            var isSuccess = ProtocolContext.GetResponseSuccess(context);

            _recorder.TryRecordServerMetric(new ProtocolPerformanceServerMetric
            {
                ReqId = metadata.ReqId!,
                Transport = ProtocolPerformanceTransport.Http,
                ProtocolType = protocolType,
                RequestPath = context.Request.Path.Value,
                HttpMethod = context.Request.Method,
                UserId = ResolveUserId(context.User),
                SystemName = ResolveSystemName(context),
                TraceId = context.TraceIdentifier,
                RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
                ServerStartedAt = serverStartedAt,
                ServerCompletedAt = DateTime.UtcNow,
                ServerElapsedMs = (int)Math.Max(0, stopwatch.ElapsedMilliseconds),
                ProtocolCode = protocolCode ?? MapStatusCode(context.Response.StatusCode),
                HttpStatus = context.Response.StatusCode,
                IsSuccess = isSuccess ?? context.Response.StatusCode < StatusCodes.Status400BadRequest,
                IsTimeout = context.Response.StatusCode == StatusCodes.Status408RequestTimeout,
                ErrorMessage = ProtocolContext.GetResponseMessage(context)
            });
        }

        private static bool ShouldTrack(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                return false;
            }

            return context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        }

        private void PrepareRequestBuffer(HttpContext context)
        {
            if (context.Request.Body.CanSeek)
            {
                return;
            }

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > _requestLimits.DefaultMaxBodyBytes)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(context.Request.ContentType)
                || !context.Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            context.Request.EnableBuffering();
        }

        private static string? ResolveUserId(ClaimsPrincipal? principal)
        {
            return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal?.FindFirst("sub")?.Value;
        }

        private static string? ResolveSystemName(HttpContext context)
        {
            if (context.Request.Query.TryGetValue("system", out var queryValue))
            {
                var system = queryValue.ToString();
                if (!string.IsNullOrWhiteSpace(system))
                {
                    return system;
                }
            }

            if (context.Request.Headers.TryGetValue("X-System", out var headerValue))
            {
                var system = headerValue.ToString();
                if (!string.IsNullOrWhiteSpace(system))
                {
                    return system;
                }
            }

            return null;
        }

        private static int MapStatusCode(int statusCode)
        {
            return statusCode switch
            {
                StatusCodes.Status200OK => ProtocolErrorCodes.Ok,
                StatusCodes.Status400BadRequest => ProtocolErrorCodes.InvalidRequest,
                StatusCodes.Status401Unauthorized => ProtocolErrorCodes.Unauthorized,
                StatusCodes.Status403Forbidden => ProtocolErrorCodes.Forbidden,
                StatusCodes.Status404NotFound => ProtocolErrorCodes.NotFound,
                StatusCodes.Status408RequestTimeout => ProtocolErrorCodes.Timeout,
                StatusCodes.Status409Conflict => ProtocolErrorCodes.Conflict,
                StatusCodes.Status429TooManyRequests => ProtocolErrorCodes.RateLimited,
                StatusCodes.Status503ServiceUnavailable => ProtocolErrorCodes.ServiceUnavailable,
                >= StatusCodes.Status500InternalServerError => ProtocolErrorCodes.InternalError,
                _ => ProtocolErrorCodes.Ok
            };
        }
    }
}
