namespace ServerTest.Options
{
    /// <summary>
    /// 实盘策略执行引擎配置。
    /// </summary>
    public sealed class LiveTradingOptions
    {
        /// <summary>
        /// 行情任务消费分片数。1 = 单 worker 串行（默认），大于 1 = 按 exchange|symbol|timeframe 键分片并行消费。
        /// 建议值：活跃交易对数或 CPU 核数 / 4，取较小值。
        /// </summary>
        public int ShardCount { get; set; } = 4;
    } 
}
