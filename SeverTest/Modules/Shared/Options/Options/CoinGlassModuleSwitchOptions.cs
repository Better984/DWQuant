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
        /// 是否启用「灰度持仓」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool GrayscaleHoldingsEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「代币解锁列表」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool CoinUnlockListEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「代币解锁详情」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool CoinVestingEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「大户账户数多空比」指标采集。
        /// 当前默认采集 Binance 的 BTC / ETH 15 分钟级别数据。
        /// </summary>
        public bool TopLongShortAccountRatioEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「合约足迹图」指标采集。
        /// 当前默认采集 Binance 的 BTC / ETH 15 分钟级别数据。
        /// </summary>
        public bool FuturesFootprintEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「交易对爆仓热力图（模型1）」指标采集。
        /// 需同时满足 CoinGlass.Enabled 与本项为 true 时才会注册并拉取。
        /// </summary>
        public bool LiquidationHeatmapEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「交易所资产明细」指标采集。
        /// </summary>
        public bool ExchangeAssetEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「交易所余额排行」指标采集。
        /// </summary>
        public bool ExchangeBalanceListEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「交易所余额趋势」指标采集。
        /// </summary>
        public bool ExchangeBalanceChartEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「Hyperliquid 鲸鱼提醒」指标采集。
        /// </summary>
        public bool HyperliquidWhaleAlertEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「Hyperliquid 鲸鱼持仓」指标采集。
        /// </summary>
        public bool HyperliquidWhalePositionEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「Hyperliquid 持仓排行」指标采集。
        /// </summary>
        public bool HyperliquidPositionEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「Hyperliquid 用户持仓」指标采集。
        /// </summary>
        public bool HyperliquidUserPositionEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「Hyperliquid 钱包持仓分布」指标采集。
        /// </summary>
        public bool HyperliquidWalletPositionDistributionEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用「Hyperliquid 钱包盈亏分布」指标采集。
        /// </summary>
        public bool HyperliquidWalletPnlDistributionEnabled { get; set; } = true;
    }
}
