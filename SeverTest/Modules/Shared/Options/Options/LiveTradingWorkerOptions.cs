namespace ServerTest.Options
{
    /// <summary>
    /// 实盘分布式节点与核心节点之间的通信配置。
    /// </summary>
    public sealed class LiveTradingWorkerOptions
    {
        /// <summary>
        /// 核心节点实盘工作端 WebSocket 地址列表（支持 | 分隔）。
        /// </summary>
        public string CoreWsUrls { get; set; } = string.Empty;

        /// <summary>
        /// 核心节点实盘工作端 WebSocket 单地址（兼容旧配置）。
        /// </summary>
        public string CoreWsUrl { get; set; } = string.Empty;

        /// <summary>
        /// 工作端鉴权密钥（核心节点与实盘节点需一致）。
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// 实盘节点标识；为空时自动使用 MachineName-PID。
        /// </summary>
        public string WorkerId { get; set; } = string.Empty;

        /// <summary>
        /// 断线重连间隔（秒）。
        /// </summary>
        public int ReconnectDelaySeconds { get; set; } = 5;

        /// <summary>
        /// 心跳上报间隔（秒）。
        /// </summary>
        public int HeartbeatSeconds { get; set; } = 10;
    }
}
