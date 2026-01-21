using Newtonsoft.Json;

namespace ServerTest.WebSockets
{
    public class WsMessageEnvelope
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("reqId")]
        public string? ReqId { get; set; }

        [JsonProperty("ts")]
        public long Ts { get; set; }

        [JsonProperty("payload")]
        public object? Payload { get; set; }

        [JsonProperty("err")]
        public WsError? Err { get; set; }

        public static WsMessageEnvelope Create(string type, string? reqId, object? payload, WsError? err)
        {
            return new WsMessageEnvelope
            {
                Type = type,
                ReqId = reqId,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = payload,
                Err = err
            };
        }

        public static WsMessageEnvelope Error(string? reqId, string code, string message)
        {
            return Create("error", reqId, null, new WsError { Code = code, Message = message });
        }
    }

    public class WsError
    {
        [JsonProperty("code")]
        public string Code { get; set; } = "";

        [JsonProperty("message")]
        public string Message { get; set; } = "";
    }
}
