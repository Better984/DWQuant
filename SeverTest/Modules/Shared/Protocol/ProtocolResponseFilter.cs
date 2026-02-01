using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Options;

namespace ServerTest.Protocol
{
    /// <summary>
    /// 协议响应过滤：统一封装 HTTP 返回格式
    /// </summary>
    public sealed class ProtocolResponseFilter : IAsyncResultFilter
    {
        private readonly RequestLimitsOptions _requestLimits;

        public ProtocolResponseFilter(IOptions<RequestLimitsOptions> requestLimits)
        {
            _requestLimits = requestLimits?.Value ?? new RequestLimitsOptions();
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is FileResult)
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (context.Result is ObjectResult objectResult)
            {
                if (objectResult.Value is IProtocolEnvelope)
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                var statusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;
                var (message, data, successOverride) = ExtractPayload(objectResult.Value);
                var isError = statusCode >= StatusCodes.Status400BadRequest
                    || (successOverride.HasValue && !successOverride.Value);

                var reqId = ProtocolContext.GetReqId(context.HttpContext);
                if (string.IsNullOrWhiteSpace(reqId))
                {
                    reqId = await ProtocolRequestIdResolver.ResolveAsync(context.HttpContext, _requestLimits.DefaultMaxBodyBytes)
                        .ConfigureAwait(false);
                }
                var requestType = ProtocolContext.GetType(context.HttpContext) ?? string.Empty;
                var traceId = context.HttpContext.TraceIdentifier;
                var responseType = isError
                    ? ProtocolConstants.ErrorType
                    : ProtocolEnvelopeFactory.BuildAckType(requestType);

                var code = isError ? MapStatusCode(statusCode) : ProtocolErrorCodes.Ok;
                var payload = new ProtocolEnvelope<object>
                {
                    Type = responseType,
                    ReqId = reqId,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Code = code,
                    Msg = string.IsNullOrWhiteSpace(message) ? (isError ? "请求失败" : "ok") : message,
                    Data = data,
                    TraceId = traceId
                };

                context.Result = new ObjectResult(payload)
                {
                    StatusCode = isError && statusCode < StatusCodes.Status400BadRequest
                        ? StatusCodes.Status400BadRequest
                        : (isError ? statusCode : StatusCodes.Status200OK)
                };

                await next().ConfigureAwait(false);
                return;
            }

            if (context.Result is StatusCodeResult statusResult)
            {
                var reqId = ProtocolContext.GetReqId(context.HttpContext);
                if (string.IsNullOrWhiteSpace(reqId))
                {
                    reqId = await ProtocolRequestIdResolver.ResolveAsync(context.HttpContext, _requestLimits.DefaultMaxBodyBytes)
                        .ConfigureAwait(false);
                }
                var traceId = context.HttpContext.TraceIdentifier;
                var code = MapStatusCode(statusResult.StatusCode);
                var payload = ProtocolEnvelopeFactory.Error(reqId, code, "请求失败", null, traceId);
                context.Result = new ObjectResult(payload)
                {
                    StatusCode = statusResult.StatusCode
                };

                await next().ConfigureAwait(false);
                return;
            }

            if (context.Result is EmptyResult)
            {
                var reqId = ProtocolContext.GetReqId(context.HttpContext);
                if (string.IsNullOrWhiteSpace(reqId))
                {
                    reqId = await ProtocolRequestIdResolver.ResolveAsync(context.HttpContext, _requestLimits.DefaultMaxBodyBytes)
                        .ConfigureAwait(false);
                }
                var requestType = ProtocolContext.GetType(context.HttpContext) ?? string.Empty;
                var traceId = context.HttpContext.TraceIdentifier;
                var payload = ProtocolEnvelopeFactory.Ok<object>(ProtocolEnvelopeFactory.BuildAckType(requestType), reqId, null, "ok", traceId);
                context.Result = new ObjectResult(payload)
                {
                    StatusCode = StatusCodes.Status200OK
                };

                await next().ConfigureAwait(false);
                return;
            }

            await next().ConfigureAwait(false);
        }

        private static int MapStatusCode(int statusCode)
        {
            return statusCode switch
            {
                StatusCodes.Status400BadRequest => ProtocolErrorCodes.InvalidRequest,
                StatusCodes.Status401Unauthorized => ProtocolErrorCodes.Unauthorized,
                StatusCodes.Status403Forbidden => ProtocolErrorCodes.Forbidden,
                StatusCodes.Status404NotFound => ProtocolErrorCodes.NotFound,
                StatusCodes.Status409Conflict => ProtocolErrorCodes.Conflict,
                StatusCodes.Status429TooManyRequests => ProtocolErrorCodes.RateLimited,
                StatusCodes.Status503ServiceUnavailable => ProtocolErrorCodes.ServiceUnavailable,
                >= StatusCodes.Status500InternalServerError => ProtocolErrorCodes.InternalError,
                _ => ProtocolErrorCodes.InternalError
            };
        }

        private static (string? Message, object? Data, bool? Success) ExtractPayload(object? value)
        {
            if (value == null)
            {
                return (null, null, null);
            }

            if (value is ValidationProblemDetails validation)
            {
                return ("参数校验失败", validation.Errors, false);
            }

            if (value is ProblemDetails problem)
            {
                return (problem.Title ?? "请求失败", problem.Detail, false);
            }

            if (TryExtractApiResponse(value, out var success, out var message, out var data))
            {
                return (message, data, success);
            }

            return (null, value, null);
        }

        private static bool TryExtractApiResponse(object value, out bool success, out string? message, out object? data)
        {
            success = false;
            message = null;
            data = null;

            var type = value.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ApiResponse<>))
            {
                return false;
            }

            var successProp = type.GetProperty("Success");
            var messageProp = type.GetProperty("Message");
            var dataProp = type.GetProperty("Data");

            if (successProp == null || messageProp == null || dataProp == null)
            {
                return false;
            }

            if (successProp.GetValue(value) is bool successValue)
            {
                success = successValue;
            }

            message = messageProp.GetValue(value) as string;
            data = dataProp.GetValue(value);
            return true;
        }
    }
}
