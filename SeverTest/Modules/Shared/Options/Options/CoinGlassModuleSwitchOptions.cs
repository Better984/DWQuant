namespace ServerTest.Options
{
    /// <summary>
    /// CoinGlass 相关模块统一入口开关（日历、快讯、指标采集）。
    /// 在 appsettings 中配置是否启用各子功能，便于一处管理。
    /// </summary>
    public sealed class CoinGlassModuleSwitchOptions
    {
        /// <summary>
        /// 是否启用「发现页日历」模块（央行活动/财经事件/经济数据）。
        /// 需同时满足 DiscoverCalendar.Enabled 与本项为 true 时才会拉取。
        /// </summary>
        public bool CalendarEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「发现页快讯/资讯」模块（新闻 + 快讯）。
        /// 需同时满足 DiscoverFeed.Enabled 与本项为 true 时才会拉取。
        /// </summary>
        public bool FeedEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「贪婪恐慌指数」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool FearGreedEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「比特币现货 ETF 净流入」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool EtfFlowEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「交易对爆仓热力图（模型1）」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool LiquidationHeatmapEnabled { get; set; } = true;
    }
}
