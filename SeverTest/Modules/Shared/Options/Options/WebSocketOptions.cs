namespace ServerTest.Options
{
    public class WebSocketOptions
    {
        public string Path { get; set; } = "/ws";
        public int MaxMessageBytes { get; set; } = 1048576;
        public string KickPolicy { get; set; } = "KickOld";
        public int KeepAliveSeconds { get; set; } = 30;
        public int MaxConnectionsPerSystem { get; set; } = 3;
        // Redis 连接上限 Key 的 TTL（秒），避免孤儿连接长期占位
        public int ConnectionKeyTtlSeconds { get; set; } = 86400;
        // 心跳或消息触发的 TTL 刷新间隔（秒）
        public int ConnectionKeyRefreshSeconds { get; set; } = 43200;
    }
}
