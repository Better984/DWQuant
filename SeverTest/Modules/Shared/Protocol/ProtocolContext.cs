using Microsoft.AspNetCore.Http;

namespace ServerTest.Protocol
{
    public static class ProtocolContext
    {
        private const string ReqIdKey = "protocol.reqId";
        private const string TypeKey = "protocol.type";

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
    }
}
