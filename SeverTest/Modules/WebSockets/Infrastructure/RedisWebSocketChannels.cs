namespace ServerTest.WebSockets
{
    internal static class RedisWebSocketChannels
    {
        // 分布式踢下线通知通道
        public const string KickChannel = "ws:kick";
    }
}
