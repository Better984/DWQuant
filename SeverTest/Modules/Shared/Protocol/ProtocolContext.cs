using Microsoft.AspNetCore.Http;

namespace ServerTest.Protocol
{
    public static class ProtocolContext
    {
        private const string ReqIdKey = "protocol.reqId";
        private const string TypeKey = "protocol.type";
        private const string ResponseCodeKey = "protocol.response.code";
        private const string ResponseSuccessKey = "protocol.response.success";
        private const string ResponseMessageKey = "protocol.response.message";

        public static void Set(HttpContext context, string type, string reqId)
        {
            context.Items[ReqIdKey] = reqId;
            context.Items[TypeKey] = type;
        }

        public static string? GetReqId(HttpContext context)
        {
            return context.Items.TryGetValue(ReqIdKey, out var value) ? value as string : null;
        }

        public static string? GetType(HttpContext context)
        {
            return context.Items.TryGetValue(TypeKey, out var value) ? value as string : null;
        }

        public static void SetResponse(HttpContext context, int code, bool success, string? message)
        {
            context.Items[ResponseCodeKey] = code;
            context.Items[ResponseSuccessKey] = success;
            context.Items[ResponseMessageKey] = message;
        }

        public static int? GetResponseCode(HttpContext context)
        {
            return context.Items.TryGetValue(ResponseCodeKey, out var value) && value is int code ? code : null;
        }

        public static bool? GetResponseSuccess(HttpContext context)
        {
            return context.Items.TryGetValue(ResponseSuccessKey, out var value) && value is bool success ? success : null;
        }

        public static string? GetResponseMessage(HttpContext context)
        {
            return context.Items.TryGetValue(ResponseMessageKey, out var value) ? value as string : null;
        }
    }
}
