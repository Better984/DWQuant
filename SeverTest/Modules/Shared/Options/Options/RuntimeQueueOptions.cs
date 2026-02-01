namespace ServerTest.Options
{
    /// <summary>
    /// 运行时队列配置（用于控制容量、背压策略与告警阈值）
    /// </summary>
    public sealed class RuntimeQueueOptions
    {
        public QueueOptions MarketData { get; set; } = new QueueOptions
        {
            Capacity = 20000,
            FullMode = "DropOldest",
            WarningThresholdPercent = 80,
            WarningIntervalSeconds = 10
        };

        public QueueOptions Indicator { get; set; } = new QueueOptions
        {
            Capacity = 20000,
            FullMode = "DropOldest",
            WarningThresholdPercent = 80,
            WarningIntervalSeconds = 10
        };

        public QueueOptions StrategyAction { get; set; } = new QueueOptions
        {
            Capacity = 5000,
            FullMode = "Wait",
            WarningThresholdPercent = 80,
            WarningIntervalSeconds = 10
        };

        public QueueOptions StrategyRunLog { get; set; } = new QueueOptions
        {
            Capacity = 5000,
            FullMode = "DropOldest",
            WarningThresholdPercent = 80,
            WarningIntervalSeconds = 10
        };
    }

    /// <summary>
    /// 单个队列配置
    /// </summary>
    public sealed class QueueOptions
    {
        /// <summary>
        /// 队列容量，建议大于 0（启动校验会拒绝 <=0 的配置）
        /// </summary>
        public int Capacity { get; set; } = 10000;

        /// <summary>
        /// 队列满时策略：Wait / DropOldest / DropNewest / DropWrite
        /// </summary>
        public string FullMode { get; set; } = "DropOldest";

        /// <summary>
        /// 触发告警的容量占比（1-100）
        /// </summary>
        public int WarningThresholdPercent { get; set; } = 80;

        /// <summary>
        /// 告警最小间隔（秒）
        /// </summary>
        public int WarningIntervalSeconds { get; set; } = 10;
    }
}
