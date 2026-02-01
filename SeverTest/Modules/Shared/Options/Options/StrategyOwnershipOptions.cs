namespace ServerTest.Options
{
    /// <summary>
    /// 策略租约与多实例协调配置
    /// </summary>
    public sealed class StrategyOwnershipOptions
    {
        /// <summary>
        /// 是否启用策略租约
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 租约有效期（秒）
        /// </summary>
        public int LeaseSeconds { get; set; } = 30;

        /// <summary>
        /// 续租间隔（秒）
        /// </summary>
        public int RenewIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// 策略状态同步间隔（秒）
        /// </summary>
        public int SyncIntervalSeconds { get; set; } = 15;

        /// <summary>
        /// Redis Key 前缀
        /// </summary>
        public string KeyPrefix { get; set; } = "strategy:lease:";
    }
}
