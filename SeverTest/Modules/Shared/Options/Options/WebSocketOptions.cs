namespace ServerTest.Options
{
    public class WebSocketOptions
    {
        public string Path { get; set; } = "/ws";
        public int MaxMessageBytes { get; set; } = 1048576;
        public string KickPolicy { get; set; } = "KickOld";
        public int KeepAliveSeconds { get; set; } = 30;
        /// <summary>
        /// WebSocket 节点标识后缀（会自动拼接机器名前缀）。
        /// 建议在多实例部署时显式配置，保证同机多实例唯一且跨重启稳定。
        /// </summary>
        public string NodeId { get; set; } = string.Empty;
        public int MaxConnectionsPerSystem { get; set; } = 3;
        // Redis 连接上限 Key 的 TTL（秒），避免孤儿连接长期占位
        public int ConnectionKeyTtlSeconds { get; set; } = 86400;
        // 心跳或消息触发的 TTL 刷新间隔（秒）
        public int ConnectionKeyRefreshSeconds { get; set; } = 43200;
        /// <summary>
        /// 启动时是否清除所有连接槽位（含旧格式）。单节点部署可设为 true，解决升级前残留；多节点须为 false。
        /// </summary>
        public bool ClearAllConnectionsOnStartup { get; set; } = false;
    }
}
