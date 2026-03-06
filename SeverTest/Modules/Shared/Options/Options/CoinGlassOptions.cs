namespace ServerTest.Options
{
    /// <summary>
    /// CoinGlass 第三方数据源配置。
    /// 注意：开发阶段可临时对接第三方非官方聚合商（盗版源），正式上线前必须切换官方源。
    /// </summary>
    public sealed class CoinGlassOptions
    {
        /// <summary>
        /// 数据源模式：`official`（官方）/`pirated_proxy`（第三方非官方聚合商，仅开发联调用）。
        /// </summary>
        public string SourceMode { get; set; } = "official";

        /// <summary>
        /// 是否启用 CoinGlass 数据拉取。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// CoinGlass API 基础地址。
        /// </summary>
        public string BaseUrl { get; set; } = "https://open-api-v4.coinglass.com";

        /// <summary>
        /// CoinGlass API Key。
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// API Key 请求头名称。
        /// 官方通常为 `CG-API-KEY`；第三方聚合商通常为 `X-Api-Key`。
        /// </summary>
        public string ApiKeyHeaderName { get; set; } = "CG-API-KEY";

        /// <summary>
        /// HTTP 超时时间（秒）。
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// 可选路径前缀（如第三方聚合商 V4 需要 `v4`）。
        /// </summary>
        public string RoutePrefix { get; set; } = string.Empty;

        /// <summary>
        /// 贪婪恐慌指数接口路径。
        /// </summary>
        public string FearGreedPath { get; set; } = "/api/index/fear-greed-history";

        /// <summary>
        /// 贪婪恐慌接口每次期望拉取的最大历史点位。
        /// </summary>
        public int FearGreedSeriesLimit { get; set; } = 200;

        /// <summary>
        /// ETF 净流入历史接口模板路径。
        /// 例如：`/api/etf/{asset}/flow-history` 或 `/v4/api/etf/{asset}/flow-history`。
        /// 其中 `{asset}` 会按资产自动替换为 `bitcoin / ethereum / solana / xrp`。
        /// </summary>
        public string EtfFlowPathTemplate { get; set; } = string.Empty;

        /// <summary>
        /// ETF 净流入历史接口基础路径。
        /// 默认保留 BTC 路径；当未配置 `EtfFlowPathTemplate` 时，ETH/SOL/XRP 会基于该路径自动替换资产段。
        /// </summary>
        public string EtfFlowPath { get; set; } = "/api/etf/bitcoin/flow-history";

        /// <summary>
        /// ETF 净流入接口保留的历史点位上限（按最新时间截取）。
        /// </summary>
        public int EtfFlowSeriesLimit { get; set; } = 180;

        /// <summary>
        /// 大户账户数多空比历史接口路径。
        /// </summary>
        public string TopLongShortAccountRatioPath { get; set; } = "/api/futures/top-long-short-account-ratio/history";

        /// <summary>
        /// 大户账户数多空比默认交易所。
        /// </summary>
        public string TopLongShortAccountRatioDefaultExchange { get; set; } = "Binance";

        /// <summary>
        /// 大户账户数多空比默认时间粒度。
        /// 当前前端仅展示 15 分钟级别数据。
        /// </summary>
        public string TopLongShortAccountRatioDefaultInterval { get; set; } = "15m";

        /// <summary>
        /// 大户账户数多空比默认保留点位上限。
        /// </summary>
        public int TopLongShortAccountRatioSeriesLimit { get; set; } = 192;

        /// <summary>
        /// 合约足迹图历史接口路径。
        /// </summary>
        public string FuturesFootprintPath { get; set; } = "/api/futures/volume/footprint-history";

        /// <summary>
        /// 合约足迹图默认交易所。
        /// </summary>
        public string FuturesFootprintDefaultExchange { get; set; } = "Binance";

        /// <summary>
        /// 合约足迹图默认时间粒度。
        /// 当前前端默认展示 15 分钟级别。
        /// </summary>
        public string FuturesFootprintDefaultInterval { get; set; } = "15m";

        /// <summary>
        /// 合约足迹图默认保留 K 线数量。
        /// </summary>
        public int FuturesFootprintSeriesLimit { get; set; } = 96;

        /// <summary>
        /// 灰度持仓列表接口路径。
        /// </summary>
        public string GrayscaleHoldingsPath { get; set; } = "/api/grayscale/holdings-list";

        /// <summary>
        /// 代币解锁列表接口路径。
        /// </summary>
        public string CoinUnlockListPath { get; set; } = "/api/coin/unlock-list";

        /// <summary>
        /// 代币解锁列表默认保留条数。
        /// </summary>
        public int CoinUnlockListTopCount { get; set; } = 24;

        /// <summary>
        /// 代币解锁详情接口路径。
        /// </summary>
        public string CoinVestingPath { get; set; } = "/api/coin/vesting";

        /// <summary>
        /// 代币解锁详情默认币种。
        /// </summary>
        public string CoinVestingDefaultSymbol { get; set; } = "HYPE";

        /// <summary>
        /// 代币解锁详情默认保留的分配列表条数。
        /// </summary>
        public int CoinVestingAllocationLimit { get; set; } = 12;

        /// <summary>
        /// 代币解锁详情默认保留的解锁计划条数。
        /// </summary>
        public int CoinVestingScheduleLimit { get; set; } = 12;

        /// <summary>
        /// 交易对爆仓热力图（模型1）接口路径。
        /// </summary>
        public string LiquidationHeatmapModel1Path { get; set; } = "/api/futures/liquidation/heatmap/model1";

        /// <summary>
        /// 爆仓热力图默认交易所（示例：Binance）。
        /// </summary>
        public string LiquidationHeatmapDefaultExchange { get; set; } = "Binance";

        /// <summary>
        /// 爆仓热力图默认交易对（示例：BTCUSDT）。
        /// </summary>
        public string LiquidationHeatmapDefaultSymbol { get; set; } = "BTCUSDT";

        /// <summary>
        /// 爆仓热力图默认时间范围（示例：3d）。
        /// </summary>
        public string LiquidationHeatmapDefaultRange { get; set; } = "3d";

        /// <summary>
        /// 交易所资产明细接口路径。
        /// </summary>
        public string ExchangeAssetsPath { get; set; } = "/api/exchange/assets";

        /// <summary>
        /// 交易所资产明细默认交易所名称。
        /// </summary>
        public string ExchangeAssetsDefaultExchangeName { get; set; } = "Binance";

        /// <summary>
        /// 交易所资产明细默认保留条数。
        /// </summary>
        public int ExchangeAssetsTopCount { get; set; } = 12;

        /// <summary>
        /// 交易所余额排行接口路径。
        /// </summary>
        public string ExchangeBalanceListPath { get; set; } = "/api/exchange/balance/list";

        /// <summary>
        /// 交易所余额排行默认币种。
        /// </summary>
        public string ExchangeBalanceListDefaultSymbol { get; set; } = "BTC";

        /// <summary>
        /// 交易所余额排行默认保留条数。
        /// </summary>
        public int ExchangeBalanceListTopCount { get; set; } = 12;

        /// <summary>
        /// 交易所余额趋势接口路径。
        /// </summary>
        public string ExchangeBalanceChartPath { get; set; } = "/api/exchange/balance/chart";

        /// <summary>
        /// 交易所余额趋势默认币种。
        /// </summary>
        public string ExchangeBalanceChartDefaultSymbol { get; set; } = "BTC";

        /// <summary>
        /// 交易所余额趋势默认展示的交易所序列条数。
        /// </summary>
        public int ExchangeBalanceChartSeriesTopCount { get; set; } = 5;

        /// <summary>
        /// 交易所余额趋势默认保留的时间点上限。
        /// </summary>
        public int ExchangeBalanceChartPointLimit { get; set; } = 180;

        /// <summary>
        /// Hyperliquid 鲸鱼提醒接口路径。
        /// </summary>
        public string HyperliquidWhaleAlertPath { get; set; } = "/api/hyperliquid/whale-alert";

        /// <summary>
        /// Hyperliquid 鲸鱼提醒默认保留条数。
        /// </summary>
        public int HyperliquidWhaleAlertTopCount { get; set; } = 12;

        /// <summary>
        /// Hyperliquid 鲸鱼持仓接口路径。
        /// </summary>
        public string HyperliquidWhalePositionPath { get; set; } = "/api/hyperliquid/whale-position";

        /// <summary>
        /// Hyperliquid 鲸鱼持仓默认保留条数。
        /// </summary>
        public int HyperliquidWhalePositionTopCount { get; set; } = 12;

        /// <summary>
        /// Hyperliquid 持仓排行接口路径。
        /// </summary>
        public string HyperliquidPositionPath { get; set; } = "/api/hyperliquid/position";

        /// <summary>
        /// Hyperliquid 持仓排行默认币种。
        /// </summary>
        public string HyperliquidPositionDefaultSymbol { get; set; } = "BTC";

        /// <summary>
        /// Hyperliquid 持仓排行默认保留条数。
        /// </summary>
        public int HyperliquidPositionTopCount { get; set; } = 12;

        /// <summary>
        /// Hyperliquid 用户持仓接口路径。
        /// </summary>
        public string HyperliquidUserPositionPath { get; set; } = "/api/hyperliquid/user-position";

        /// <summary>
        /// Hyperliquid 用户持仓默认地址。
        /// </summary>
        public string HyperliquidUserPositionDefaultUserAddress { get; set; } = "0xa5b0edf6b55128e0ddae8e51ac538c3188401d41";

        /// <summary>
        /// Hyperliquid 钱包持仓分布接口路径。
        /// </summary>
        public string HyperliquidWalletPositionDistributionPath { get; set; } = "/api/hyperliquid/wallet/position-distribution";

        /// <summary>
        /// Hyperliquid 钱包持仓分布默认保留条数。
        /// </summary>
        public int HyperliquidWalletPositionDistributionTopCount { get; set; } = 12;

        /// <summary>
        /// Hyperliquid 钱包盈亏分布接口路径。
        /// </summary>
        public string HyperliquidWalletPnlDistributionPath { get; set; } = "/api/hyperliquid/wallet/pnl-distribution";

        /// <summary>
        /// Hyperliquid 钱包盈亏分布默认保留条数。
        /// </summary>
        public int HyperliquidWalletPnlDistributionTopCount { get; set; } = 12;

        /// <summary>
        /// 是否启用“平台推送”WS 拉流桥（ws://.../ws/stream）。
        /// </summary>
        public bool EnableStreamWsBridge { get; set; } = false;

        /// <summary>
        /// 平台推送 WS 地址（第三方聚合商提供）。
        /// </summary>
        public string StreamWsUrl { get; set; } = "ws://coindata.trainee.host/ws/stream";

        /// <summary>
        /// 平台推送订阅频道列表。
        /// </summary>
        public List<string> StreamChannels { get; set; } = new()
        {
            "funding-rate",
            "open-interest",
            "liquidation",
            "long-short-ratio"
        };

        /// <summary>
        /// WS 断线重连间隔（秒）。
        /// </summary>
        public int WsReconnectDelaySeconds { get; set; } = 5;

        /// <summary>
        /// WS 频道缓存有效期（秒）。
        /// </summary>
        public int WsChannelCacheSeconds { get; set; } = 120;
    }
}
