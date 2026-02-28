namespace ServerTest.Options
{
    /// <summary>
    /// 策略链路诊断日志配置（用于定位瓶颈阶段与阻塞点）。
    /// </summary>
    public sealed class StrategyDiagnosticsOptions
    {
        /// <summary>
        /// 是否启用策略运行画像日志。
        /// </summary>
        public bool EnableRunProfileLog { get; set; } = true;

        /// <summary>
        /// 是否输出每一条策略任务画像（默认仅输出执行任务或慢任务）。
        /// </summary>
        public bool LogEveryRunTask { get; set; }

        /// <summary>
        /// 策略任务慢日志阈值（毫秒）。
        /// </summary>
        public int SlowRunThresholdMs { get; set; } = 120;

        /// <summary>
        /// 指标刷新慢日志阈值（毫秒）。
        /// </summary>
        public int SlowIndicatorRefreshThresholdMs { get; set; } = 60;

        /// <summary>
        /// 是否将策略运行画像写入 strategy_engine_run_log.extra_json。
        /// </summary>
        public bool PersistRunProfileExtraJson { get; set; } = true;

        /// <summary>
        /// 是否启用行情任务分发耗时日志。
        /// </summary>
        public bool EnableMarketDispatchLog { get; set; } = true;

        /// <summary>
        /// 是否输出每一条行情任务分发耗时（默认仅输出慢分发）。
        /// </summary>
        public bool LogEveryMarketDispatch { get; set; }

        /// <summary>
        /// 行情分发慢日志阈值（毫秒）。
        /// </summary>
        public int SlowMarketDispatchThresholdMs { get; set; } = 20;

        /// <summary>
        /// 是否启用动作队列入队耗时日志。
        /// </summary>
        public bool EnableActionEnqueueLog { get; set; } = true;

        /// <summary>
        /// 是否输出每一次动作入队耗时（默认仅输出慢入队）。
        /// </summary>
        public bool LogEveryActionEnqueue { get; set; }

        /// <summary>
        /// 动作入队慢日志阈值（毫秒）。
        /// </summary>
        public int SlowActionEnqueueThresholdMs { get; set; } = 30;

        /// <summary>
        /// 是否启用交易动作消费耗时日志。
        /// </summary>
        public bool EnableTradeConsumeLog { get; set; } = true;

        /// <summary>
        /// 是否输出每一条交易动作消费耗时（默认仅输出慢消费）。
        /// </summary>
        public bool LogEveryTradeAction { get; set; }

        /// <summary>
        /// 交易动作消费慢日志阈值（毫秒）。
        /// </summary>
        public int SlowTradeActionThresholdMs { get; set; } = 300;

        /// <summary>
        /// 是否启用策略任务链路追踪落库（strategy_task_trace_log）。
        /// </summary>
        public bool EnableTaskTracePersist { get; set; } = true;

        /// <summary>
        /// 是否记录每一个链路阶段事件；关闭后仅记录慢事件或异常事件。
        /// </summary>
        public bool TaskTraceLogEveryEvent { get; set; } = true;

        /// <summary>
        /// 链路追踪慢阶段阈值（毫秒）。
        /// </summary>
        public int SlowTaskTraceThresholdMs { get; set; } = 120;
    }
}
