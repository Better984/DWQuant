namespace ServerTest.Options
{
    /// <summary>
    /// 行情查询与拉取相关配置
    /// </summary>
    public sealed class MarketDataQueryOptions
    {
        /// <summary>
        /// 默认批次大小（历史下载入库）
        /// </summary>
        public int DefaultBatchSize { get; set; } = 2000;

        /// <summary>
        /// 单次请求最大限制（交易所 API）
        /// </summary>
        public int MaxLimitPerRequest { get; set; } = 1000;

        /// <summary>
        /// 实时行情缓存长度（每周期缓存的 K 线数量）
        /// </summary>
        public int CacheHistoryLength { get; set; } = 2000;
    }
}
