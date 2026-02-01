namespace ServerTest.Protocol
{
    public sealed class ProtocolRequest<T> : IProtocolRequest
    {
        public string Type { get; set; } = string.Empty;
        public string ReqId { get; set; } = string.Empty;
        public long Ts { get; set; }
        public T? Data { get; set; }
    }
}
