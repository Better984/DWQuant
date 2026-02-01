using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace ServerTest.Protocol
{
    public sealed class ProtocolEnvelope<T> : IProtocolEnvelope
    {
        [JsonProperty("type")]
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("reqId")]
        [JsonPropertyName("reqId")]
        public string? ReqId { get; set; }

        [JsonProperty("ts")]
        [JsonPropertyName("ts")]
        public long Ts { get; set; }

        [JsonProperty("code")]
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonProperty("msg")]
        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonProperty("data")]
        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonProperty("traceId")]
        [JsonPropertyName("traceId")]
        public string? TraceId { get; set; }

        object? IProtocolEnvelope.Data => Data;
    }
}
