namespace ServerTest.Modules.Indicators.Domain
{
    /// <summary>
    /// WS 实时频道缓存快照。
    /// </summary>
    public sealed class IndicatorRealtimeChannelSnapshot
    {
        public string Channel { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
        public long ReceivedAt { get; set; }
        public long ExpireAt { get; set; }
        public string Source { get; set; } = "coinglass.ws.stream";
    }
}
