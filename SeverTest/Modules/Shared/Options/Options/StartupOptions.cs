namespace ServerTest.Options
{
    /// <summary>
    /// 启动流程配置
    /// </summary>
    public sealed class StartupOptions
    {
        /// <summary>
        /// 行情引擎初始化超时（秒）
        /// </summary>
        public int MarketDataInitTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// 策略运行时服务预热等待时间（秒）
        /// </summary>
        public int StrategyRuntimeWarmupSeconds { get; set; } = 2;
    }
}
