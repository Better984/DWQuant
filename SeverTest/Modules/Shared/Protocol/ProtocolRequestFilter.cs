using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ServerTest.Protocol
{
    /// <summary>
    /// 协议请求过滤：校验必填字段并写入上下文
    /// </summary>
    public sealed class ProtocolRequestFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.ActionArguments.Values.OfType<IProtocolRequest>().FirstOrDefault();
            if (request == null)
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Type))
            {
                context.Result = BuildError(context.HttpContext, request.ReqId, ProtocolErrorCodes.MissingField, "缺少协议类型");
                return;
            }

            if (string.IsNullOrWhiteSpace(request.ReqId))
            {
                context.Result = BuildError(context.HttpContext, request.ReqId, ProtocolErrorCodes.MissingField, "缺少 reqId");
                return;
            }

            if (request.Ts <= 0)
            {
                context.Result = BuildError(context.HttpContext, request.ReqId, ProtocolErrorCodes.InvalidFormat, "缺少有效时间戳");
                return;
            }

            var expectedType = context.ActionDescriptor.EndpointMetadata
                .OfType<ProtocolTypeAttribute>()
                .FirstOrDefault()?.Type;
            if (!string.IsNullOrWhiteSpace(expectedType)
                && !string.Equals(request.Type, expectedType, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = BuildError(context.HttpContext, request.ReqId, ProtocolErrorCodes.InvalidRequest, "协议类型不匹配");
                return;
            }

            ProtocolContext.Set(context.HttpContext, request.Type, request.ReqId);
            await next().ConfigureAwait(false);
        }

        private static ObjectResult BuildError(HttpContext context, string? reqId, int code, string message)
        {
            var payload = ProtocolEnvelopeFactory.Error(reqId, code, message, null, context.TraceIdentifier);
            return new ObjectResult(payload)
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
    }
}
