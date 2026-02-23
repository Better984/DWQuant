namespace ServerTest.Options
{
    /// <summary>
    /// 指标框架配置。
    /// </summary>
    public sealed class IndicatorFrameworkOptions
    {
        /// <summary>
        /// 是否启用指标框架。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否在启动时自动创建指标相关表。
        /// </summary>
        public bool AutoCreateSchema { get; set; } = true;

        /// <summary>
        /// 是否在启动时自动写入默认指标定义。
        /// </summary>
        public bool AutoSeedDefinitions { get; set; } = true;

        /// <summary>
        /// 后台扫描刷新间隔（秒）。
        /// </summary>
        public int RefreshScanIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// 指标定义内存缓存刷新间隔（秒）。
        /// </summary>
        public int DefinitionReloadSeconds { get; set; } = 30;

        /// <summary>
        /// 单次历史查询最大点位数。
        /// </summary>
        public int MaxHistoryQueryPoints { get; set; } = 500;

        /// <summary>
        /// Redis 缓存最短 TTL（秒）。
        /// </summary>
        public int RedisCacheSeconds { get; set; } = 120;

        /// <summary>
        /// 允许返回过期数据的容忍时间（秒）。
        /// </summary>
        public int StaleToleranceSeconds { get; set; } = 300;

        /// <summary>
        /// 历史数据清理检查间隔（分钟）。
        /// </summary>
        public int HistoryCleanupIntervalMinutes { get; set; } = 60;
    }
}
