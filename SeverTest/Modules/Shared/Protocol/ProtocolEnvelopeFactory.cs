using System;

namespace ServerTest.Protocol
{
    public static class ProtocolEnvelopeFactory
    {
        public static ProtocolEnvelope<T> Ok<T>(string type, string? reqId, T? data, string? msg = null, string? traceId = null)
        {
            return new ProtocolEnvelope<T>
            {
                Type = type,
                ReqId = reqId,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Code = ProtocolErrorCodes.Ok,
                Msg = string.IsNullOrWhiteSpace(msg) ? "ok" : msg,
                Data = data,
                TraceId = traceId
            };
        }

        public static ProtocolEnvelope<object> Error(string? reqId, int code, string msg, object? data = null, string? traceId = null)
        {
            return new ProtocolEnvelope<object>
            {
                Type = ProtocolConstants.ErrorType,
                ReqId = reqId,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Code = code,
                Msg = msg,
                Data = data,
                TraceId = traceId
            };
        }

        public static string BuildAckType(string requestType)
        {
            if (string.IsNullOrWhiteSpace(requestType))
            {
                return "ack";
            }

            return requestType.EndsWith(ProtocolConstants.AckSuffix, StringComparison.OrdinalIgnoreCase)
                ? requestType
                : requestType + ProtocolConstants.AckSuffix;
        }
    }
}
