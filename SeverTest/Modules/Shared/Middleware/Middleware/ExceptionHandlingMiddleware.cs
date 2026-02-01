using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Net;
using System.Text.Json;

namespace ServerTest.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly RequestLimitsOptions _requestLimits;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger,
            IOptions<RequestLimitsOptions> requestLimits)
        {
            _next = next;
            _logger = logger;
            _requestLimits = requestLimits?.Value ?? new RequestLimitsOptions();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "未处理的异常");
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var reqId = await ProtocolRequestIdResolver.ResolveAsync(context, _requestLimits.DefaultMaxBodyBytes)
                .ConfigureAwait(false);
            var payload = ProtocolEnvelopeFactory.Error(
                reqId,
                ProtocolErrorCodes.InternalError,
                $"内部错误: {exception.Message}",
                null,
                context.TraceIdentifier);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(payload, options);
            await context.Response.WriteAsync(json).ConfigureAwait(false);
        }
    }
}
