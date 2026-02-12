namespace ServerTest.Options
{
    /// <summary>
    /// 回测算力节点与核心节点之间的通信配置。
    /// </summary>
    public sealed class BacktestWorkerOptions
    {
        /// <summary>
        /// 核心节点工作端 WebSocket 地址，例如：ws://core-host:9635/ws/worker
        /// </summary>
        public string CoreWsUrls { get; set; } = string.Empty;

        public string CoreWsUrl { get; set; } = string.Empty;

        /// <summary>
        /// 工作端鉴权密钥（核心节点与算力节点需一致）。
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// 算力节点标识；为空时自动使用 MachineName-PID。
        /// </summary>
        public string WorkerId { get; set; } = string.Empty;

        /// <summary>
        /// 断线重连间隔（秒）。
        /// </summary>
        public int ReconnectDelaySeconds { get; set; } = 5;

        /// <summary>
        /// 核心节点任务分发轮询间隔（毫秒）。
        /// </summary>
        public int DispatchPollingIntervalMs { get; set; } = 500;

        /// <summary>
        /// 心跳上报间隔（秒）。
        /// </summary>
        public int HeartbeatSeconds { get; set; } = 10;

        /// <summary>
        /// 每个算力节点允许并行执行任务数（当前默认单任务，预留扩展）。
        /// </summary>
        public int MaxParallelTasksPerWorker { get; set; } = 1;
    }
}
