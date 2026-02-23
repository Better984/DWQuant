namespace ServerTest.Options
{
    /// <summary>
    /// CoinGlass 第三方数据源配置。
    /// </summary>
    public sealed class CoinGlassOptions
    {
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
        /// HTTP 超时时间（秒）。
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// 贪婪恐慌指数接口路径。
        /// </summary>
        public string FearGreedPath { get; set; } = "/api/index/fear-greed-history";

        /// <summary>
        /// 贪婪恐慌接口每次期望拉取的最大历史点位。
        /// </summary>
        public int FearGreedSeriesLimit { get; set; } = 200;
    }
}
