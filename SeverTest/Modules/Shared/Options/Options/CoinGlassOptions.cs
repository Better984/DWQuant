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
        /// 比特币现货 ETF 净流入历史接口路径。
        /// </summary>
        public string EtfFlowPath { get; set; } = "/api/etf/bitcoin/flow-history";

        /// <summary>
        /// ETF 净流入接口保留的历史点位上限（按最新时间截取）。
        /// </summary>
        public int EtfFlowSeriesLimit { get; set; } = 180;

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
