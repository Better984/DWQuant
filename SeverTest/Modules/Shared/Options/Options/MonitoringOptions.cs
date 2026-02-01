namespace ServerTest.Options
{
    /// <summary>
    /// 监控窗口相关配置
    /// </summary>
    public sealed class MonitoringOptions
    {
        /// <summary>
        /// 启动监控日志最大条数
        /// </summary>
        public int MaxLogItems { get; set; } = 1000;

        /// <summary>
        /// 实盘交易日志最大条数
        /// </summary>
        public int MaxTradingLogItems { get; set; } = 300;
    }
}
